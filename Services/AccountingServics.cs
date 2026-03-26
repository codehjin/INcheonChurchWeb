using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms; // 파일 업로드용
using Microsoft.AspNetCore.Hosting; // 경로 확인용
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing; // 리사이징용
using SixLabors.ImageSharp.Formats.Jpeg; // 압축용

namespace INcheonChurchWeb.Services
{
    // [DTO] 대시보드 부서별 통합 통계용
    public class DeptStat
    {
        public string DeptName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public decimal Budget { get; set; }
        public decimal Spent { get; set; }
        public decimal Balance { get; set; }
        public int Rate { get; set; }
    }

    // [DTO] 단일 부서 통계용
    public class StatItem { public string Category { get; set; } = ""; public decimal Budget { get; set; } public decimal Spent { get; set; } }

    // 🚀 1. 기존 public class 대신 public partial class 하나만 남깁니다.
    public partial class AccountingService
    {
        // 🚀 2. 정규식 생성기는 반드시 클래스의 '{' 안쪽에 위치해야 합니다.
        [GeneratedRegex(@"[\\/:*?""<>|]")]
        private static partial Regex InvalidFileNameChars();

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AccountingService(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }
        // =========================================================
        // [1-1] 보조금 현황 계산 로직
        // =========================================================
        // 🚀 11월 3째주 주일을 구하는 헬퍼 메서드 추가
        public static DateTime GetThirdSundayOfNovember(int year)
        {
            DateTime nov1 = new DateTime(year, 11, 1);
            int daysToSunday = ((int)DayOfWeek.Sunday - (int)nov1.DayOfWeek + 7) % 7;
            DateTime firstSunday = nov1.AddDays(daysToSunday);
            return firstSunday.AddDays(14); // 첫째 주일 + 14일 = 셋째 주일
        }

        // 🚀 분기 날짜 로직 업데이트
        public async Task<(DateTime Start, DateTime End)> GetQuarterDateRangeAsync(int deptId, int year, int quarter)
        {
            string key = $"Quarter_{year}_Q{quarter}";
            var setting = await _db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.DepartmentId == deptId && m.Keyword == key);

            // 1. DB에 설정된 분기값이 있으면 우선 적용
            if (setting != null && setting.Category.Contains('~'))
            {
                var parts = setting.Category.Split('~');
                if (DateTime.TryParse(parts[0], out DateTime s) && DateTime.TryParse(parts[1], out DateTime e))
                {
                    return (s, e);
                }
            }

            // 2. 설정이 없을 경우 새로운 기준(11월~11월)에 따른 기본값 계산
            DateTime prevNovThirdSun = GetThirdSundayOfNovember(year - 1);
            DateTime q1Start = prevNovThirdSun.AddDays(1); // 전년도 11월 4째주 월요일 시작
            DateTime currentNovThirdSun = GetThirdSundayOfNovember(year); // 당해년도 11월 3째주 주일 마감

            return quarter switch
            {
                1 => (q1Start, new DateTime(year, 2, DateTime.DaysInMonth(year, 2))),
                2 => (new DateTime(year, 3, 1), new DateTime(year, 5, 31)),
                3 => (new DateTime(year, 6, 1), new DateTime(year, 8, 31)),
                4 => (new DateTime(year, 9, 1), currentNovThirdSun),
                _ => (q1Start, currentNovThirdSun) // 전체 (회계연도 전체)
            };
        }
        // =========================================================
        // [1-2] 분기 설정: 날짜 범위 저장 및 조회
        // =========================================================

        public async Task SaveQuarterSettingAsync(int deptId, int year, int quarter, DateTime start, DateTime end)
        {
            string key = $"Quarter_{year}_Q{quarter}";
            string rangeStr = $"{start:yyyy-MM-dd}~{end:yyyy-MM-dd}";

            var setting = await _db.CategoryMappings.FirstOrDefaultAsync(m => m.DepartmentId == deptId && m.Keyword == key);
            if (setting == null)
            {
                _db.CategoryMappings.Add(new CategoryMapping { DepartmentId = deptId, Keyword = key, Category = rangeStr });
            }
            else
            {
                setting.Category = rangeStr;
            }
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // [1-3] 월별장부 - 기간별 조회
        // =========================================================
        public async Task<List<LedgerEntry>> GetLedgerByOptionAsync(int deptId, int year, string option, int value)
        {
            var query = _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId);

            if (option == "Month") query = query.Where(t => t.FiscalYear == year && t.Date.Month == value);
            else if (option == "Quarter")
            {
                var range = await GetQuarterDateRangeAsync(deptId, year, value);
                query = query.Where(t => t.Date >= range.Start && t.Date <= range.End);
            }
            else query = query.Where(t => t.FiscalYear == year);

            return await query.OrderBy(t => t.Date).ToListAsync();
        }

        public async Task<List<string>> GetAllCategoriesAsync(int deptId)
        {
            var budgetCats = await _db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).Select(b => b.Category).ToListAsync();
            var ledgerCats = await _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).Select(t => t.Category).ToListAsync();
            return budgetCats.Union(ledgerCats).Where(c => !string.IsNullOrEmpty(c) && c != "미분류").Distinct().OrderBy(c => c).ToList();
        }

        public async Task BulkUpdateCategoryAsync(List<int> ids, string newCategory)
        {
            var targets = await _db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync();
            foreach (var item in targets) { item.Category = newCategory; }
            await _db.SaveChangesAsync();
        }

        public async Task<List<BudgetPlan>> GetAllBudgetPlansForDeptAsync(int deptId, string type)
        {
            return await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Type == type)
                .OrderByDescending(b => b.Year).ThenBy(b => b.Category).ToListAsync();
        }

        // =========================================================
        // 1. 대시보드 통계 (SQLite 호환성 보완)
        // =========================================================
        public async Task<List<DeptStat>> GetIntegratedDashboardAsync(int year)
        {
            var trans = await _db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();
            var budgets = await _db.BudgetPlans.AsNoTracking().Where(b => b.Year == year && b.Type == "Expense").ToListAsync();

            // 모든 부서를 DB에서 가져와서 처리합니다.
            var depts = await _db.Departments.AsNoTracking().ToListAsync();
            var list = new List<DeptStat>();

            foreach (var d in depts)
            {
                // 특수 부서(관리자 등) 제외
                if (d.Name == "관리자") continue;

                string shortName = d.Name.Length >= 2 ? d.Name[..2] : d.Name;
                decimal budget = budgets.Where(b => b.DepartmentId == d.Id).Sum(b => b.Amount);
                decimal spent = trans.Where(t => t.DepartmentId == d.Id && t.Type == "지출").Sum(t => t.Expense);

                list.Add(new DeptStat { DeptName = d.Name, ShortName = shortName, Budget = budget, Spent = spent, Balance = budget - spent, Rate = budget == 0 ? 0 : (int)(spent / budget * 100) });
            }
            return list;
        }

        public async Task<(decimal TotalIn, decimal TotalOut, List<StatItem> Stats)> GetDashboardDataAsync(int deptId, int year)
        {
            var trans = await _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.FiscalYear == year).ToListAsync();
            var budgets = await _db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Year == year && b.Type == "Expense").ToListAsync();
            var stats = budgets.GroupBy(b => b.Category).Select(g => new StatItem { Category = g.Key, Budget = g.Sum(x => x.Amount), Spent = trans.Where(t => t.Type == "지출" && t.Category == g.Key).Sum(t => t.Expense) }).ToList();
            var unclassified = trans.Where(t => t.Type == "지출" && t.Category == "미분류").Sum(t => t.Expense);
            if (unclassified > 0) stats.Add(new StatItem { Category = "미분류", Budget = 0, Spent = unclassified });
            return (trans.Sum(t => t.Income), trans.Sum(t => t.Expense), stats);
        }

        // =========================================================
        // 2. 영수증 이미지 최적화 업로드 (수정 없음)
        // =========================================================
        public async Task<string> UploadReceiptAsync(IBrowserFile file, int transactionId)
        {
            var entry = await _db.Transactions.FindAsync(transactionId);
            if (entry == null) return "내역을 찾을 수 없습니다.";
            try
            {
                string extension = Path.GetExtension(file.Name).ToLower();
                string safeDesc = InvalidFileNameChars().Replace(entry.Description, "_");
                string newFileName = $"{entry.Date:yyyy-MM-dd}_{entry.Category}_{safeDesc}.jpg";
                string uploadFolder = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);
                string filePath = Path.Combine(uploadFolder, newFileName);

                using var inputStream = file.OpenReadStream(1024 * 1024 * 20);
                {
                    if (extension == ".pdf") { using (var fs = new FileStream(filePath, FileMode.Create)) { await inputStream.CopyToAsync(fs); } }
                    else
                    {
                        using (var image = await Image.LoadAsync(inputStream))
                        {
                            if (image.Width > 1200) image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1200, 0), Mode = ResizeMode.Max }));
                            await image.SaveAsync(filePath, new JpegEncoder { Quality = 75 });
                        }
                    }
                }
                entry.ReceiptPath = $"/uploads/{newFileName}";
                await _db.SaveChangesAsync();
                return "OK";
            }
            catch (Exception ex) { return $"실패: {ex.Message}"; }
        }

        public async Task RemoveReceiptAsync(int id)
        {
            var entry = await _db.Transactions.FindAsync(id);
            if (entry != null)
            {
                if (!string.IsNullOrEmpty(entry.ReceiptPath))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, entry.ReceiptPath.TrimStart('/'));
                    if (File.Exists(fullPath)) { File.Delete(fullPath); }
                }
                entry.ReceiptPath = "";
                await _db.SaveChangesAsync();
            }
        }

        // =========================================================
        // 3. 로그 및 백업 (DataType 수정)
        // =========================================================
        public async Task LogActivityAsync(string username, string action, string details)
        {
            _db.ActivityLogs.Add(new ActivityLog { Username = username, Action = action, Details = details, Timestamp = DateTime.Now });
            await _db.SaveChangesAsync();
        }

        public async Task CreateBackupAsync(int deptId, string type, string memo)
        {
            string jsonData = "";
            if (type == "Ledger") jsonData = JsonSerializer.Serialize(await _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            else if (type == "Budget") jsonData = JsonSerializer.Serialize(await _db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).ToListAsync());
            else if (type == "Mapping") jsonData = JsonSerializer.Serialize(await _db.CategoryMappings.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            _db.DataBackups.Add(new DataBackup { DepartmentId = deptId, DataType = type, Memo = memo, JsonData = jsonData, BackupDate = DateTime.Now });
            await _db.SaveChangesAsync();
        }

        public async Task<List<DataBackup>> GetBackupsAsync(int deptId) => await _db.DataBackups.AsNoTracking().Where(b => b.DepartmentId == deptId).OrderByDescending(b => b.BackupDate).ToListAsync();

        public async Task RestoreFromBackupAsync(int backupId)
        {
            var backup = await _db.DataBackups.FindAsync(backupId);
            if (backup == null) return;
            if (backup.DataType == "Budget") { var old = await _db.BudgetPlans.Where(b => b.DepartmentId == backup.DepartmentId).ToListAsync(); _db.BudgetPlans.RemoveRange(old); var restored = JsonSerializer.Deserialize<List<BudgetPlan>>(backup.JsonData); if (restored != null) _db.BudgetPlans.AddRange(restored); }
            else if (backup.DataType == "Ledger") { var old = await _db.Transactions.Where(t => t.DepartmentId == backup.DepartmentId).ToListAsync(); _db.Transactions.RemoveRange(old); var restored = JsonSerializer.Deserialize<List<LedgerEntry>>(backup.JsonData); if (restored != null) _db.Transactions.AddRange(restored); }
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // 4. 장부 관리 및 자동 분류
        // =========================================================
        public async Task<List<string>> GetCategorySuggestionsAsync(int deptId, string type)
        {
            var fromBudget = await _db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Type == type).Select(b => b.Category).Distinct().ToListAsync();
            var fromLedger = await _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.Type == (type == "Expense" ? "지출" : "수입")).Select(t => t.Category).Distinct().ToListAsync();
            return fromBudget.Union(fromLedger).OrderBy(c => c).ToList();
        }

        public async Task AddTransactionAsync(LedgerEntry entry) { entry.Id = 0; if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; if (string.IsNullOrEmpty(entry.Category)) entry.Category = "미분류"; _db.Transactions.Add(entry); await _db.SaveChangesAsync(); }

        public async Task AddTransactionsAsync(List<LedgerEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            // 유년부 아이디 가져오기 (기본값 세팅용)
            var defaultDept = await _db.Departments.FirstOrDefaultAsync(d => d.Name == "유년부");
            int defaultDeptId = defaultDept?.Id ?? 1;

            foreach (var entry in entries)
            {
                entry.Id = 0;
                if (string.IsNullOrEmpty(entry.Note)) entry.Note = "";
                if (string.IsNullOrEmpty(entry.Category)) entry.Category = "미분류";
                if (entry.DepartmentId == 0) entry.DepartmentId = defaultDeptId; // 수정됨

                int fiscalYear = entry.FiscalYear == 0 ? entry.Date.Year : entry.FiscalYear;
                if (entry.Date.Month == 11 || entry.Date.Month == 12)
                {
                    var (_, end) = await GetQuarterDateRangeAsync(entry.DepartmentId, entry.Date.Year, 4);
                    if (entry.Date > end) fiscalYear = entry.Date.Year + 1;
                }

                int quarter = await GetQuarterNumberAsync(entry.DepartmentId, fiscalYear, entry.Date);
                entry.FiscalYear = fiscalYear;
                entry.Quarter = quarter;
                _db.Transactions.Add(entry);
            }
            await _db.SaveChangesAsync();
        }

        public async Task<DateTime?> GetLastTransactionDateAsync(int deptId)
        {
            return await _db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId)
                .OrderByDescending(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateTransactionAsync(LedgerEntry entry) { var ex = await _db.Transactions.FindAsync(entry.Id); if (ex != null) { if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; _db.Entry(ex).CurrentValues.SetValues(entry); await _db.SaveChangesAsync(); } }
        public async Task DeleteTransactionAsync(int id) { var target = await _db.Transactions.FindAsync(id); if (target != null) { _db.Transactions.Remove(target); await _db.SaveChangesAsync(); } }

        public async Task<List<LedgerEntry>> GetMonthlyTransactionsAsync(int deptId, int year, int month) => await _db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.Date.Year == year && t.Date.Month == month).OrderBy(t => t.Date).ToListAsync();
        public async Task<List<LedgerEntry>> GetAllTransactionsAsync(int year) => await _db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();

        // =========================================================
        // CSV/XLS 은행 거래내역 파싱
        // =========================================================
        public async Task<List<LedgerEntry>> ParseAndClassifyBankCsvAsync(Stream fileStream, int departmentId)
        {
            var list = new List<LedgerEntry>();
            var dbMappings = await _db.CategoryMappings.AsNoTracking()
                .Where(m => m.DepartmentId == departmentId).ToListAsync();

            Encoding encoding;
            try { encoding = Encoding.GetEncoding("euc-kr"); }
            catch { encoding = Encoding.UTF8; }

            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                rawBytes = ms.ToArray();
            }

            if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                encoding = Encoding.UTF8;
            else if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
                encoding = Encoding.Unicode;

            var content = encoding.GetString(rawBytes);
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int dataStartLine = 0;
            int colDate = -1, colDesc = -1, colMemo = -1, colPerson = -1;
            int colIncome = -1, colExpense = -1, colBalance = -1;

            for (int i = 0; i < Math.Min(lines.Length, 15); i++)
            {
                var cols = SplitCsvLine(lines[i]);
                for (int c = 0; c < cols.Count; c++)
                {
                    string h = cols[c].Trim().Trim('"');
                    if (h == "거래일시" || h == "날짜") colDate = c;
                    else if (h == "적요") colDesc = c;
                    else if (h == "추가메모") colMemo = c;
                    else if (h == "의뢰인/수취인" || h == "의뢰인" || h == "수취인") colPerson = c;
                    else if (h == "입금" || h == "입금액" || h == "입금(원)") colIncome = c;
                    else if (h == "출금" || h == "출금액" || h == "출금(원)") colExpense = c;
                    else if (h == "거래후잔액" || h == "잔액") colBalance = c;
                }
                if (colDate >= 0 && colDesc >= 0) { dataStartLine = i + 1; break; }
            }

            if (colDate < 0)
            {
                colDate = 0; colDesc = 1; colMemo = 2; colPerson = 3;
                colIncome = 4; colExpense = 5; colBalance = 6;
                dataStartLine = 2;
            }

            var existingKeys = new HashSet<string>(
                (await _db.Transactions.AsNoTracking()
                    .Where(t => t.DepartmentId == departmentId)
                    .Select(t => t.Date.ToString("yyyyMMddHHmm") + "_" + t.Description + "_" + t.Income + "_" + t.Expense)
                    .ToListAsync()));

            for (int i = dataStartLine; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var v = SplitCsvLine(line);
                if (v.Count <= Math.Max(colDate, Math.Max(colDesc, Math.Max(colIncome, colExpense)))) continue;

                string Clean(int idx) => idx >= 0 && idx < v.Count ? v[idx].Trim().Trim('"').Trim() : "";

                string dateStr = Clean(colDate);
                if (!DateTime.TryParse(dateStr, out DateTime date)) continue;

                string desc = Clean(colDesc);
                string memo = colMemo >= 0 ? Clean(colMemo) : "";
                string person = colPerson >= 0 ? Clean(colPerson) : "";

                decimal income = ParseMoney(Clean(colIncome));
                decimal expense = ParseMoney(Clean(colExpense));

                if (income == 0 && expense == 0) continue;

                string note = string.Join(" ", new[] { person, memo }.Where(s => !string.IsNullOrWhiteSpace(s)));
                string type = income > 0 ? "수입" : "지출";
                string searchText = $"{desc} {person} {memo}";
                string category = ClassifyTransaction(searchText, type, dbMappings);

                string key = $"{date:yyyyMMddHHmm}_{desc}_{income}_{expense}";
                if (existingKeys.Contains(key)) continue;
                existingKeys.Add(key);

                int fiscalYear = date.Year;
                if ((date.Month == 11 || date.Month == 12))
                {
                    var q4End = await GetQuarterDateRangeAsync(departmentId, date.Year, 4);
                    if (date > q4End.End) fiscalYear = date.Year + 1;
                }
                int quarter = await GetQuarterNumberAsync(departmentId, fiscalYear, date);

                list.Add(new LedgerEntry
                {
                    Date = date,
                    Description = desc,
                    Income = income,
                    Expense = expense,
                    DepartmentId = departmentId,
                    FiscalYear = fiscalYear,
                    Quarter = quarter,
                    Type = type,
                    Category = category,
                    Note = note
                });
            }
            return list;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuote = !inQuote; }
                else if (c == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
            result.Add(current.ToString());
            return result;
        }

        private async Task<int> GetQuarterNumberAsync(int deptId, int fiscalYear, DateTime date)
        {
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q2 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 2);
            var q3 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 3);
            var q4prev = await GetQuarterDateRangeAsync(deptId, fiscalYear - 1, 4);
            if (date >= q4prev.Start && date <= q1.End) return 1;
            if (date > q1.End && date <= q2.End) return 2;
            if (date > q2.End && date <= q3.End) return 3;
            return 4;
        }

        private static string ClassifyTransaction(string text, string type, List<CategoryMapping> mappings)
        {
            foreach (var m in mappings)
                if (!string.IsNullOrEmpty(m.Keyword) && text.Contains(m.Keyword))
                    return m.Category;

            if (type == "수입")
            {
                if (text.Contains("주정헌금") || text.Contains("주일헌금") || text.Contains("주정힌금") || text.Contains("주정헌긍") || text.Contains("작정헌금")) return "주일헌금";
                if (text.Contains("인천중앙교회") && !text.Contains("초등부") && !text.Contains("영유아") && !text.Contains("유치")) return "교회보조금";
                if (text.Contains("후원") || text.Contains("찬조") || text.Contains("권사회") || text.Contains("집사회")) return "찬조금";
                if (text.Contains("이자") || text.Contains("이자소득") || text.Contains("예금이자")) return "은행이자";
                if (text.Contains("환급") || text.Contains("반납") || text.Contains("취소")) return "환급금";
                return "회비수입";
            }

            if (text.Contains("성경학교") || text.Contains("볼베어") || text.Contains("웅진플레이") || text.Contains("캠프") || text.Contains("트래블로버") || text.Contains("여행자보험") || text.Contains("손해보험"))
            {
                if (text.Contains("겨울") || text.Contains("12") || text.Contains("1월") || text.Contains("2월")) return "겨울성경학교";
                return "여름성경학교";
            }
            if (text.Contains("두란노") || text.Contains("세계로") || text.Contains("씨유") || text.Contains("CU") || text.Contains("이마트24") || text.Contains("킹식자재") || text.Contains("탐나는피자") || text.Contains("빵") || text.Contains("컵케익") || text.Contains("약봉투") || text.Contains("점토") || text.Contains("우리동네할인")) return "공과비";
            if (text.Contains("파리바게트") || text.Contains("명랑시대") || text.Contains("돌담옥") || text.Contains("뚝배기") || text.Contains("안스") || text.Contains("수푸드") || text.Contains("솔리드퍼퓸") || text.Contains("교사간담")) return "교사회의비";
            if (text.Contains("QT") || text.Contains("공과책") || text.Contains("훈련") || text.Contains("MT") || text.Contains("춘천") || text.Contains("삼악산") || text.Contains("이디야") || text.Contains("목향원") || text.Contains("KH에너지")) return "훈련비";
            if (text.Contains("현수막") || text.Contains("명찰") || text.Contains("마이크") || text.Contains("테이블") || text.Contains("바구니") || text.Contains("테이블보")) return "부서관리비";
            if (text.Contains("다이소") || text.Contains("아트박스") || text.Contains("크로바") || text.Contains("와글") || text.Contains("캣플") || text.Contains("롤링파스타") || text.Contains("101번지") || text.Contains("중화가정") || text.Contains("암송") || text.Contains("꽃") || text.Contains("펜던트") || text.Contains("십자가")) return "행사비";
            if (text.Contains("쿠팡") || text.Contains("네이버파이낸셜") || text.Contains("카카오페이") || text.Contains("비바리퍼블리카")) return "행사비";

            return "미분류";
        }

        // =========================================================
        // 5. 예산 및 분류 설정
        // =========================================================
        public async Task<List<BudgetPlan>> GetBudgetPlansAsync(int deptId, int year, string type) => await _db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Year == year && b.Type == type).ToListAsync();
        public async Task SaveBudgetPlanAsync(BudgetPlan plan)
        {
            if (plan == null) return;
            if (!string.IsNullOrEmpty(plan.Type))
            {
                if (plan.Type.Equals("수입", StringComparison.OrdinalIgnoreCase)) plan.Type = "Income";
                else if (plan.Type.Equals("지출", StringComparison.OrdinalIgnoreCase)) plan.Type = "Expense";
            }

            if (plan.Id == 0) _db.BudgetPlans.Add(plan);
            else { var ex = await _db.BudgetPlans.FindAsync(plan.Id); if (ex != null) _db.Entry(ex).CurrentValues.SetValues(plan); }
            await _db.SaveChangesAsync();
        }
        public async Task DeleteBudgetPlanAsync(int id) { var t = await _db.BudgetPlans.FindAsync(id); if (t != null) { _db.BudgetPlans.Remove(t); await _db.SaveChangesAsync(); } }

        public async Task<List<CategoryMapping>> GetMappingsAsync(int deptId) => await _db.CategoryMappings.AsNoTracking().Where(m => m.DepartmentId == deptId).ToListAsync();
        public async Task SaveMappingAsync(CategoryMapping m) { if (m.Id == 0) _db.CategoryMappings.Add(m); else { var ex = await _db.CategoryMappings.FindAsync(m.Id); if (ex != null) _db.Entry(ex).CurrentValues.SetValues(m); } await _db.SaveChangesAsync(); }
        public async Task DeleteMappingAsync(int id) { var m = await _db.CategoryMappings.FindAsync(id); if (m != null) { _db.CategoryMappings.Remove(m); await _db.SaveChangesAsync(); } }

        // =========================================================
        // 6. 사용자 관리 및 부서 정보 관리
        // =========================================================
        public async Task<List<User>> GetAllUsersAsync() => await _db.Users.AsNoTracking().ToListAsync();
        public async Task AddUserAsync(User user) { if (!await _db.Users.AnyAsync(u => u.Username == user.Username)) { _db.Users.Add(user); await _db.SaveChangesAsync(); } }
        public async Task DeleteUserAsync(string id) { var u = await _db.Users.FindAsync(id); if (u != null) { _db.Users.Remove(u); await _db.SaveChangesAsync(); } }
        public async Task ChangePasswordAsync(string id, string pw) { var u = await _db.Users.FindAsync(id); if (u != null) { u.Password = pw; await _db.SaveChangesAsync(); } }
        public async Task ResetPasswordAsync(string id) { var u = await _db.Users.FindAsync(id); if (u != null) { u.Password = "1234"; await _db.SaveChangesAsync(); } }

        // 부서 목록 전체 불러오기
        public async Task<List<Department>> GetDepartmentsAsync() => await _db.Departments.AsNoTracking().ToListAsync();

        // 🚀 신규 추가: 단일 부서 정보 가져오기 (환경설정용)
        public async Task<Department?> GetDepartmentAsync(int departmentId)
        {
            return await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId);
        }

        // 🚀 신규 추가: 단일 부서 정보 업데이트 (환경설정용)
        public async Task UpdateDepartmentAsync(Department updatedDept)
        {
            var existing = await _db.Departments.FindAsync(updatedDept.Id);
            if (existing != null)
            {
                _db.Entry(existing).CurrentValues.SetValues(updatedDept);
                await _db.SaveChangesAsync();
            }
        }

        // 사용자 정보 통째로 업데이트 (이름, 부서, 활성상태 변경용)
        public async Task UpdateUserAsync(User user)
        {
            var existing = await _db.Users.FindAsync(user.Username);
            if (existing != null)
            {
                _db.Entry(existing).CurrentValues.SetValues(user);
                await _db.SaveChangesAsync();
            }
        }

        // 초기 매핑 셋팅 (유년부 Id 찾아서 저장)
        public async Task EnsureDetailedMappingsAsync(int deptId)
        {
            var mapData = new Dictionary<string, string>
            {
                { "인천중앙교회", "교회보조금" }, { "주정헌금", "주일헌금" }, { "주일헌금", "주일헌금" }, { "이자", "은행이자" },
                { "후원", "찬조금" }, { "환급", "환급금" }, { "반납", "환급금" },
                { "두란노", "공과비" }, { "세계로", "공과비" }, { "씨유", "공과비" }, { "이마트24", "공과비" }, { "우리동네할인", "공과비" }, { "킹식자재", "공과비" }, { "탐나는피자", "공과비" },
                { "파리바게트", "교사회의비" }, { "명랑시대", "교사회의비" }, { "돌담옥", "교사회의비" }, { "뚝배기이탈리아", "교사회의비" }, { "수푸드", "교사회의비" },
                { "다이소", "행사비" }, { "아트박스", "행사비" }, { "크로바", "행사비" }, { "와글", "행사비" }, { "캣플", "행사비" }, { "롤링파스타", "행사비" }, { "알짜마트", "행사비" },
                { "현수막", "부서관리비" }, { "명찰", "부서관리비" }, { "테이블", "부서관리비" },
                { "QT", "훈련비" }, { "목향원", "훈련비" }, { "삼악산", "훈련비" }, { "이디야", "훈련비" }, { "KH에너지", "훈련비" },
                { "볼베어", "여름성경학교" }, { "웅진플레이", "여름성경학교" }, { "트래블로버", "여름성경학교" }, { "한솥도시락", "여름성경학교" }, { "캠프", "여름성경학교" },
            };
            foreach (var kv in mapData)
            {
                if (!await _db.CategoryMappings.AnyAsync(x => x.Keyword == kv.Key && x.DepartmentId == deptId))
                    _db.CategoryMappings.Add(new CategoryMapping { Keyword = kv.Key, Category = kv.Value, DepartmentId = deptId });
            }
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // [7] 지출결의서
        // =========================================================
        public async Task SaveExpenseReportAsync(ExpenseReport report)
        {
            if (report.Id == 0) _db.ExpenseReports.Add(report);
            else { var existing = await _db.ExpenseReports.FindAsync(report.Id); if (existing != null) _db.Entry(existing).CurrentValues.SetValues(report); }
            await _db.SaveChangesAsync();
        }

        public async Task<List<ExpenseReport>> GetExpenseReportsAsync(int deptId, int year) =>
            await _db.ExpenseReports.AsNoTracking().Where(r => r.DepartmentId == deptId && r.FiscalYear == year).OrderByDescending(r => r.Date).ToListAsync();

        public async Task DeleteExpenseReportAsync(int id) { var target = await _db.ExpenseReports.FindAsync(id); if (target != null) { _db.ExpenseReports.Remove(target); await _db.SaveChangesAsync(); } }
        // =========================================================
        // [신규 추가] 지출결의서 작성용 예산 및 기 신청액 통계 조회 (SQLite 호환성 패치)
        // =========================================================
        public async Task<(decimal TotalReceived, decimal TotalUsed)> GetSubsidyStatusForReportAsync(int deptId, int year)
        {
            // 1. 총 예산(수령액): 해당 연도 수입 예산 총합 (SQLite decimal Sum 오류 방지를 위해 메모리에서 합산)
            var budgetList = await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Year == year && (b.Type == "Income" || b.Type == "수입"))
                .Select(b => b.Amount)
                .ToListAsync();

            decimal totalBudget = budgetList.Sum();

            // 2. 기 신청액: 해당 연도에 이미 작성된 지출결의서들의 총합
            var usedList = await _db.ExpenseReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == year)
                .Select(r => r.TotalAmount)
                .ToListAsync();

            decimal totalUsed = usedList.Sum();

            return (totalBudget, totalUsed);
        }
        private static decimal ParseMoney(string s) => decimal.TryParse((s ?? "").Replace(",", "").Replace("\"", "").Trim(), out decimal r) ? r : 0;

        public async Task<List<LedgerEntry>> GetLedgerAsync(int deptId, int year)
        {
            return await _db.Transactions.Where(t => t.DepartmentId == deptId && t.FiscalYear == year)
                .OrderBy(t => t.Date).AsNoTracking().ToListAsync();
        }

        public async Task DeleteLedgerEntryAsync(int id) { var entry = await _db.Transactions.FindAsync(id); if (entry != null) { _db.Transactions.Remove(entry); await _db.SaveChangesAsync(); } }
    }
}