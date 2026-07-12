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
using ClosedXML.Excel; // 환경설정 엑셀 내보내기
using ExcelDataReader; // 환경설정 엑셀 읽기
using System.Data; // DataTable

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

    // [DTO] 자동분류 규칙 분석(추천 시스템)용 — 키워드별 분류 빈도
    public class CategoryFrequency
    {
        public string Category { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; } // 0~100
    }

    // [DTO] 자동분류 규칙 분석(추천 시스템)용 — 키워드 한 건의 분석 결과
    public class KeywordSuggestion
    {
        public string Keyword { get; set; } = "";          // 적요/내역 문구 (예: "파리바게트미추")
        public int TotalCount { get; set; }                 // 해당 키워드의 총 분류 건수
        public string RecommendedCategory { get; set; } = ""; // 비율이 가장 높은 추천 분류
        public double RecommendedPercentage { get; set; }   // 추천 분류의 비율(%)
        public List<CategoryFrequency> Distribution { get; set; } = new(); // 분류별 빈도(내림차순)
    }

    // [DTO] 부서 단위 장부 백업/복구용
    public class DepartmentBackupDto
    {
        public DateTime ExportDate { get; set; }
        public int DepartmentId { get; set; }
        public List<LedgerEntry> Transactions { get; set; } = new();
        public List<BudgetPlan> BudgetPlans { get; set; } = new();
        public List<CategoryMapping> CategoryMappings { get; set; } = new();
        public List<ExpenseReport> ExpenseReports { get; set; } = new();
    }

    // [DTO] 레거시 장부 → 신규 LedgerTransaction 이관 결과 리포트
    public class LedgerMigrationResult
    {
        public bool SchemaReady { get; set; }       // 신규 테이블 존재 여부(미존재 시 이관 불가)
        public int Migrated { get; set; }           // 새로 이관된 건수
        public int LinkedToBudget { get; set; }     // BudgetPlan FK 연결 성공 건수
        public int Quarantined { get; set; }        // 금액 이상치로 건너뛴 건수(양쪽 0 / 양쪽 입력 / 음수)
        public int AlreadyMigrated { get; set; }    // 멱등 가드로 스킵된 기존 이관 건수
        public string Message { get; set; } = "";   // 운영자 표시용 요약 메시지
    }

    // 🚀 1. 기존 public class 대신 public partial class 하나만 남깁니다.
    public partial class AccountingService
    {
        // 🚀 2. 정규식 생성기는 반드시 클래스의 '{' 안쪽에 위치해야 합니다.
        [GeneratedRegex(@"[\\/:*?""<>|]")]
        private static partial Regex InvalidFileNameChars();

        // 🚀 [동시성 안전화] Blazor Server에서 Scoped DbContext는 회로(circuit) 수명 동안
        // 살아남아 여러 비동기 작업이 같은 인스턴스를 공유 → 스레드 충돌이 발생합니다.
        // 따라서 전역 _db 주입 대신 IDbContextFactory로 각 메서드마다 짧은 수명의 컨텍스트를 생성합니다.
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IWebHostEnvironment _env;

        public AccountingService(IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment env)
        {
            _dbFactory = dbFactory;
            _env = env;
        }
        // =========================================================
        // [1-1] 보조금 현황 계산 로직
        // =========================================================
        // 특정 월의 4번째 주일(일요일)을 구하는 범용 헬퍼
        public static DateTime GetFourthSundayOfMonth(int year, int month)
        {
            DateTime first = new DateTime(year, month, 1);
            int daysToSunday = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
            DateTime firstSunday = first.AddDays(daysToSunday);
            return firstSunday.AddDays(21); // 첫째 주일 + 21일 = 넷째 주일
        }

        // 기존 호환용 래퍼
        public static DateTime GetFourthSundayOfNovember(int year) => GetFourthSundayOfMonth(year, 11);

        // 기본 분기 날짜 계산 (DB 설정이 없을 때 사용되는 규칙)
        // 규칙: 3개월마다 4주차 주일이 분기 마감일, 다음날 월요일이 새 분기 시작
        //   Q1: 전년 11월 4주차 주일 다음 월요일 ~ 2월 4주차 주일
        //   Q2: 2월 4주차 주일 다음 월요일 ~ 5월 4주차 주일
        //   Q3: 5월 4주차 주일 다음 월요일 ~ 8월 4주차 주일
        //   Q4: 8월 4주차 주일 다음 월요일 ~ 11월 4주차 주일
        public static (DateTime Start, DateTime End) GetDefaultQuarterRange(int year, int quarter)
        {
            DateTime prevNov4Sun = GetFourthSundayOfMonth(year - 1, 11);
            DateTime feb4Sun = GetFourthSundayOfMonth(year, 2);
            DateTime may4Sun = GetFourthSundayOfMonth(year, 5);
            DateTime aug4Sun = GetFourthSundayOfMonth(year, 8);
            DateTime nov4Sun = GetFourthSundayOfMonth(year, 11);

            return quarter switch
            {
                1 => (prevNov4Sun.AddDays(1), feb4Sun),
                2 => (feb4Sun.AddDays(1), may4Sun),
                3 => (may4Sun.AddDays(1), aug4Sun),
                4 => (aug4Sun.AddDays(1), nov4Sun),
                _ => (prevNov4Sun.AddDays(1), nov4Sun) // 전체 (회계연도 전체)
            };
        }

        // 분기 날짜 조회: DB 수동 설정 우선, 없으면 기본 규칙 적용
        public async Task<(DateTime Start, DateTime End)> GetQuarterDateRangeAsync(int deptId, int year, int quarter)
        {
            using var db = _dbFactory.CreateDbContext();

            string key = $"Quarter_{year}_Q{quarter}";
            var setting = await db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.DepartmentId == deptId && m.Keyword == key);

            // 1. DB에 설정된 분기값이 있으면 우선 적용
            if (setting != null && setting.Category.Contains('~'))
            {
                var parts = setting.Category.Split('~');
                if (DateTime.TryParse(parts[0], out DateTime s) && DateTime.TryParse(parts[1], out DateTime e))
                {
                    return (s, e);
                }
            }

            // 2. 설정이 없을 경우 기본 규칙 적용
            return GetDefaultQuarterRange(year, quarter);
        }
        // =========================================================
        // [1-2] 분기 설정: 날짜 범위 저장 및 조회
        // =========================================================

        // isSystemAdmin == true: 특정 부서가 아닌 '모든 부서(관리자/시스템 제외)'에 동일 분기 일정을 일괄 적용.
        public async Task SaveQuarterSettingAsync(int deptId, int year, int quarter, DateTime start, DateTime end, bool isSystemAdmin = false)
        {
            using var db = _dbFactory.CreateDbContext();

            string key = $"Quarter_{year}_Q{quarter}";
            string rangeStr = $"{start:yyyy-MM-dd}~{end:yyyy-MM-dd}";

            // 적용 대상 부서: 관리자면 전체, 아니면 본인 부서만
            List<int> targetDeptIds = isSystemAdmin
                ? await db.Departments.Where(d => d.Name != "관리자" && d.Name != "시스템").Select(d => d.Id).ToListAsync()
                : new List<int> { deptId };

            foreach (var tid in targetDeptIds)
            {
                var setting = await db.CategoryMappings.FirstOrDefaultAsync(m => m.DepartmentId == tid && m.Keyword == key);
                if (setting == null)
                    db.CategoryMappings.Add(new CategoryMapping { DepartmentId = tid, Keyword = key, Category = rangeStr });
                else
                    setting.Category = rangeStr;
            }
            await db.SaveChangesAsync();
        }

        // =========================================================
        // [1-3] 월별장부 - 기간별 조회
        // =========================================================
        public async Task<List<LedgerEntry>> GetLedgerByOptionAsync(int deptId, int year, string option, int value)
        {
            using var db = _dbFactory.CreateDbContext();

            var query = db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId);

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
            using var db = _dbFactory.CreateDbContext();

            var budgetCats = await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).Select(b => b.Category).ToListAsync();
            var ledgerCats = await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).Select(t => t.Category).ToListAsync();
            return budgetCats.Union(ledgerCats).Where(c => !string.IsNullOrEmpty(c) && c != "미분류").Distinct().OrderBy(c => c).ToList();
        }

        public async Task BulkUpdateCategoryAsync(List<int> ids, string newCategory)
        {
            using var db = _dbFactory.CreateDbContext();

            var targets = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync();
            foreach (var item in targets) { item.Category = newCategory; }
            await db.SaveChangesAsync();
        }

        public async Task<List<BudgetPlan>> GetAllBudgetPlansForDeptAsync(int deptId, string type)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Type == type)
                .OrderByDescending(b => b.Year).ThenBy(b => b.Category).ToListAsync();
        }

        // =========================================================
        // 1. 대시보드 통계 (SQLite 호환성 보완)
        // =========================================================
        public async Task<List<DeptStat>> GetIntegratedDashboardAsync(int year)
        {
            using var db = _dbFactory.CreateDbContext();

            var trans = await db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();
            var budgets = await db.BudgetPlans.AsNoTracking().Where(b => b.Year == year && b.Type == "Expense").ToListAsync();

            // 모든 부서를 DB에서 가져와서 처리합니다.
            var depts = await db.Departments.AsNoTracking().ToListAsync();
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
            using var db = _dbFactory.CreateDbContext();

            var trans = await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.FiscalYear == year).ToListAsync();
            var budgets = await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Year == year && b.Type == "Expense").ToListAsync();
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
            using var db = _dbFactory.CreateDbContext();

            // 🚀 부서 정보를 가져오기 위해 Include 추가
            var entry = await db.Transactions.Include(t => t.DepartmentInfo)
                                              .FirstOrDefaultAsync(t => t.Id == transactionId);

            if (entry == null) return "내역을 찾을 수 없습니다.";

            try
            {
                string extension = Path.GetExtension(file.Name).ToLower();
                if (extension != ".pdf") extension = ".jpg"; // 이미지는 jpg로 통일

                // 파일명 오류 방지 및 부서명 추출
                string safeDesc = InvalidFileNameChars().Replace(entry.Description ?? "내용없음", "_");
                string deptName = entry.DepartmentInfo?.Name ?? "부서미정";

                // 🚀 요청하신 파일명 규칙: 부서명_날짜_구분_적요.확장자
                string newFileName = $"{deptName}_{entry.Date:yyyy-MM-dd}_{entry.Category}_{safeDesc}{extension}";

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
                await db.SaveChangesAsync();
                return "OK";
            }
            catch (Exception ex) { return $"실패: {ex.Message}"; }
        }

        public async Task RemoveReceiptAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();

            var entry = await db.Transactions.FindAsync(id);
            if (entry != null)
            {
                if (!string.IsNullOrEmpty(entry.ReceiptPath))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, entry.ReceiptPath.TrimStart('/'));
                    if (File.Exists(fullPath)) { File.Delete(fullPath); }
                }
                entry.ReceiptPath = "";
                await db.SaveChangesAsync();
            }
        }

        // =========================================================
        // 3. 로그 및 백업 (DataType 수정)
        // =========================================================
        public async Task LogActivityAsync(string username, string action, string details)
        {
            using var db = _dbFactory.CreateDbContext();

            db.ActivityLogs.Add(new ActivityLog { Username = username, Action = action, Details = details, Timestamp = DateTime.Now });
            await db.SaveChangesAsync();
        }

        public async Task CreateBackupAsync(int deptId, string type, string memo)
        {
            using var db = _dbFactory.CreateDbContext();

            string jsonData;
            if (type == "Ledger")
            {
                jsonData = JsonSerializer.Serialize(await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            }
            else if (type == "Budget")
            {
                jsonData = JsonSerializer.Serialize(await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).ToListAsync());
            }
            else if (type == "Mapping")
            {
                jsonData = JsonSerializer.Serialize(await db.CategoryMappings.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            }
            else
            {
                var snapshot = new DepartmentBackupDto
                {
                    ExportDate = DateTime.Now,
                    DepartmentId = deptId,
                    Transactions = await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync(),
                    BudgetPlans = await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).ToListAsync(),
                    CategoryMappings = await db.CategoryMappings.AsNoTracking().Where(m => m.DepartmentId == deptId).ToListAsync(),
                    ExpenseReports = await db.ExpenseReports.AsNoTracking().Where(r => r.DepartmentId == deptId).ToListAsync()
                };

                jsonData = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            }

            db.DataBackups.Add(new DataBackup { DepartmentId = deptId, DataType = type, Memo = memo, JsonData = jsonData, BackupDate = DateTime.Now });
            await db.SaveChangesAsync();
        }

        public async Task<List<DataBackup>> GetBackupsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.DataBackups.AsNoTracking().Where(b => b.DepartmentId == deptId).OrderByDescending(b => b.BackupDate).ToListAsync();
        }

        public async Task RestoreFromBackupAsync(int backupId)
        {
            using var db = _dbFactory.CreateDbContext();

            var backup = await db.DataBackups.FindAsync(backupId);
            if (backup == null) return;

            if (backup.DataType == "Budget")
            {
                var old = await db.BudgetPlans.Where(b => b.DepartmentId == backup.DepartmentId).ToListAsync();
                db.BudgetPlans.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<BudgetPlan>>(backup.JsonData);
                if (restored != null) db.BudgetPlans.AddRange(restored);
            }
            else if (backup.DataType == "Ledger")
            {
                var old = await db.Transactions.Where(t => t.DepartmentId == backup.DepartmentId).ToListAsync();
                db.Transactions.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<LedgerEntry>>(backup.JsonData);
                if (restored != null) db.Transactions.AddRange(restored);
            }
            else if (backup.DataType == "Mapping")
            {
                var old = await db.CategoryMappings.Where(m => m.DepartmentId == backup.DepartmentId).ToListAsync();
                db.CategoryMappings.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<CategoryMapping>>(backup.JsonData);
                if (restored != null) db.CategoryMappings.AddRange(restored);
            }
            else
            {
                var snapshot = JsonSerializer.Deserialize<DepartmentBackupDto>(backup.JsonData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (snapshot == null) return;

                var oldTrans = await db.Transactions.Where(t => t.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldBudgets = await db.BudgetPlans.Where(b => b.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldMappings = await db.CategoryMappings.Where(m => m.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldReports = await db.ExpenseReports.Where(r => r.DepartmentId == backup.DepartmentId).ToListAsync();

                db.Transactions.RemoveRange(oldTrans);
                db.BudgetPlans.RemoveRange(oldBudgets);
                db.CategoryMappings.RemoveRange(oldMappings);
                db.ExpenseReports.RemoveRange(oldReports);

                if (snapshot.Transactions.Any()) db.Transactions.AddRange(snapshot.Transactions);
                if (snapshot.BudgetPlans.Any()) db.BudgetPlans.AddRange(snapshot.BudgetPlans);
                if (snapshot.CategoryMappings.Any()) db.CategoryMappings.AddRange(snapshot.CategoryMappings);
                if (snapshot.ExpenseReports.Any()) db.ExpenseReports.AddRange(snapshot.ExpenseReports);
            }

            await db.SaveChangesAsync();
        }

        // =========================================================
        // 4. 장부 관리 및 자동 분류
        // =========================================================
        public async Task<List<string>> GetCategorySuggestionsAsync(int deptId, string type)
        {
            using var db = _dbFactory.CreateDbContext();

            var fromBudget = await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Type == type).Select(b => b.Category).Distinct().ToListAsync();
            var fromLedger = await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.Type == (type == "Expense" ? "지출" : "수입")).Select(t => t.Category).Distinct().ToListAsync();
            return fromBudget.Union(fromLedger).OrderBy(c => c).ToList();
        }

        public async Task AddTransactionAsync(LedgerEntry entry)
        {
            using var db = _dbFactory.CreateDbContext();
            entry.Id = 0; if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; if (string.IsNullOrEmpty(entry.Category)) entry.Category = "미분류"; db.Transactions.Add(entry); await db.SaveChangesAsync();
        }

        public async Task AddTransactionsAsync(List<LedgerEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            using var db = _dbFactory.CreateDbContext();

            // 유년부 아이디 가져오기 (기본값 세팅용)
            var defaultDept = await db.Departments.FirstOrDefaultAsync(d => d.Name == "유년부");
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
                db.Transactions.Add(entry);
            }
            await db.SaveChangesAsync();
        }

        public async Task<DateTime?> GetLastTransactionDateAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId)
                .OrderByDescending(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateTransactionAsync(LedgerEntry entry)
        {
            using var db = _dbFactory.CreateDbContext();
            var ex = await db.Transactions.FindAsync(entry.Id); if (ex != null) { if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; db.Entry(ex).CurrentValues.SetValues(entry); await db.SaveChangesAsync(); }
        }
        public async Task DeleteTransactionAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.Transactions.FindAsync(id); if (target != null) { db.Transactions.Remove(target); await db.SaveChangesAsync(); }
        }

        public async Task<List<LedgerEntry>> GetMonthlyTransactionsAsync(int deptId, int year, int month)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId && t.Date.Year == year && t.Date.Month == month).OrderBy(t => t.Date).ToListAsync();
        }
        public async Task<List<LedgerEntry>> GetAllTransactionsAsync(int year)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();
        }

        // =========================================================
        // 🚀 [1회성 마이그레이션] 레거시 LedgerEntry → 신규 LedgerTransaction 이관
        //   - 기존 db.Transactions(LedgerEntry) 데이터를 읽어 신규 장부 테이블로 복사/매핑한다.
        //   - ⚠️ 신규 테이블이 없으면(기존 DB는 EnsureCreated 로 생성되지 않음) 예외 대신
        //         SchemaReady=false 로 안전하게 종료한다. (스키마 적용 안내는 운영자에게 위임)
        //   - 멱등성: 소프트 삭제분까지 포함해 한 건이라도 이관됐으면 중복 생성하지 않는다.
        //   - 금액 이상치(양쪽 0 / 양쪽 입력=대체거래 / 음수)는 격리(Quarantine)하여 건너뛴다.
        //   - 구분/금액: '금액이 들어있는 컬럼'을 거래 유형의 1차 근거로 신뢰한다(Type 문자열은 보조).
        //   - (부서·연도·항목명·구분)이 일치하는 BudgetPlan 이 있으면 FK 연결(Trim+대소문자 무시), 없으면 null.
        // =========================================================
        public async Task<LedgerMigrationResult> MigrateLedgerDataAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            // 0) 스키마 가드: 신규 테이블 미존재 시(기존 church.db) 즉시 안전 종료
            if (!await TableExistsAsync(db, "LedgerTransactions"))
            {
                return new LedgerMigrationResult
                {
                    SchemaReady = false,
                    Message = "LedgerTransactions 테이블이 없습니다. 신규 생명주기 테이블 스키마를 먼저 적용한 뒤 다시 실행하세요."
                };
            }

            // 1) 멱등성 가드: 소프트 삭제분까지 포함해 이미 이관된 행이 있으면 중복 방지
            int already = await db.LedgerTransactions.IgnoreQueryFilters().CountAsync();
            if (already > 0)
            {
                return new LedgerMigrationResult
                {
                    SchemaReady = true,
                    AlreadyMigrated = already,
                    Message = $"이미 {already}건이 이관되어 있어 건너뜁니다(중복 방지)."
                };
            }

            var legacy = await db.Transactions.AsNoTracking().ToListAsync();
            if (legacy.Count == 0)
                return new LedgerMigrationResult { SchemaReady = true, Message = "이관할 레거시 장부 데이터가 없습니다." };

            // BudgetPlan 매칭용 캐시 (반복 DB 조회 방지)
            var budgetPlans = await db.BudgetPlans.AsNoTracking().ToListAsync();

            int linked = 0, quarantined = 0;
            var newRows = new List<LedgerTransaction>(legacy.Count);

            foreach (var e in legacy)
            {
                // ── 1) 이상치 격리: 양쪽 0(빈 거래) / 양쪽 입력(대체·이중계상) / 음수 금액은 건너뜀 ──
                bool bothZero = e.Income == 0 && e.Expense == 0;
                bool bothSet = e.Income != 0 && e.Expense != 0;
                bool negative = e.Income < 0 || e.Expense < 0;
                if (bothZero || bothSet || negative) { quarantined++; continue; }

                // ── 2) 구분/금액 결정: 금액이 들어있는 컬럼이 곧 거래 유형(데이터 신뢰도 우선) ──
                LedgerTransactionType type;
                decimal amount;
                if (e.Income > 0) { type = LedgerTransactionType.Income; amount = e.Income; }
                else { type = LedgerTransactionType.Expense; amount = e.Expense; }

                // ── 3) BudgetPlan(예산 항목) 매칭: 부서·연도·항목명·구분 (Trim + 대소문자 무시) ──
                bool isIncome = type == LedgerTransactionType.Income;
                string cat = (e.Category ?? "").Trim();
                var plan = budgetPlans.FirstOrDefault(b =>
                    b.DepartmentId == e.DepartmentId &&
                    b.Year == e.FiscalYear &&
                    string.Equals((b.Category ?? "").Trim(), cat, StringComparison.OrdinalIgnoreCase) &&
                    (isIncome ? IsIncomeTypeString(b.Type) : IsExpenseTypeString(b.Type)));
                if (plan != null) linked++;

                // ── 4) 신규 장부 행 구성 (적요는 내역 우선, 없으면 비고 사용) ──
                string desc = !string.IsNullOrWhiteSpace(e.Description) ? e.Description : (e.Note ?? "");

                newRows.Add(new LedgerTransaction
                {
                    TransactionDate = e.Date,
                    Type = type,
                    Amount = amount,
                    Description = desc,
                    BudgetPlanId = plan?.Id,      // 매칭 실패 시 null → FK 위반 없음
                    AnnualPlanId = null,           // 레거시 데이터는 연간계획 미연결
                    ExpenseResolutionId = null,    // 결의서 미연결
                    CreatedBy = "LedgerMigration"  // CreatedAt 은 SaveChanges 오버라이드가 자동 기록
                });
            }

            db.LedgerTransactions.AddRange(newRows);
            await db.SaveChangesAsync();

            return new LedgerMigrationResult
            {
                SchemaReady = true,
                Migrated = newRows.Count,
                LinkedToBudget = linked,
                Quarantined = quarantined,
                Message = $"{newRows.Count}건 이관 완료 (예산항목 연결 {linked}건, 이상치 격리 {quarantined}건)."
            };
        }

        // 예산 항목 구분 문자열 정규화 비교 (수입/Income, 지출/Expense — 대소문자·공백 무시)
        private static bool IsIncomeTypeString(string? t)
        {
            string v = (t ?? "").Trim();
            return string.Equals(v, "수입", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Income", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsExpenseTypeString(string? t)
        {
            string v = (t ?? "").Trim();
            return string.Equals(v, "지출", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Expense", StringComparison.OrdinalIgnoreCase);
        }

        // SQLite sqlite_master 를 조회해 특정 테이블 존재 여부를 확인한다.
        // (EnsureCreated 기반 프로젝트라 신규 테이블이 기존 DB에 없을 수 있어 사전 점검에 사용)
        private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName)
        {
            var conn = db.Database.GetDbConnection();
            bool opened = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                opened = true;
            }
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                var p = cmd.CreateParameter();
                p.ParameterName = "$name";
                p.Value = tableName;
                cmd.Parameters.Add(p);
                var result = await cmd.ExecuteScalarAsync();
                return result != null && Convert.ToInt64(result) > 0;
            }
            finally
            {
                if (opened) await conn.CloseAsync();
            }
        }

        // =========================================================
        // CSV/XLS 은행 거래내역 파싱
        // =========================================================
        public async Task<List<LedgerEntry>> ParseAndClassifyBankCsvAsync(Stream fileStream, int departmentId)
        {
            using var db = _dbFactory.CreateDbContext();

            var list = new List<LedgerEntry>();
            var dbMappings = await db.CategoryMappings.AsNoTracking()
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
                (await db.Transactions.AsNoTracking()
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

        public async Task<int> GetQuarterNumberAsync(int deptId, int fiscalYear, DateTime date)
        {
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q2 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 2);
            var q3 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 3);
            if (date.Date >= q1.Start.Date && date.Date <= q1.End.Date) return 1;
            if (date.Date >= q2.Start.Date && date.Date <= q2.End.Date) return 2;
            if (date.Date >= q3.Start.Date && date.Date <= q3.End.Date) return 3;
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
        public async Task<List<BudgetPlan>> GetBudgetPlansAsync(int deptId, int year, string type)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Year == year && b.Type == type).ToListAsync();
        }
        public async Task SaveBudgetPlanAsync(BudgetPlan plan)
        {
            if (plan == null) return;

            using var db = _dbFactory.CreateDbContext();

            if (!string.IsNullOrEmpty(plan.Type))
            {
                if (plan.Type.Equals("수입", StringComparison.OrdinalIgnoreCase)) plan.Type = "Income";
                else if (plan.Type.Equals("지출", StringComparison.OrdinalIgnoreCase)) plan.Type = "Expense";
            }

            if (plan.Id == 0) db.BudgetPlans.Add(plan);
            else { var ex = await db.BudgetPlans.FindAsync(plan.Id); if (ex != null) db.Entry(ex).CurrentValues.SetValues(plan); }
            await db.SaveChangesAsync();
        }
        public async Task DeleteBudgetPlanAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var t = await db.BudgetPlans.FindAsync(id); if (t != null) { db.BudgetPlans.Remove(t); await db.SaveChangesAsync(); }
        }

        public async Task<List<CategoryMapping>> GetMappingsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.CategoryMappings.AsNoTracking().Where(m => m.DepartmentId == deptId).ToListAsync();
        }
        public async Task SaveMappingAsync(CategoryMapping m)
        {
            using var db = _dbFactory.CreateDbContext();
            if (m.Id == 0) db.CategoryMappings.Add(m); else { var ex = await db.CategoryMappings.FindAsync(m.Id); if (ex != null) db.Entry(ex).CurrentValues.SetValues(m); } await db.SaveChangesAsync();
        }
        public async Task DeleteMappingAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var m = await db.CategoryMappings.FindAsync(id); if (m != null) { db.CategoryMappings.Remove(m); await db.SaveChangesAsync(); }
        }

        // =========================================================
        // 🚀 환경설정 엑셀 백업/복원 — 2개 시트(예산항목 / 자동분류)
        // =========================================================

        // 해당 연도 예산항목 + 자동분류 규칙을 2개 시트 엑셀로 내보냅니다.
        // isSystemAdmin == true: 전체 부서 데이터를 내보냄 / false: 해당 deptId만.
        // 모든 시트의 A열에 '부서명'을 출력합니다.
        public async Task<byte[]> ExportSettingsExcelAsync(int deptId, int year, bool isSystemAdmin)
        {
            using var db = _dbFactory.CreateDbContext();

            var budgetQuery = db.BudgetPlans.AsNoTracking().Where(b => b.Year == year);
            // 분기설정(Quarter_*) 등 시스템용 매핑은 제외하고 실제 자동분류 규칙만 내보냄
            var mappingQuery = db.CategoryMappings.AsNoTracking().Where(m => !m.Keyword.StartsWith("Quarter_"));

            if (!isSystemAdmin)
            {
                budgetQuery = budgetQuery.Where(b => b.DepartmentId == deptId);
                mappingQuery = mappingQuery.Where(m => m.DepartmentId == deptId);
            }

            var budgets = await budgetQuery.OrderBy(b => b.DepartmentId).ThenBy(b => b.Type).ThenBy(b => b.Category).ToListAsync();
            var mappings = await mappingQuery.OrderBy(m => m.DepartmentId).ThenBy(m => m.Category).ThenBy(m => m.Keyword).ToListAsync();

            var deptNames = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
            string DeptName(int id) => deptNames.TryGetValue(id, out var n) ? n : "";

            // 자동분류 구분(수입/지출)은 (부서, 항목)이 해당 부서 수입 예산 항목군에 속하는지로 추론
            var incomeKey = budgets.Where(b => b.Type == "Income" || b.Type == "수입").Select(b => (b.DepartmentId, b.Category)).ToHashSet();

            using var wb = new XLWorkbook();

            // Sheet 1: 예산항목 — 부서명 | 구분 | 항목명 | 예산금액
            var ws1 = wb.Worksheets.Add("예산항목");
            string[] h1 = { "부서명", "구분", "항목명", "예산금액" };
            for (int i = 0; i < h1.Length; i++) { ws1.Cell(1, i + 1).Value = h1[i]; ws1.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray; ws1.Cell(1, i + 1).Style.Font.Bold = true; }
            int r1 = 2;
            foreach (var b in budgets)
            {
                ws1.Cell(r1, 1).Value = DeptName(b.DepartmentId);
                ws1.Cell(r1, 2).Value = (b.Type == "Income" || b.Type == "수입") ? "수입" : "지출";
                ws1.Cell(r1, 3).Value = b.Category;
                ws1.Cell(r1, 4).Value = b.Amount;
                r1++;
            }
            ws1.Column(1).Width = 18; ws1.Column(2).Width = 10; ws1.Column(3).Width = 30; ws1.Column(4).Width = 18;

            // Sheet 2: 자동분류 — 부서명 | 구분 | 키워드 | 분류될항목
            var ws2 = wb.Worksheets.Add("자동분류");
            string[] h2 = { "부서명", "구분", "키워드", "분류될항목" };
            for (int i = 0; i < h2.Length; i++) { ws2.Cell(1, i + 1).Value = h2[i]; ws2.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray; ws2.Cell(1, i + 1).Style.Font.Bold = true; }
            int r2 = 2;
            foreach (var m in mappings)
            {
                ws2.Cell(r2, 1).Value = DeptName(m.DepartmentId);
                ws2.Cell(r2, 2).Value = incomeKey.Contains((m.DepartmentId, m.Category)) ? "수입" : "지출";
                ws2.Cell(r2, 3).Value = m.Keyword;
                ws2.Cell(r2, 4).Value = m.Category;
                r2++;
            }
            ws2.Column(1).Width = 18; ws2.Column(2).Width = 10; ws2.Column(3).Width = 40; ws2.Column(4).Width = 30;

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        // 업로드된 엑셀(예산항목/자동분류 시트)을 파싱하여 병합/업데이트합니다.
        // A열 = 부서명. isSystemAdmin == true: A열 부서명대로 각 부서에 분배 /
        // false: 보안상 엑셀 부서명을 무시하고 무조건 currentDeptId에만 반영.
        // 반환: (예산항목 처리 건수, 자동분류 추가 건수)
        public async Task<(int budgetCount, int mappingCount)> ImportSettingsExcelAsync(int currentDeptId, int year, bool isSystemAdmin, Stream fileStream)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var db = _dbFactory.CreateDbContext();
            int budgetCount = 0, mappingCount = 0;

            // 부서명 → DepartmentId 매핑(관리자 전체 분배용)
            var depts = await db.Departments.AsNoTracking().ToListAsync();
            int ResolveDeptId(string name)
            {
                if (!isSystemAdmin) return currentDeptId; // 일반 사용자는 무조건 본인 부서로 강제(보안)
                var hit = depts.FirstOrDefault(d => d.Name.Trim() == (name ?? "").Trim());
                return hit?.Id ?? 0;
            }

            using var reader = ExcelReaderFactory.CreateReader(fileStream);
            var ds = reader.AsDataSet();

            foreach (DataTable table in ds.Tables)
            {
                if (table.TableName == "예산항목")
                {
                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        // A열=부서명, B열=구분, C열=항목명, D열=예산금액
                        string deptName = row[0]?.ToString()?.Trim() ?? "";
                        string typeStr = (table.Columns.Count > 1 ? row[1]?.ToString()?.Trim() : "") ?? "";
                        string cat = (table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : "") ?? "";
                        string amtStr = (table.Columns.Count > 3 ? row[3]?.ToString()?.Trim() : "0") ?? "0";
                        if (string.IsNullOrEmpty(cat)) continue;

                        int targetDeptId = ResolveDeptId(deptName);
                        if (targetDeptId == 0) continue; // 관리자인데 매칭되는 부서명이 없으면 스킵

                        string type = (typeStr == "수입" || typeStr == "Income") ? "Income"
                                    : (typeStr == "지출" || typeStr == "Expense") ? "Expense" : "";
                        if (type == "") continue;
                        decimal.TryParse(amtStr, out decimal amt);

                        // 동일 부서/연도/구분/항목명이면 금액 업데이트, 없으면 신규 추가(병합)
                        var existing = await db.BudgetPlans.FirstOrDefaultAsync(b => b.DepartmentId == targetDeptId && b.Year == year && b.Type == type && b.Category == cat);
                        if (existing == null) db.BudgetPlans.Add(new BudgetPlan { DepartmentId = targetDeptId, Year = year, Type = type, Category = cat, Amount = amt });
                        else existing.Amount = amt;
                        budgetCount++;
                    }
                }
                else if (table.TableName == "자동분류")
                {
                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        // A열=부서명, B열=구분(참고용), C열=키워드, D열=분류될항목
                        string deptName = row[0]?.ToString()?.Trim() ?? "";
                        string keyword = (table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : "") ?? "";
                        string cat = (table.Columns.Count > 3 ? row[3]?.ToString()?.Trim() : "") ?? "";
                        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(cat)) continue;

                        int targetDeptId = ResolveDeptId(deptName);
                        if (targetDeptId == 0) continue;

                        // 키워드 중복 방지: 동일 부서에 같은 키워드가 이미 있으면 건너뜀
                        bool exists = await db.CategoryMappings.AnyAsync(m => m.DepartmentId == targetDeptId && m.Keyword == keyword);
                        if (exists) continue;
                        db.CategoryMappings.Add(new CategoryMapping { DepartmentId = targetDeptId, Keyword = keyword, Category = cat });
                        mappingCount++;
                    }
                }
            }

            await db.SaveChangesAsync();
            return (budgetCount, mappingCount);
        }

        // =========================================================
        // 🚀 [추천 시스템] 자동분류 규칙 분석
        // 선택 기간의 '지출' 장부에서 이미 분류된 내역을 키워드(내역/비고)별로 그룹핑하여
        // 어떤 분류 항목이 가장 자주 귀속되었는지 빈도/비율을 계산하고 추천 분류를 선정합니다.
        // quarter / month 는 0이면 '전체'로 간주하여 필터를 적용하지 않습니다.
        // =========================================================
        public async Task<List<KeywordSuggestion>> AnalyzeMappingSuggestionsAsync(int deptId, int year, int quarter, int month, string type)
        {
            using var db = _dbFactory.CreateDbContext();

            // 구분(type)에 맞춰 수입/지출 장부를 선택적으로 분석 (한글/영문 표기 모두 대응)
            bool isIncome = type == "수입" || type == "Income";

            var query = db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId
                         && t.FiscalYear == year
                         && (isIncome ? (t.Type == "수입" || t.Type == "Income")
                                      : (t.Type == "지출" || t.Type == "Expense")));

            if (quarter > 0) query = query.Where(t => t.Quarter == quarter);
            if (month > 0) query = query.Where(t => t.Date.Month == month);

            var rows = await query.ToListAsync();

            // 🚀 [단어(Token) 기반 분석]
            // 텍스트는 '내역(Description)' 우선, 비어있으면 '비고(Note)'를 사용.
            // 이미 분류된 내역만 학습 대상으로 삼으므로 '미분류'와 빈 분류는 제외.
            // 각 지출 건의 텍스트를 단어로 토큰화한 뒤, (단어 → 분류) 쌍으로 1:1 평탄화(SelectMany)한다.
            // 한 건 안에서 같은 단어가 여러 번 나와도 1표만 인정하도록 건별로 Distinct 처리.
            var tokenCategoryPairs = rows
                .Select(t => new
                {
                    Text = !string.IsNullOrWhiteSpace(t.Description) ? t.Description : (t.Note ?? ""),
                    Category = (t.Category ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "미분류")
                .SelectMany(x => Tokenize(x.Text).Distinct().Select(token => new { Token = token, x.Category }));

            var suggestions = new List<KeywordSuggestion>();

            foreach (var grp in tokenCategoryPairs.GroupBy(p => p.Token))
            {
                int total = grp.Count();

                // 🚀 [필터 완화] 1번만 등장한 단어도 모두 추천 리스트에 노출.
                // (기존 노이즈 제거: if (total < 2) continue; — 과소추출 방지를 위해 비활성화)

                var distribution = grp.GroupBy(p => p.Category)
                    .Select(cg => new CategoryFrequency
                    {
                        Category = cg.Key,
                        Count = cg.Count(),
                        Percentage = Math.Round(cg.Count() * 100.0 / total, 1)
                    })
                    .OrderByDescending(c => c.Count)
                    .ThenBy(c => c.Category)
                    .ToList();

                var top = distribution.First();

                suggestions.Add(new KeywordSuggestion
                {
                    Keyword = grp.Key,
                    TotalCount = total,
                    RecommendedCategory = top.Category,
                    RecommendedPercentage = top.Percentage,
                    Distribution = distribution
                });
            }

            // 분석 가치가 높은(자주 등장한) 단어부터 위로 정렬
            return suggestions
                .OrderByDescending(s => s.TotalCount)
                .ThenByDescending(s => s.RecommendedPercentage)
                .ToList();
        }

        // 회계 장부에서 분류 단서가 되지 못하는 불용어(Stopwords) — 토큰화 후 제외.
        private static readonly HashSet<string> _analysisStopwords = new()
        {
            "지출", "결제", "구입", "구매", "비용", "이체", "송금", "대금", "지급", "현금", "카드", "사용", "기타"
        };

        // 🚀 텍스트 정제 및 토큰화:
        // ① 한글·영문·숫자가 아닌 모든 문자(괄호·쉼표·하이픈·언더스코어 등)를 공백으로 치환
        // ② 공백 기준 분리(빈 항목 제거)
        // ③ 1글자 단어 제외 + 불용어 제외
        private static IEnumerable<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<string>();

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            return sb.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2)                       // 1글자 단어 제외
                .Where(w => !_analysisStopwords.Contains(w));     // 불용어 제외
        }

        // =========================================================
        // 6. 사용자 관리 및 부서 정보 관리
        // =========================================================
        public async Task<List<User>> GetAllUsersAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Users.AsNoTracking().ToListAsync();
        }
        public async Task AddUserAsync(User user)
        {
            using var db = _dbFactory.CreateDbContext();
            if (!await db.Users.AnyAsync(u => u.Username == user.Username)) { db.Users.Add(user); await db.SaveChangesAsync(); }
        }
        public async Task DeleteUserAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { db.Users.Remove(u); await db.SaveChangesAsync(); }
        }
        public async Task ChangePasswordAsync(string id, string pw)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { u.Password = pw; await db.SaveChangesAsync(); }
        }
        public async Task ResetPasswordAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { u.Password = "1234"; await db.SaveChangesAsync(); }
        }

        // 부서 목록 전체 불러오기
        public async Task<List<Department>> GetDepartmentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Departments.AsNoTracking().ToListAsync();
        }

        // 🚀 신규 추가: 단일 부서 정보 가져오기 (환경설정용)
        public async Task<Department?> GetDepartmentAsync(int departmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId);
        }

        // 🚀 신규 추가: 단일 부서 정보 업데이트 (환경설정용)
        public async Task UpdateDepartmentAsync(Department updatedDept)
        {
            using var db = _dbFactory.CreateDbContext();

            var existing = await db.Departments.FindAsync(updatedDept.Id);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(updatedDept);
                await db.SaveChangesAsync();
            }
        }

        // 사용자 정보 통째로 업데이트 (이름, 부서, 활성상태 변경용)
        public async Task UpdateUserAsync(User user)
        {
            using var db = _dbFactory.CreateDbContext();

            var existing = await db.Users.FindAsync(user.Username);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(user);
                await db.SaveChangesAsync();
            }
        }

        // 초기 매핑 셋팅 (유년부 Id 찾아서 저장)
        public async Task EnsureDetailedMappingsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();

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
                if (!await db.CategoryMappings.AnyAsync(x => x.Keyword == kv.Key && x.DepartmentId == deptId))
                    db.CategoryMappings.Add(new CategoryMapping { Keyword = kv.Key, Category = kv.Value, DepartmentId = deptId });
            }
            await db.SaveChangesAsync();
        }

        // =========================================================
        // [7] 지출결의서
        // =========================================================
        public async Task SaveExpenseReportAsync(ExpenseReport report)
        {
            using var db = _dbFactory.CreateDbContext();

            if (report.Id == 0) db.ExpenseReports.Add(report);
            else { var existing = await db.ExpenseReports.FindAsync(report.Id); if (existing != null) db.Entry(existing).CurrentValues.SetValues(report); }
            await db.SaveChangesAsync();
        }

        public async Task<List<ExpenseReport>> GetExpenseReportsAsync(int deptId, int year)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ExpenseReports.AsNoTracking().Where(r => r.DepartmentId == deptId && r.FiscalYear == year).OrderByDescending(r => r.Date).ToListAsync();
        }

        public async Task DeleteExpenseReportAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.ExpenseReports.FindAsync(id); if (target != null) { db.ExpenseReports.Remove(target); await db.SaveChangesAsync(); }
        }
        // =========================================================
        // [신규 추가] 지출결의서 작성용 예산 및 기 신청액 통계 조회 (SQLite 호환성 패치)
        // =========================================================
        public async Task<(decimal TotalReceived, decimal TotalUsed)> GetSubsidyStatusForReportAsync(int deptId, int year)
        {
            using var db = _dbFactory.CreateDbContext();

            // 1. 총 예산(수령액): 해당 연도 수입 예산 총합 (SQLite decimal Sum 오류 방지를 위해 메모리에서 합산)
            var budgetList = await db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Year == year && (b.Type == "Income" || b.Type == "수입"))
                .Select(b => b.Amount)
                .ToListAsync();

            decimal totalBudget = budgetList.Sum();

            // 2. 기 신청액: 해당 연도에 이미 작성된 지출결의서들의 총합
            var usedList = await db.ExpenseReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == year)
                .Select(r => r.TotalAmount)
                .ToListAsync();

            decimal totalUsed = usedList.Sum();

            return (totalBudget, totalUsed);
        }
        private static decimal ParseMoney(string s) => decimal.TryParse((s ?? "").Replace(",", "").Replace("\"", "").Trim(), out decimal r) ? r : 0;

        public async Task<List<LedgerEntry>> GetLedgerAsync(int deptId, int year)
        {
            using var db = _dbFactory.CreateDbContext();

            return await db.Transactions.Where(t => t.DepartmentId == deptId && t.FiscalYear == year)
                .OrderBy(t => t.Date).AsNoTracking().ToListAsync();
        }

        public async Task DeleteLedgerEntryAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var entry = await db.Transactions.FindAsync(id); if (entry != null) { db.Transactions.Remove(entry); await db.SaveChangesAsync(); }
        }
        // 수동 설정 및 11월 4째주 주일 규칙을 모두 반영하여 회계연도를 반환하는 메서드
        // 핵심: Q1 시작일(전년 11월 말)과 Q4 종료일(당년 11월)을 모두 확인하여
        //       날짜가 어느 회계연도에 속하는지 정확히 판단
        public async Task<int> CalculateFiscalYearAsync(int deptId, DateTime date)
        {
            int calendarYear = date.Year;

            // 11월·12월 데이터는 올해 회계연도(calendarYear) 소속인지,
            // 다음 해 회계연도(calendarYear+1) 소속인지 판단해야 함
            if (date.Month == 11 || date.Month == 12)
            {
                // 다음 해 회계연도의 Q1 시작일을 확인
                var q1Next = await GetQuarterDateRangeAsync(deptId, calendarYear + 1, 1);

                // 날짜가 다음 해 Q1 시작일 이후라면 → 다음 해 회계연도 소속
                if (date.Date >= q1Next.Start.Date)
                {
                    return calendarYear + 1;
                }
            }
            return calendarYear;
        }

        // 분기 설정 변경 시, 해당 부서의 관련 장부 데이터 FiscalYear/Quarter를 일괄 재계산
        // isSystemAdmin == true: 모든 부서(관리자/시스템 제외)의 장부를 각각 재계산하고 총 변경건수를 합산.
        public async Task<int> BulkRecalcFiscalYearQuarterAsync(int deptId, int fiscalYear, bool isSystemAdmin = false)
        {
            if (isSystemAdmin)
            {
                using var dbAll = _dbFactory.CreateDbContext();
                var deptIds = await dbAll.Departments.Where(d => d.Name != "관리자" && d.Name != "시스템").Select(d => d.Id).ToListAsync();
                int total = 0;
                foreach (var tid in deptIds)
                    total += await BulkRecalcFiscalYearQuarterAsync(tid, fiscalYear, false);
                return total;
            }

            using var db = _dbFactory.CreateDbContext();

            // 해당 회계연도의 전체 날짜 범위를 구함
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q4 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 4);

            // 이전 회계연도의 범위도 구함 (경계에 있는 데이터 포착용)
            var q1Prev = await GetQuarterDateRangeAsync(deptId, fiscalYear - 1, 1);
            var q4Prev = await GetQuarterDateRangeAsync(deptId, fiscalYear - 1, 4);

            // 넓은 날짜 범위로 해당 부서의 모든 관련 데이터를 한 번에 가져옴
            DateTime rangeStart = q4Prev.Start.Date < q1.Start.Date ? q4Prev.Start.Date : q1.Start.Date;
            DateTime rangeEnd = q4.End.Date.AddDays(60); // 여유 있게

            var targets = await db.Transactions
                .Where(t => t.DepartmentId == deptId && t.Date >= rangeStart && t.Date <= rangeEnd)
                .ToListAsync();

            int updatedCount = 0;
            foreach (var t in targets)
            {
                int newFiscalYear = await CalculateFiscalYearAsync(deptId, t.Date);
                int newQuarter = await GetQuarterNumberAsync(deptId, newFiscalYear, t.Date);

                if (t.FiscalYear != newFiscalYear || t.Quarter != newQuarter)
                {
                    t.FiscalYear = newFiscalYear;
                    t.Quarter = newQuarter;
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
                await db.SaveChangesAsync();

            return updatedCount;
        }

        // ══════════════════════════════════════════════════════════════
        // 🚀 연간 계획(AnnualPlan) — 조회 / 저장 / 삭제 + 날짜 자동계산
        //   회계연도·분기는 부서별 분기 설정을 따르는 기존 메서드
        //   (CalculateFiscalYearAsync / GetQuarterNumberAsync)를 그대로 재사용한다.
        // ══════════════════════════════════════════════════════════════

        // [DTO] 연간 계획 화면에 필요한 계산값 묶음 (날짜 → 회계연도/분기/주차)
        public class PlanDateInfo
        {
            public int FiscalYear { get; set; }
            public byte Quarter { get; set; }
            public int WeekNo { get; set; }       // 연중 주차 (1~53, 일요일 시작)
            public int MonthWeek { get; set; }    // 그 달의 몇째 주 (화면 표시용)
            public int Month { get; set; }        // 화면 표시용 월
        }

        // 행사 날짜 하나로 회계연도·분기·주차를 한 번에 계산.
        public async Task<PlanDateInfo> CalcPlanDateInfoAsync(int deptId, DateTime date)
        {
            int fy = await CalculateFiscalYearAsync(deptId, date);
            int q = await GetQuarterNumberAsync(deptId, fy, date);
            return new PlanDateInfo
            {
                FiscalYear = fy,
                Quarter = (byte)q,
                WeekNo = GetWeekOfYear(date),
                MonthWeek = GetWeekOfMonth(date),
                Month = date.Month
            };
        }

        // 연중 주차 (1월 1일부터, 일요일을 한 주의 시작으로).
        public static int GetWeekOfYear(DateTime date)
        {
            var first = new DateTime(date.Year, 1, 1);
            int firstSunOffset = (int)first.DayOfWeek; // 일=0 … 토=6
            int dayOfYear = date.DayOfYear;            // 1~366
            return (dayOfYear + firstSunOffset - 1) / 7 + 1;
        }

        // 그 달의 몇째 주 (그 달 1일이 속한 주 = 1주차, 일요일 시작).
        public static int GetWeekOfMonth(DateTime date)
        {
            var first = new DateTime(date.Year, date.Month, 1);
            int firstSunOffset = (int)first.DayOfWeek;
            return (date.Day + firstSunOffset - 1) / 7 + 1;
        }

        // 특정 부서·회계연도의 연간 계획 목록 (분기·주차 순 정렬).
        public async Task<List<AnnualPlan>> GetAnnualPlansAsync(int deptId, int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .Where(p => p.DepartmentId == deptId && p.FiscalYear == fiscalYear)
                .OrderBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 전체 부서(관리자/감사 조회용) 연간 계획 목록.
        public async Task<List<AnnualPlan>> GetAllAnnualPlansAsync(int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .Where(p => p.FiscalYear == fiscalYear)
                .OrderBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 연간 계획 저장 (Id==0 신규 추가 / 그 외 수정). 감사 필드는 DbContext가 자동 처리.
        public async Task SaveAnnualPlanAsync(AnnualPlan plan, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            if (plan.Id == 0)
            {
                plan.CreatedBy = actorUsername;
                db.AnnualPlans.Add(plan);
            }
            else
            {
                var existing = await db.AnnualPlans.FirstOrDefaultAsync(p => p.Id == plan.Id);
                if (existing == null) return;
                existing.DepartmentId = plan.DepartmentId;
                existing.EventDate = plan.EventDate;
                existing.EndDate = plan.EndDate;
                existing.FiscalYear = plan.FiscalYear;
                existing.WeekNo = plan.WeekNo;
                existing.Quarter = plan.Quarter;
                existing.Title = plan.Title;
                existing.Description = plan.Description;
                existing.PlannedIncome = plan.PlannedIncome;
                existing.PlannedExpense = plan.PlannedExpense;
                existing.Status = plan.Status;
                existing.UpdatedBy = actorUsername;
            }
            await db.SaveChangesAsync();
        }

        // 연간 계획 소프트 삭제 (IsDeleted = true → 글로벌 쿼리 필터로 자동 제외).
        public async Task DeleteAnnualPlanAsync(int planId, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            var plan = await db.AnnualPlans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null) return;
            plan.IsDeleted = true;
            plan.UpdatedBy = actorUsername;
            await db.SaveChangesAsync();
        }
    }
}
