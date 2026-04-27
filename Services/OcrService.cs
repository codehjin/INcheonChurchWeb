using Google.Apis.Auth.OAuth2;
using Google.Cloud.Vision.V1;
using INcheonChurchWeb.Models;
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
        private readonly string[] DateKeywords = { "결제일시", "승인일시", "결제일자", "거래일시", "거래일자", "판매일시", "일자", "주문일시", "결제일", "판매일", "승인일" };
        private readonly string[] AmountKeywords = { "총결제금액", "결제금액", "승인금액", "합계금액", "받을금액", "판매총액", "이체금액", "청구금액", "결제합계", "카드결제", "판매합계", "받은금액", "과세합계", "총액", "합계" };

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

        private DateTime? ExtractDate(string[] lines, string fullText)
        {
            // 연도 2자리(24~39) 또는 4자리(2024~2039) 대응, 다양한 구분자(- . / 년 월 일) 허용
            var dateRegex = new Regex(@"(20[2-3][0-9]|[2-3][0-9])[\-\./년\s]+(0?[1-9]|1[0-2])[\-\./월\s]+(0?[1-9]|[12][0-9]|3[01])[일\s]*");

            // 1순위: '결제일시' 등 키워드가 있는 줄 먼저 검색
            foreach (var line in lines)
            {
                if (DateKeywords.Any(k => line.Replace(" ", "").Contains(k)))
                {
                    var match = dateRegex.Match(line);
                    if (match.Success) return ParseDateString(match);
                }
            }

            // 2순위: 키워드가 없어도 전체 텍스트에서 날짜 패턴이 보이면 가져옴
            var fallbackMatch = dateRegex.Match(fullText);
            if (fallbackMatch.Success) return ParseDateString(fallbackMatch);

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
            // 1, 2순위용: 키워드 근처에 있는 숫자는 콤마나 '원'이 없어도 인정 (단, 10원 이상)
            var looseNumRegex = new Regex(@"([1-9][0-9]{0,2}(,[0-9]{3})+|[1-9][0-9]{1,})\s*원?");

            foreach (var line in lines)
            {
                string cleanLine = line.Replace(" ", "");
                if (AmountKeywords.Any(k => cleanLine.Contains(k)))
                {
                    // 1순위: 키워드와 같은 줄 검색
                    var matches = looseNumRegex.Matches(cleanLine);
                    if (matches.Count > 0)
                    {
                        string numStr = matches.Last().Groups[1].Value.Replace(",", "");
                        if (decimal.TryParse(numStr, out decimal amt)) return amt;
                    }

                    // 2순위: 아래 2줄 안에서 검색 (모바일 영수증 줄바꿈 대응)
                    for (int j = 1; j <= 2; j++)
                    {
                        int targetIdx = Array.IndexOf(lines, line) + j;
                        if (targetIdx < lines.Length)
                        {
                            var nextMatches = looseNumRegex.Matches(lines[targetIdx].Replace(" ", ""));
                            if (nextMatches.Count > 0)
                            {
                                string numStr = nextMatches.Last().Groups[1].Value.Replace(",", "");
                                if (decimal.TryParse(numStr, out decimal amt)) return amt;
                            }
                        }
                    }
                }
            }

            // 🚀 3순위 (최후의 보루): 영수증 전체에서 가장 큰 금액 찾기
            // 승인번호 오인식을 막기 위해 반드시 '콤마(,)'가 있거나 '원'으로 끝나는 숫자만 금액으로 인정!
            decimal maxAmount = 0;

            // 패턴 A: '원'으로 끝나는 100원 이상 숫자 (예: 15000원, 15,000원)
            var strictNumRegex = new Regex(@"([1-9][0-9]{0,2}(,[0-9]{3})+|[1-9][0-9]{2,})\s*원");
            foreach (Match match in strictNumRegex.Matches(fullText.Replace(" ", "")))
            {
                string numStr = match.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(numStr, out decimal amt) && amt > maxAmount) maxAmount = amt;
            }

            // 패턴 B: 콤마가 포함된 1,000 이상 숫자 (예: 15,000)
            var commaNumRegex = new Regex(@"([1-9][0-9]{0,2}(,[0-9]{3})+)");
            foreach (Match match in commaNumRegex.Matches(fullText.Replace(" ", "")))
            {
                string numStr = match.Value.Replace(",", "");
                if (decimal.TryParse(numStr, out decimal amt) && amt > maxAmount) maxAmount = amt;
            }

            if (maxAmount > 0) return maxAmount;

            return null;
        }
    }
}