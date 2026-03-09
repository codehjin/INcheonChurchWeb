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

    public class AccountingService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AccountingService(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // =========================================================
        // [1-1] 보조금 현황 계산 로직 (SQLite 호환성 - 메모리 합계 방식)
        // =========================================================
        public async Task<(DateTime Start, DateTime End)> GetFiscalYearRangeAsync(string dept, int year)
        {
            var q1Range = await GetQuarterDateRangeAsync(dept, year, 1);
            var q4Range = await GetQuarterDateRangeAsync(dept, year, 4);
            return (q1Range.Start, q4Range.End);
        }

        public async Task<(decimal TotalReceived, decimal TotalUsed)> GetSubsidyStatusAsync(string dept, int year)
        {
            // SQLite decimal Sum 오류 해결을 위해 ToList 후 메모리 계산
            var budgetList = await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.Department == dept && b.Year == year && b.Type == "Income" && b.Category == "교회보조금")
                .Select(b => b.Amount).ToListAsync();

            var expenseList = await _db.Transactions.AsNoTracking()
                .Where(t => t.Department == dept && t.FiscalYear == year && t.Type == "지출")
                .Select(t => t.Expense).ToListAsync();

            return (budgetList.Sum(), expenseList.Sum());
        }

        public async Task<(decimal TotalReceived, decimal TotalUsed)> GetSubsidyStatusForReportAsync(string dept, int year)
        {
            // 예산 합계 (메모리 계산)
            var budgetList = await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.Department == dept && b.Year == year && b.Type == "Income" && b.Category == "교회보조금")
                .Select(b => b.Amount).ToListAsync();

            // 결의서 신청 합계 (메모리 계산)
            var usedList = await _db.ExpenseReports.AsNoTracking()
                .Where(r => r.Department == dept && r.FiscalYear == year)
                .Select(r => r.TotalAmount).ToListAsync();

            return (budgetList.Sum(), usedList.Sum());
        }

        // =========================================================
        // [1-2] 분기 설정: 날짜 범위 저장 및 조회
        // =========================================================

        public async Task SaveQuarterSettingAsync(string dept, int year, int quarter, DateTime start, DateTime end)
        {
            string key = $"Quarter_{year}_Q{quarter}";
            string rangeStr = $"{start:yyyy-MM-dd}~{end:yyyy-MM-dd}";

            var setting = await _db.CategoryMappings.FirstOrDefaultAsync(m => m.Department == dept && m.Keyword == key);
            if (setting == null)
            {
                _db.CategoryMappings.Add(new CategoryMapping { Department = dept, Keyword = key, Category = rangeStr });
            }
            else
            {
                setting.Category = rangeStr;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<(DateTime Start, DateTime End)> GetQuarterDateRangeAsync(string dept, int year, int quarter)
        {
            string key = $"Quarter_{year}_Q{quarter}";
            var setting = await _db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Department == dept && m.Keyword == key);

            if (setting != null && setting.Category.Contains("~"))
            {
                var parts = setting.Category.Split('~');
                if (DateTime.TryParse(parts[0], out DateTime s) && DateTime.TryParse(parts[1], out DateTime e))
                {
                    return (s, e);
                }
            }

            return quarter switch
            {
                1 => (new DateTime(year - 1, 12, 1), new DateTime(year, 2, DateTime.DaysInMonth(year, 2))),
                2 => (new DateTime(year, 3, 1), new DateTime(year, 5, 31)),
                3 => (new DateTime(year, 6, 1), new DateTime(year, 8, 31)),
                4 => (new DateTime(year, 9, 1), new DateTime(year, 11, 30)),
                _ => (new DateTime(year, 1, 1), new DateTime(year, 12, 31))
            };
        }

        // =========================================================
        // [1-3] 월별장부 - 기간별 조회
        // =========================================================
        public async Task<List<LedgerEntry>> GetLedgerByOptionAsync(string dept, int year, string option, int value)
        {
            var query = _db.Transactions.AsNoTracking().Where(t => t.Department == dept);

            if (option == "Month") query = query.Where(t => t.FiscalYear == year && t.Date.Month == value);
            else if (option == "Quarter")
            {
                var range = await GetQuarterDateRangeAsync(dept, year, value);
                query = query.Where(t => t.Date >= range.Start && t.Date <= range.End);
            }
            else query = query.Where(t => t.FiscalYear == year);

            return await query.OrderBy(t => t.Date).ToListAsync();
        }

        // [신규] 카테고리 목록 조회
        public async Task<List<string>> GetAllCategoriesAsync(string dept)
        {
            var budgetCats = await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == dept).Select(b => b.Category).ToListAsync();
            var ledgerCats = await _db.Transactions.AsNoTracking().Where(t => t.Department == dept).Select(t => t.Category).ToListAsync();
            return budgetCats.Union(ledgerCats).Where(c => !string.IsNullOrEmpty(c) && c != "미분류").Distinct().OrderBy(c => c).ToList();
        }

        // [신규] 일괄 수정
        public async Task BulkUpdateCategoryAsync(List<int> ids, string newCategory)
        {
            var targets = await _db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync();
            foreach (var item in targets) { item.Category = newCategory; }
            await _db.SaveChangesAsync();
        }

        // [1-4] 예산 히스토리 조회
        public async Task<List<BudgetPlan>> GetAllBudgetPlansForDeptAsync(string dept, string type)
        {
            return await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.Department == dept && b.Type == type)
                .OrderByDescending(b => b.Year).ThenBy(b => b.Category).ToListAsync();
        }

        // =========================================================
        // 1. 대시보드 통계 (SQLite 호환성 보완)
        // =========================================================
        public async Task<List<DeptStat>> GetIntegratedDashboardAsync(int year)
        {
            var trans = await _db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();
            var budgets = await _db.BudgetPlans.AsNoTracking().Where(b => b.Year == year && b.Type == "Expense").ToListAsync();
            var depts = new[] { "영유아부", "유치부", "유년부", "초등부", "중고등부", "교회학교운영팀" };
            var list = new List<DeptStat>();

            foreach (var d in depts)
            {
                string shortName = d switch { "영유아부" => "영유", "유치부" => "유치", "유년부" => "유년", "초등부" => "초등", "중고등부" => "중고", _ => "운영" };
                decimal budget = budgets.Where(b => b.Department == d).Sum(b => b.Amount);
                decimal spent = trans.Where(t => t.Department == d && t.Type == "지출").Sum(t => t.Expense);

                list.Add(new DeptStat { DeptName = d, ShortName = shortName, Budget = budget, Spent = spent, Balance = budget - spent, Rate = budget == 0 ? 0 : (int)(spent / budget * 100) });
            }
            return list;
        }

        public async Task<(decimal TotalIn, decimal TotalOut, List<StatItem> Stats)> GetDashboardDataAsync(string dept, int year)
        {
            var trans = await _db.Transactions.AsNoTracking().Where(t => t.Department == dept && t.FiscalYear == year).ToListAsync();
            var budgets = await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == dept && b.Year == year && b.Type == "Expense").ToListAsync();
            var stats = budgets.GroupBy(b => b.Category).Select(g => new StatItem { Category = g.Key, Budget = g.Sum(x => x.Amount), Spent = trans.Where(t => t.Type == "지출" && t.Category == g.Key).Sum(t => t.Expense) }).ToList();
            var unclassified = trans.Where(t => t.Type == "지출" && t.Category == "미분류").Sum(t => t.Expense);
            if (unclassified > 0) stats.Add(new StatItem { Category = "미분류", Budget = 0, Spent = unclassified });
            return (trans.Sum(t => t.Income), trans.Sum(t => t.Expense), stats);
        }

        // =========================================================
        // 2. 영수증 이미지 최적화 업로드
        // =========================================================
        public async Task<string> UploadReceiptAsync(IBrowserFile file, int transactionId)
        {
            var entry = await _db.Transactions.FindAsync(transactionId);
            if (entry == null) return "내역을 찾을 수 없습니다.";
            try
            {
                string extension = Path.GetExtension(file.Name).ToLower();
                string safeDesc = Regex.Replace(entry.Description, @"[\\/:*?""<>|]", "_");
                string newFileName = $"{entry.Date:yyyy-MM-dd}_{entry.Category}_{safeDesc}.jpg";
                string uploadFolder = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);
                string filePath = Path.Combine(uploadFolder, newFileName);

                using (var inputStream = file.OpenReadStream(1024 * 1024 * 20))
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
            var item = await _db.Transactions.FindAsync(id);
            if (item != null) { item.ReceiptPath = null; await _db.SaveChangesAsync(); }
        }

        // =========================================================
        // 3. 로그 및 백업 (기존 로직 보존)
        // =========================================================
        public async Task LogActivityAsync(string username, string action, string details)
        {
            _db.ActivityLogs.Add(new ActivityLog { Username = username, Action = action, Details = details, Timestamp = DateTime.Now });
            await _db.SaveChangesAsync();
        }

        public async Task CreateBackupAsync(string dept, string type, string memo)
        {
            string jsonData = "";
            if (type == "Ledger") jsonData = JsonSerializer.Serialize(await _db.Transactions.AsNoTracking().Where(t => t.Department == dept).ToListAsync());
            else if (type == "Budget") jsonData = JsonSerializer.Serialize(await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == dept).ToListAsync());
            else if (type == "Mapping") jsonData = JsonSerializer.Serialize(await _db.CategoryMappings.AsNoTracking().Where(t => t.Department == dept).ToListAsync());
            _db.DataBackups.Add(new DataBackup { Department = dept, DataType = type, Memo = memo, JsonData = jsonData, BackupDate = DateTime.Now });
            await _db.SaveChangesAsync();
        }

        public async Task<List<DataBackup>> GetBackupsAsync(string dept) => await _db.DataBackups.AsNoTracking().Where(b => b.Department == dept).OrderByDescending(b => b.BackupDate).ToListAsync();

        public async Task RestoreFromBackupAsync(int backupId)
        {
            var backup = await _db.DataBackups.FindAsync(backupId);
            if (backup == null) return;
            if (backup.DataType == "Budget") { var old = await _db.BudgetPlans.Where(b => b.Department == backup.Department).ToListAsync(); _db.BudgetPlans.RemoveRange(old); var restored = JsonSerializer.Deserialize<List<BudgetPlan>>(backup.JsonData); if (restored != null) _db.BudgetPlans.AddRange(restored); }
            else if (backup.DataType == "Ledger") { var old = await _db.Transactions.Where(t => t.Department == backup.Department).ToListAsync(); _db.Transactions.RemoveRange(old); var restored = JsonSerializer.Deserialize<List<LedgerEntry>>(backup.JsonData); if (restored != null) _db.Transactions.AddRange(restored); }
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // 4. 장부 관리 및 자동 분류
        // =========================================================
        public async Task<List<string>> GetCategorySuggestionsAsync(string dept, string type)
        {
            var fromBudget = await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == dept && b.Type == type).Select(b => b.Category).Distinct().ToListAsync();
            var fromLedger = await _db.Transactions.AsNoTracking().Where(t => t.Department == dept && t.Type == (type == "Expense" ? "지출" : "수입")).Select(t => t.Category).Distinct().ToListAsync();
            return fromBudget.Union(fromLedger).OrderBy(c => c).ToList();
        }

        public async Task AddTransactionAsync(LedgerEntry entry) { entry.Id = 0; if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; if (string.IsNullOrEmpty(entry.Category)) entry.Category = "미분류"; _db.Transactions.Add(entry); await _db.SaveChangesAsync(); }
        public async Task UpdateTransactionAsync(LedgerEntry entry) { var ex = await _db.Transactions.FindAsync(entry.Id); if (ex != null) { if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; _db.Entry(ex).CurrentValues.SetValues(entry); await _db.SaveChangesAsync(); } }
        public async Task DeleteTransactionAsync(int id) { var target = await _db.Transactions.FindAsync(id); if (target != null) { _db.Transactions.Remove(target); await _db.SaveChangesAsync(); } }

        public async Task<List<LedgerEntry>> GetMonthlyTransactionsAsync(string dept, int year, int month) => await _db.Transactions.AsNoTracking().Where(t => t.Department == dept && t.Date.Year == year && t.Date.Month == month).OrderBy(t => t.Date).ToListAsync();
        public async Task<List<LedgerEntry>> GetAllTransactionsAsync(int year) => await _db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();

        // CSV 자동 분류 및 파싱
        public async Task<List<LedgerEntry>> ParseAndClassifyBankCsvAsync(Stream fileStream, string department)
        {
            var list = new List<LedgerEntry>();
            var dbMappings = await _db.CategoryMappings.AsNoTracking().Where(m => m.Department == department).ToListAsync();
            using (var reader = new StreamReader(fileStream, Encoding.UTF8))
            {
                await reader.ReadLineAsync(); await reader.ReadLineAsync(); // 헤더 스킵
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var v = Regex.Split(line, ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
                    if (v.Length >= 7 && DateTime.TryParse(v[1].Replace("\"", ""), out DateTime date))
                    {
                        decimal inc = decimal.TryParse(v[5].Replace("\"", "").Replace(",", ""), out var i) ? i : 0;
                        decimal exp = decimal.TryParse(v[6].Replace("\"", "").Replace(",", ""), out var e) ? e : 0;
                        string desc = v[2].Replace("\"", "").Trim();
                        string type = inc > 0 ? "수입" : "지출";
                        string category = ClassifyTransaction(desc, type, dbMappings);
                        list.Add(new LedgerEntry { Date = date, Description = desc, Income = inc, Expense = exp, Department = department, FiscalYear = date.Year, Quarter = 1, Type = type, Category = category, Note = "" });
                    }
                }
            }
            return list;
        }

        private string ClassifyTransaction(string desc, string type, List<CategoryMapping> mappings)
        {
            foreach (var m in mappings) { if (desc.Contains(m.Keyword)) return m.Category; }
            if (type == "수입") { if (desc.Contains("후원")) return "찬조금"; if (desc.Contains("인천중앙교회")) return "교회보조금"; return "회비수입"; }
            else { if (desc.Contains("두란노")) return "공과비"; return "행사비"; }
        }

        // =========================================================
        // 5. 예산 및 분류 설정
        // =========================================================
        public async Task<List<BudgetPlan>> GetBudgetPlansAsync(string dept, int year, string type) => await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == dept && b.Year == year && b.Type == type).ToListAsync();
        public async Task SaveBudgetPlanAsync(BudgetPlan plan) { if (plan.Id == 0) _db.BudgetPlans.Add(plan); else { var ex = await _db.BudgetPlans.FindAsync(plan.Id); if (ex != null) _db.Entry(ex).CurrentValues.SetValues(plan); } await _db.SaveChangesAsync(); }
        public async Task DeleteBudgetPlanAsync(int id) { var t = await _db.BudgetPlans.FindAsync(id); if (t != null) { _db.BudgetPlans.Remove(t); await _db.SaveChangesAsync(); } }
        public async Task<List<CategoryMapping>> GetMappingsAsync(string dept) => await _db.CategoryMappings.AsNoTracking().Where(m => m.Department == dept).ToListAsync();
        public async Task SaveMappingAsync(CategoryMapping m) { if (m.Id == 0) _db.CategoryMappings.Add(m); else { var ex = await _db.CategoryMappings.FindAsync(m.Id); if (ex != null) _db.Entry(ex).CurrentValues.SetValues(m); } await _db.SaveChangesAsync(); }
        public async Task DeleteMappingAsync(int id) { var m = await _db.CategoryMappings.FindAsync(id); if (m != null) { _db.CategoryMappings.Remove(m); await _db.SaveChangesAsync(); } }

        // =========================================================
        // 6. 사용자 및 기초 데이터 (보존)
        // =========================================================
        public async Task<List<User>> GetAllUsersAsync() => await _db.Users.AsNoTracking().ToListAsync();
        public async Task AddUserAsync(User user) { if (!await _db.Users.AnyAsync(u => u.Username == user.Username)) { _db.Users.Add(user); await _db.SaveChangesAsync(); } }
        public async Task DeleteUserAsync(string id) { var u = await _db.Users.FindAsync(id); if (u != null) { _db.Users.Remove(u); await _db.SaveChangesAsync(); } }
        public async Task ChangePasswordAsync(string id, string pw) { var u = await _db.Users.FindAsync(id); if (u != null) { u.Password = pw; await _db.SaveChangesAsync(); } }
        public async Task ResetPasswordAsync(string id) { var u = await _db.Users.FindAsync(id); if (u != null) { u.Password = "1234"; await _db.SaveChangesAsync(); } }

        public async Task EnsureDefaultMappingsAsync()
        {
            // Program.cs에서 부서 인자 없이 호출할 경우 기본값 "유년부"로 실행
            await EnsureDetailedMappingsAsync("유년부");
        }

        private List<BudgetPlan> GetOriginal2026Data(string dept)
        {
            if (dept != "유년부") return new List<BudgetPlan>();
            return new List<BudgetPlan> {
        new BudgetPlan { Type="Income", Category="교회보조금", CalcDetail="기본 보조", Amount=7800000, Year=2026, Department="유년부"},
        new BudgetPlan { Type="Income", Category="주일헌금", CalcDetail="매주 헌금 예상", Amount=1500000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Income", Category="찬조금", CalcDetail="특별 찬조", Amount=3239000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Income", Category="회비수입", CalcDetail="수련회비 등", Amount=1500000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="겨울성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="겨울성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="겨울성경학교", SubCategory="예비비", CalcDetail="보조교사 선물 등", Amount=200000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="여름성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="여름성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="여름성경학교", SubCategory="외부 물놀이", CalcDetail="30,000원*38명", Amount=1140000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="여름성경학교", SubCategory="예비비", CalcDetail="기타 진행비", Amount=200000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="생일축하행사", CalcDetail="10,000원*38명", Amount=380000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="성탄절준비비", CalcDetail="20,000원*25명", Amount=500000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="성탄절선물", CalcDetail="10,000원*38명", Amount=380000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="졸업선물", CalcDetail="20,000원*8명", Amount=160000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="부활절 특별활동", CalcDetail="계란 등", Amount=300000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="야외예배", CalcDetail="10,000원*38명*2회", Amount=760000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="전도비", CalcDetail="새친구 선물 등", Amount=200000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="반데이트", CalcDetail="20,000원*38명*2회", Amount=1520000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="행사비", SubCategory="달란트행사", CalcDetail="20,000원*25명*2회", Amount=1000000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="훈련비", SubCategory="공과교재", CalcDetail="4,500원*35명*2회", Amount=315000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="훈련비", SubCategory="교사훈련(MT)", CalcDetail="30,000원*13명*2회", Amount=780000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="부서관리비", SubCategory="물품구입", CalcDetail="가방, 명찰, 앞치마", Amount=100000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="부서관리비", SubCategory="환경미화", CalcDetail="현수막 및 데코", Amount=100000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="교사회의비", SubCategory="교사회식", CalcDetail="10,000원*13명*4분기", Amount=520000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="교사회의비", SubCategory="교사간식", CalcDetail="3,000원*13명*12달", Amount=468000, Year=2026, Department="유년부" },
        new BudgetPlan { Type="Expense", Category="공과비", SubCategory="주간공과", CalcDetail="2,000원*25명*52주", Amount=2600000, Year=2026, Department="유년부" },
    };
        }
        public async Task EnsureDefaultUsersAsync()
        {
            var defaultUsers = new List<User> {
                new User { Username = "admin", Password = "1234", Department = "관리자", Role = "Admin" },
                new User { Username = "manager", Password = "1234", Department = "교회학교운영팀", Role = "User" },
                new User { Username = "child", Password = "1234", Department = "유년부", Role = "User" },
                new User { Username = "infant", Password = "1234", Department = "영유아부", Role = "User" },
                new User { Username = "kinder", Password = "1234", Department = "유치부", Role = "User" },
                new User { Username = "elementary", Password = "1234", Department = "초등부", Role = "User" },
                new User { Username = "middle", Password = "1234", Department = "중고등부", Role = "User" }
            };
            foreach (var user in defaultUsers) { if (!await _db.Users.AnyAsync(u => u.Username == user.Username)) _db.Users.Add(user); }
            await _db.SaveChangesAsync();
        }

        public async Task<bool> CopyBudgetFromBaseYearAsync(string department, int baseYear, int targetYear)
        {
            // [수정] 기초데이터 원본 준비 (DB → 없으면 하드코딩 데이터)
            var sourceData = await _db.BudgetPlans.AsNoTracking()
                .Where(b => b.Department == department && b.Year == baseYear)
                .ToListAsync();

            if (!sourceData.Any())
                sourceData = GetOriginal2026Data(department);

            // 불러올 원본 자체가 없으면 false
            if (!sourceData.Any()) return false;

            // [수정] 기존 데이터가 있으면 금액이 0인(빈) 항목만 삭제 후 재적재
            //        실제 입력된 데이터가 있는 경우는 건드리지 않음
            var existingData = await _db.BudgetPlans
                .Where(b => b.Department == department && b.Year == targetYear)
                .ToListAsync();

            bool hasRealData = existingData.Any(b => b.Amount > 0);
            if (hasRealData) return false; // 실제 입력된 예산이 있으면 중단

            // 빈 데이터(Amount == 0)만 있거나 아예 없는 경우 → 모두 삭제 후 재적재
            if (existingData.Any())
                _db.BudgetPlans.RemoveRange(existingData);

            foreach (var plan in sourceData)
            {
                _db.BudgetPlans.Add(new BudgetPlan
                {
                    Department = department,
                    Year = targetYear,
                    Type = plan.Type,
                    Category = plan.Category,
                    SubCategory = plan.SubCategory,
                    CalcDetail = plan.CalcDetail,
                    Amount = plan.Amount
                });
            }
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task EnsureDetailedMappingsAsync(string dept = "유년부")
        {
            var mapData = new Dictionary<string, string> { { "다이소", "행사비" }, { "마트", "공과비" }, { "주일헌금", "주일헌금" }, { "보조금", "교회보조금" } };
            foreach (var kv in mapData) { if (!await _db.CategoryMappings.AnyAsync(x => x.Keyword == kv.Key && x.Department == dept)) _db.CategoryMappings.Add(new CategoryMapping { Keyword = kv.Key, Category = kv.Value, Department = dept }); }
            await _db.SaveChangesAsync();
        }

        public async Task InitializeMappingFrom2025Async(string dept) => await EnsureDetailedMappingsAsync(dept);

        // =========================================================
        // [7] 지출결의서 독립 관리 (ExpenseReport 테이블 사용)
        // =========================================================
        public async Task SaveExpenseReportAsync(ExpenseReport report)
        {
            if (report.Id == 0) _db.ExpenseReports.Add(report);
            else
            {
                var existing = await _db.ExpenseReports.FindAsync(report.Id);
                if (existing != null) _db.Entry(existing).CurrentValues.SetValues(report);
            }
            await _db.SaveChangesAsync();
        }

        public async Task<List<ExpenseReport>> GetExpenseReportsAsync(string dept, int year) =>
            await _db.ExpenseReports.AsNoTracking().Where(r => r.Department == dept && r.FiscalYear == year).OrderByDescending(r => r.Date).ToListAsync();

        public async Task DeleteExpenseReportAsync(int id)
        {
            var target = await _db.ExpenseReports.FindAsync(id);
            if (target != null) { _db.ExpenseReports.Remove(target); await _db.SaveChangesAsync(); }
        }
    }
}