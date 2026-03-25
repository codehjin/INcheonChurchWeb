using Google.Cloud.Vision.V1;
using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace INcheonChurchWeb.Services
{
    public class OcrResult
    {
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
        public string Description { get; set; } = "";
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public class OcrService
    {
        private readonly AppDbContext _dbContext;
        private readonly IWebHostEnvironment _env;

        public OcrService(AppDbContext dbContext, IWebHostEnvironment env)
        {
            _dbContext = dbContext;
            _env = env;

            string keyPath = Path.Combine(_env.ContentRootPath, "google-vision-key.json");
            if (File.Exists(keyPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", keyPath);
            }
        }

        public async Task<OcrResult> ProcessReceiptAsync(byte[] imageBytes)
        {
            var result = new OcrResult();
            string currentMonth = DateTime.Now.ToString("yyyy-MM");

            var usage = await _dbContext.OcrUsages.FirstOrDefaultAsync(u => u.YearMonth == currentMonth);
            if (usage != null && usage.UsageCount >= 1000)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "이번 달 무료 자동인식(OCR) 제공량(1,000건)을 모두 소진했습니다. 수동으로 입력해 주세요.";
                return result;
            }

            try
            {
                var client = ImageAnnotatorClient.Create();
                var image = Image.FromBytes(imageBytes);
                var response = await client.DetectTextAsync(image);

                if (response == null || response.Count == 0)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "영수증에서 글자를 찾지 못했습니다. 사진을 다시 찍어주세요.";
                    return result;
                }

                string fullText = response[0].Description;
                string noSpaceText = Regex.Replace(fullText, @"\s+", ""); // 줄바꿈을 포함한 모든 공백 완벽 제거
                string[] lines = fullText.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

                // ==============================================================
                // 🚀 [1] 날짜 추출 로직
                // ==============================================================
                DateTime? foundDate = null;
                var possibleDates = new List<DateTime>();

                var dateMatches = Regex.Matches(fullText, @"(20[1-3]\d|\d{2})[\s\-\./년]+(\d{1,2})[\s\-\./월]+(\d{1,2})");

                foreach (Match m in dateMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int y) &&
                        int.TryParse(m.Groups[2].Value, out int month) &&
                        int.TryParse(m.Groups[3].Value, out int day))
                    {
                        if (y < 100) y += 2000;
                        try
                        {
                            var tempDate = new DateTime(y, month, day);
                            if (tempDate <= DateTime.Now.AddMonths(1) && tempDate > DateTime.Now.AddYears(-2))
                            {
                                possibleDates.Add(tempDate);
                            }
                        }
                        catch { }
                    }
                }

                if (possibleDates.Any())
                {
                    foundDate = possibleDates.OrderByDescending(d => d).FirstOrDefault(d => d <= DateTime.Now.AddDays(1));
                    if (foundDate == default || foundDate == DateTime.MinValue) foundDate = possibleDates.First();
                    result.Date = foundDate;
                }

                // ==============================================================
                // 🚀 [2] 금액 추출 로직 (한글 '원' 글자 찰싹 붙음 문제 해결 패치)
                // ==============================================================
                decimal finalAmount = 0;

                // 1순위: 명확한 키워드 옆에 있는 숫자 (줄바꿈이 있어도 찾도록 noSpaceText 사용)
                var labelAmountMatches = Regex.Matches(noSpaceText, @"(총액|합계|결제금액|승인금액|청구금액|받을금액|판매금액|실승인금액|총결제금액|대상금액)[:=]?([1-9]\d{0,2}(?:,\d{3})+|[1-9]\d{3,7})");
                foreach (Match m in labelAmountMatches)
                {
                    if (decimal.TryParse(m.Groups[2].Value.Replace(",", ""), out decimal amt))
                    {
                        if (amt > finalAmount && amt < 10000000) finalAmount = amt;
                    }
                }

                // 2순위: 콤마(,)가 들어간 모든 숫자 찾기 (가장 강력함)
                // [핵심] '32,000원' 처럼 글자가 붙어있어도 숫자만 귀신같이 빼옵니다!
                var commaNumbers = Regex.Matches(fullText, @"(?<!\d)[1-9]\d{0,2}(,\d{3})+(?!\d)");
                foreach (Match m in commaNumbers)
                {
                    if (decimal.TryParse(m.Value.Replace(",", ""), out decimal amt))
                    {
                        if (amt > finalAmount && amt < 10000000) finalAmount = amt;
                    }
                }

                // 3순위: 콤마가 없는 옛날 영수증 방어
                if (finalAmount == 0)
                {
                    var keywordMatches = Regex.Matches(noSpaceText, @"(합계|금액|결제|승인|청구|받을|카드|계)[:=]?([1-9]\d{3,7})(?!\d)");
                    foreach (Match m in keywordMatches)
                    {
                        if (decimal.TryParse(m.Groups[2].Value, out decimal amt))
                        {
                            if (amt > finalAmount && amt < 10000000) finalAmount = amt;
                        }
                    }
                }

                if (finalAmount > 0) result.Amount = finalAmount;

                // ==============================================================
                // 🚀 [3] 가맹점(사용처) 추출 로직
                // ==============================================================
                string storeName = "";

                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"(?:상호명?|가맹점명?|매장명|업소명)[\s:\|]*(.+)");
                    if (match.Success && match.Groups[1].Value.Trim().Length > 1)
                    {
                        storeName = match.Groups[1].Value.Trim();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(storeName))
                {
                    string[] goodSuffixes = { "점", "본점", "식당", "카페", "마트", "다이소", "슈퍼", "약국", "병원", "의원", "상회", "점포", "파스타", "돈까스", "베이커리", "커피", "라멘" };
                    foreach (var line in lines)
                    {
                        if (line.Length > 20) continue;
                        if (goodSuffixes.Any(suffix => line.EndsWith(suffix) || line.Contains(suffix + " ")))
                        {
                            if (!line.Contains("가맹점") && !line.Contains("대표") && !line.Contains("주소") && line.Length > 2)
                            {
                                storeName = line; break;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(storeName))
                {
                    string[] ignoreWords = { "영수증", "매출", "전표", "고객", "번호", "주소", "대표", "사업자", "TEL", "전화", "카드", "승인", "포스", "POS", "결제", "취소", "재발행", "내역", "할부", "가맹", "일자", "시간", "품명", "수량", "단가", "금액", "합계", "부가세", "공급가", "주문" };
                    foreach (var line in lines.Take(20))
                    {
                        if (Regex.IsMatch(line, @"^\d+$")) continue;
                        if (Regex.IsMatch(line, @"^[a-zA-Z0-9\s\.\,\:\-\/]+$")) continue;
                        if (ignoreWords.Any(w => line.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        if (Regex.IsMatch(line, @"\d{4}-\d{2}-\d{2}")) continue;
                        if (line.Length <= 2 || line.Length > 20) continue;
                        if (line.Contains("(") && line.EndsWith(")")) continue;

                        storeName = line; break;
                    }
                }

                if (!string.IsNullOrEmpty(storeName))
                {
                    result.Description = Regex.Replace(storeName, @"^[^a-zA-Z0-9가-힣]+|[^a-zA-Z0-9가-힣]+$", "").Trim();
                    if (string.IsNullOrWhiteSpace(result.Description)) result.Description = storeName.Trim();
                }

                // 4. 성공 시 사용량 카운트 1 증가
                if (usage == null)
                {
                    usage = new OcrUsage { YearMonth = currentMonth, UsageCount = 1 };
                    _dbContext.OcrUsages.Add(usage);
                }
                else
                {
                    usage.UsageCount++;
                }
                await _dbContext.SaveChangesAsync();

                result.IsSuccess = true;
            }
            catch (Exception)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "인식 중 오류가 발생했습니다.";
            }

            return result;
        }
    }
}