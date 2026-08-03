using Google.Apis.Auth.OAuth2;
using Google.Cloud.Vision.V1;
using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace INcheonChurchWeb.Services
{
    public class OcrResult
    {
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
        public string Description { get; set; } = ""; // 내역은 비워둡니다.
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class OcrService
    {
        // 💡 오직 날짜와 금액 관련 키워드만 남겼습니다.
        // 🚀 날짜 키워드 — 구체적인 것부터. 짧은 '일자'는 오탐이 많아 뒤로 뺀다.
        private readonly string[] _dateKeywords = {
            "결제일시", "결제일자", "승인일시", "승인일자", "거래일시", "거래일자",
            "판매일시", "주문일시", "계산일자", "결제일", "승인일", "판매일", "거래일", "일자"
        };

        // 🚀 금액 키워드 — 배열 순서가 곧 '우선순위'다.
        //   할인·멤버십이 적용된 뒤의 '실제 카드 결제액'을 가장 앞에 둔다.
        //   (예: GS더프레시는 총합계 143,910 / 결제금액 123,520 / 카드결제 120,520 → 정답은 120,520)
        //   '과세합계·총액·합계'처럼 오인 소지가 큰 키워드는 맨 뒤에 둔다.
        private readonly string[] _amountKeywords = {
            "카드결제", "신용카드", "총결제금액", "결제금액", "승인금액", "합계금액",
            "청구금액", "받을금액", "판매총액", "이체금액", "결제합계", "판매합계",
            "받은금액", "총액", "합계", "과세합계"
        };

        // 🚀 Vision API 사용량(이번 달 카운트) 기록을 위한 DB 팩토리
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public OcrService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<OcrResult> ProcessReceiptAsync(byte[] imageBytes)
        {
            var result = new OcrResult();
            try
            {
                string keyPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "google-vision-key.json");
                ImageAnnotatorClient client;

                if (System.IO.File.Exists(keyPath))
                {
                    var clientBuilder = new ImageAnnotatorClientBuilder
                    {
                        // 🚀 파일이 '서비스 계정' 임을 명시하고, 안전하게 팩토리를 통해 변환합니다.
                        GoogleCredential = CredentialFactory.FromFile<ServiceAccountCredential>(keyPath).ToGoogleCredential()
                    };
                    client = await clientBuilder.BuildAsync();
                }
                else
                {
                    client = await ImageAnnotatorClient.CreateAsync();
                }

                var image = Image.FromBytes(imageBytes);
                var response = await client.DetectTextAsync(image);

                // 🚀 Vision API 호출이 (예외 없이) 성공했으므로 이번 달 사용량 카운트를 1 증가시킵니다.
                // 글자 인식 결과(날짜/금액)와 무관하게 API 호출 자체가 과금 대상이므로 여기서 집계합니다.
                await IncrementUsageAsync();

                if (response == null || !response.Any())
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "이미지에서 글자를 인식하지 못했습니다.";
                    return result;
                }

                string fullText = response.First().Description;
                string[] lines = fullText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                // 🚀 1. 날짜 추출 (집중 타겟팅)
                result.Date = ExtractDate(lines, fullText);

                // 🚀 2. 금액 추출 (집중 타겟팅)
                result.Amount = ExtractAmount(lines, fullText);

                // 상호명/내역은 추출하지 않으므로, 날짜와 금액 중 하나라도 있으면 성공으로 간주
                if (result.Date.HasValue || result.Amount.HasValue)
                {
                    result.IsSuccess = true;
                }
                else
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "글자는 인식했으나 날짜와 금액을 특정하지 못했습니다. 수동으로 입력해주세요.";
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"OCR 처리 오류: {ex.Message}";
            }

            return result;
        }

        // 🚀 이번 달(yyyy-MM) Vision API 사용량을 DB에서 가져와 +1 하고 저장합니다.
        // 카운트 집계 실패가 OCR 기능 자체를 막지 않도록 예외는 삼킵니다.
        private async Task IncrementUsageAsync()
        {
            try
            {
                string currentMonth = DateTime.Now.ToString("yyyy-MM");
                using var context = _dbFactory.CreateDbContext();

                var usage = await context.OcrUsages.FirstOrDefaultAsync(u => u.YearMonth == currentMonth);
                if (usage == null)
                {
                    context.OcrUsages.Add(new OcrUsage { YearMonth = currentMonth, UsageCount = 1 });
                }
                else
                {
                    usage.UsageCount++;
                }
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR 사용량 집계 실패(무시): {ex.Message}");
            }
        }

        private DateTime? ExtractDate(string[] lines, string fullText)
        {
            // 🚀 시각(hh:mm[:ss])을 먼저 걷어낸다.
            //   "2026-07-12 20:45:53"처럼 날짜 뒤에 시각이 붙으면
            //   일(日) 자리에서 "12"가 아니라 "1"만 읽히는 문제가 있었다.
            var timeRegex = new Regex(@"(?<=[0-9])\s*[0-2]?[0-9]:[0-5][0-9](:[0-5][0-9])?");
            string cleanedFull = timeRegex.Replace(fullText, " ");

            // 두 자리(1[0-2], 3[01], [12][0-9])를 한 자리보다 먼저 시도해야
            // 12월·31일 같은 값이 잘리지 않는다.
            var dateRegex = new Regex(@"(20[2-3][0-9]|[2-3][0-9])[\-\./년\s]+(1[0-2]|0?[1-9])[\-\./월\s]+(3[01]|[12][0-9]|0?[1-9])");

            // 공백 제거본과 원본을 모두 후보로 둔다(영수증마다 띄어쓰기가 제각각)
            var candidates = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                                  .Select(l => timeRegex.Replace(l, " "))
                                  .SelectMany(l => new[] { l.Replace(" ", ""), l })
                                  .ToArray();

            // 🚀 1순위: 날짜 키워드를 '우선순위 순'으로 훑는다.
            foreach (var keyword in _dateKeywords)
            {
                foreach (var line in candidates)
                {
                    int idx = line.IndexOf(keyword, StringComparison.Ordinal);
                    if (idx < 0) continue;

                    var m = dateRegex.Match(line.Substring(idx + keyword.Length));
                    if (!m.Success) m = dateRegex.Match(line);
                    if (m.Success)
                    {
                        var d = ParseDateString(m);
                        if (d.HasValue) return d;
                    }
                }
            }

            // 2순위: 키워드가 없으면 전체 텍스트의 첫 날짜
            var fallbackMatch = dateRegex.Match(cleanedFull);
            if (fallbackMatch.Success) return ParseDateString(fallbackMatch);

            // 🚀 3순위: yyyymmdd 8자리 연속 숫자.
            //   쇼핑몰 주문완료 화면처럼 날짜 표기 없이
            //   "주문번호: 20260526-0000161" 안에만 날짜가 있는 경우를 구제한다.
            var eightDigit = Regex.Match(cleanedFull.Replace(" ", ""),
                                         @"(20[2-3][0-9])(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])");
            if (eightDigit.Success)
            {
                try
                {
                    return new DateTime(int.Parse(eightDigit.Groups[1].Value),
                                        int.Parse(eightDigit.Groups[2].Value),
                                        int.Parse(eightDigit.Groups[3].Value));
                }
                catch { /* 유효하지 않은 날짜면 무시 */ }
            }

            return null;
        }

        private DateTime? ParseDateString(Match match)
        {
            try
            {
                int year = int.Parse(match.Groups[1].Value);
                if (year < 100) year += 2000; // '26' -> '2026' 자동 보정
                int month = int.Parse(match.Groups[2].Value);
                int day = int.Parse(match.Groups[3].Value);
                return new DateTime(year, month, day);
            }
            catch { return null; }
        }

        private decimal? ExtractAmount(string[] lines, string fullText)
        {
            // 공백 제거한 줄 목록 (영수증은 공백이 불규칙해 비교 전 제거)
            var flat = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Replace(" ", ""))
                            .ToArray();

            // 🚀 1순위: 키워드 '우선순위 순'으로 전체를 훑는다.
            //   기존에는 줄 순서로 훑어서, 영수증 위쪽의 '과세합계'가
            //   아래쪽의 '결제금액'보다 먼저 잡히는 문제가 있었다.
            foreach (var keyword in _amountKeywords)
            {
                foreach (var line in flat)
                {
                    int idx = line.IndexOf(keyword, StringComparison.Ordinal);
                    if (idx < 0) continue;

                    // 키워드 '뒤쪽'만 본다 (앞의 품목 단가 등을 배제)
                    string tail = line.Substring(idx + keyword.Length);
                    decimal? found = PickAmountFrom(tail);
                    if (found.HasValue) return found;

                    // 모바일 영수증처럼 값이 다음 줄에 오는 경우 2줄까지 확인
                    int pos = Array.IndexOf(flat, line);
                    for (int j = 1; j <= 2 && pos >= 0 && pos + j < flat.Length; j++)
                    {
                        found = PickAmountFrom(flat[pos + j]);
                        if (found.HasValue) return found;
                    }
                }
            }

            // 🚀 2순위(최후의 보루): 콤마가 있는 숫자 중 가장 큰 값.
            //   승인번호·카드번호 오인식을 막기 위해 콤마 표기만 인정한다.
            //   단, '잔액·포인트'처럼 결제액이 아닌 줄은 제외한다(은행 앱 화면 대응).
            var excludeWords = new[] { "잔액", "포인트", "적립", "한도", "누적" };
            decimal maxAmount = 0;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.Replace(" ", "");
                if (excludeWords.Any(w => line.Contains(w))) continue;

                foreach (Match m in Regex.Matches(line, @"[1-9][0-9]{0,2}(?:,[0-9]{3})+"))
                {
                    if (decimal.TryParse(m.Value.Replace(",", ""), out decimal amt) && amt > maxAmount)
                        maxAmount = amt;
                }
            }

            return maxAmount > 0 ? maxAmount : (decimal?)null;
        }

        /// <summary>
        /// 문자열에서 금액을 고른다.
        /// 콤마가 있는 수(금액 표기)를 우선하고, 없으면 7자리 이하 숫자를 쓴다.
        /// (카드번호·승인번호처럼 8자리 이상인 수는 금액으로 보지 않는다)
        /// </summary>
        private decimal? PickAmountFrom(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // 1) 콤마 표기 우선 — 예: 557,300
            var commaMatches = Regex.Matches(text, @"[1-9][0-9]{0,2}(?:,[0-9]{3})+");
            if (commaMatches.Count > 0)
            {
                string s = commaMatches[commaMatches.Count - 1].Value.Replace(",", "");
                if (decimal.TryParse(s, out decimal v)) return v;
            }

            // 2) 🚀 공백으로 자릿수를 구분한 경우 — 예: "₩53 500" (앱 결제 화면에 흔함)
            //    줄바꿈을 넘지 않도록 일반 공백만 허용한다.
            var spaceMatches = Regex.Matches(text, @"[1-9][0-9]{0,2}(?:[ \u00a0][0-9]{3})+(?![0-9])");
            if (spaceMatches.Count > 0)
            {
                string s = Regex.Replace(spaceMatches[spaceMatches.Count - 1].Value, @"[ \u00a0]", "");
                if (decimal.TryParse(s, out decimal v)) return v;
            }

            // 3) 콤마가 없으면 2~7자리 숫자 (10원 미만·8자리 이상 제외)
            var plainMatches = Regex.Matches(text, @"(?<![0-9,])[1-9][0-9]{1,6}(?![0-9,])");
            if (plainMatches.Count > 0)
            {
                string s = plainMatches[plainMatches.Count - 1].Value;
                if (decimal.TryParse(s, out decimal v)) return v;
            }

            return null;
        }
    }
}