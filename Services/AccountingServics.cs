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
            // 변수명을 'transaction'에서 'entry'로 변경하여 컨텍스트 오류 해결
            var entry = await _db.Transactions.FindAsync(id);

            if (entry != null)
            {
                // 1. 실제 서버에서 파일 삭제
                if (!string.IsNullOrEmpty(entry.ReceiptPath))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, entry.ReceiptPath.TrimStart('/'));
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }

                // 2. DB의 경로 값을 빈 문자열로 업데이트 (NOT NULL 제약조건 우회)
                entry.ReceiptPath = "";

                await _db.SaveChangesAsync();
            }
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

        // 배치 저장: 여러 건을 일괄 추가하며 회계연도/분기 및 기본값을 적용
        public async Task AddTransactionsAsync(List<LedgerEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            foreach (var entry in entries)
            {
                entry.Id = 0;
                if (string.IsNullOrEmpty(entry.Note)) entry.Note = "";
                if (string.IsNullOrEmpty(entry.Category)) entry.Category = "미분류";
                if (string.IsNullOrEmpty(entry.Department)) entry.Department = "유년부";

                // 회계연도 계산: 기본은 거래년, 단 11~12월 특수 처리(프로젝트 로직과 일치)
                int fiscalYear = entry.FiscalYear == 0 ? entry.Date.Year : entry.FiscalYear;
                if (entry.Date.Month == 11 || entry.Date.Month == 12)
                {
                    var q4End = await GetQuarterDateRangeAsync(entry.Department, entry.Date.Year, 4);
                    if (entry.Date > q4End.End) fiscalYear = entry.Date.Year + 1;
                }

                int quarter = await GetQuarterNumberAsync(entry.Department, fiscalYear, entry.Date);

                entry.FiscalYear = fiscalYear;
                entry.Quarter = quarter;

                _db.Transactions.Add(entry);
            }

            await _db.SaveChangesAsync();
        }

        // 최근 거래일 반환 (UI에서 중복 방지용)
        public async Task<DateTime?> GetLastTransactionDateAsync(string dept)
        {
            return await _db.Transactions.AsNoTracking()
                .Where(t => t.Department == dept)
                .OrderByDescending(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateTransactionAsync(LedgerEntry entry) { var ex = await _db.Transactions.FindAsync(entry.Id); if (ex != null) { if (string.IsNullOrEmpty(entry.Note)) entry.Note = ""; _db.Entry(ex).CurrentValues.SetValues(entry); await _db.SaveChangesAsync(); } }
        public async Task DeleteTransactionAsync(int id) { var target = await _db.Transactions.FindAsync(id); if (target != null) { _db.Transactions.Remove(target); await _db.SaveChangesAsync(); } }

        public async Task<List<LedgerEntry>> GetMonthlyTransactionsAsync(string dept, int year, int month) => await _db.Transactions.AsNoTracking().Where(t => t.Department == dept && t.Date.Year == year && t.Date.Month == month).OrderBy(t => t.Date).ToListAsync();
        public async Task<List<LedgerEntry>> GetAllTransactionsAsync(int year) => await _db.Transactions.AsNoTracking().Where(t => t.FiscalYear == year).ToListAsync();

        // =========================================================
        // CSV/XLS 은행 거래내역 파싱 (하나은행 양식 기준)
        // 컬럼 순서: 거래일시, 적요, 추가메모, 의뢰인/수취인, 입금, 출금, 거래후잔액, 구분, 거래점, 거래특이사항
        // =========================================================
        public async Task<List<LedgerEntry>> ParseAndClassifyBankCsvAsync(Stream fileStream, string department)
        {
            var list = new List<LedgerEntry>();
            var dbMappings = await _db.CategoryMappings.AsNoTracking()
                .Where(m => m.Department == department).ToListAsync();

            // [수정] EUC-KR 인코딩 우선 시도 (하나은행 CSV 기본 인코딩)
            Encoding encoding;
            try { encoding = Encoding.GetEncoding("euc-kr"); }
            catch { encoding = Encoding.UTF8; }

            // 스트림을 바이트로 읽어 인코딩 자동 감지
            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                rawBytes = ms.ToArray();
            }

            // BOM 확인으로 인코딩 결정
            if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                encoding = Encoding.UTF8; // UTF-8 BOM
            else if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
                encoding = Encoding.Unicode; // UTF-16 LE

            var content = encoding.GetString(rawBytes);
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // [수정] 헤더 행 동적 탐색 (고정 행 스킵 방식 제거)
            int dataStartLine = 0;
            int colDate = -1, colDesc = -1, colMemo = -1, colPerson = -1;
            int colIncome = -1, colExpense = -1, colBalance = -1;

            for (int i = 0; i < Math.Min(lines.Length, 15); i++)
            {
                var cols = SplitCsvLine(lines[i]);
                // 헤더 탐색: '거래일시' 또는 '날짜' 컬럼 찾기
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
                if (colDate >= 0 && colDesc >= 0)
                {
                    dataStartLine = i + 1;
                    break;
                }
            }

            // 헤더를 못 찾으면 하나은행 기본 컬럼 순서 적용
            // 순서: 거래일시(0), 적요(1), 추가메모(2), 의뢰인/수취인(3), 입금(4), 출금(5), 거래후잔액(6)
            if (colDate < 0)
            {
                colDate = 0; colDesc = 1; colMemo = 2; colPerson = 3;
                colIncome = 4; colExpense = 5; colBalance = 6;
                dataStartLine = 2; // 상단 2줄 정보 행 스킵
            }

            // 기존 거래 중복 체크용 해시셋
            var existingKeys = new HashSet<string>(
                (await _db.Transactions.AsNoTracking()
                    .Where(t => t.Department == department)
                    .Select(t => t.Date.ToString("yyyyMMddHHmm") + "_" + t.Description + "_" + t.Income + "_" + t.Expense)
                    .ToListAsync()));

            for (int i = dataStartLine; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var v = SplitCsvLine(line);
                if (v.Count <= Math.Max(colDate, Math.Max(colDesc, Math.Max(colIncome, colExpense)))) continue;

                string Clean(int idx) => idx >= 0 && idx < v.Count
                    ? v[idx].Trim().Trim('"').Trim() : "";

                string dateStr = Clean(colDate);
                if (!DateTime.TryParse(dateStr, out DateTime date)) continue;

                string desc = Clean(colDesc);
                string memo = colMemo >= 0 ? Clean(colMemo) : "";
                string person = colPerson >= 0 ? Clean(colPerson) : "";

                decimal income = ParseMoney(Clean(colIncome));
                decimal expense = ParseMoney(Clean(colExpense));
                decimal balance = colBalance >= 0 ? ParseMoney(Clean(colBalance)) : 0;

                if (income == 0 && expense == 0) continue;

                // [수정] 비고: 의뢰인/수취인 + 추가메모 합산
                string note = string.Join(" ", new[] { person, memo }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

                string type = income > 0 ? "수입" : "지출";

                // [수정] 분류: 적요 + 의뢰인/수취인 전체를 검색 대상으로
                string searchText = $"{desc} {person} {memo}";
                string category = ClassifyTransaction(searchText, type, dbMappings);

                // 중복 체크
                string key = $"{date:yyyyMMddHHmm}_{desc}_{income}_{expense}";
                if (existingKeys.Contains(key)) continue;
                existingKeys.Add(key);

                // 회계년도/분기 계산
                int fiscalYear = date.Year;
                if ((date.Month == 11 || date.Month == 12))
                {
                    var q4End = await GetQuarterDateRangeAsync(department, date.Year, 4);
                    if (date > q4End.End) fiscalYear = date.Year + 1;
                }

                int quarter = await GetQuarterNumberAsync(department, fiscalYear, date);

                list.Add(new LedgerEntry
                {
                    Date = date,
                    Description = desc,
                    Income = income,
                    Expense = expense,
                    Department = department,
                    FiscalYear = fiscalYear,
                    Quarter = quarter,
                    Type = type,
                    Category = category,
                    Note = note
                });
            }
            return list;
        }

        // CSV 행 파싱 (따옴표 안의 쉼표 처리)
        private List<string> SplitCsvLine(string line)
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

        // 분기 번호 계산
        private async Task<int> GetQuarterNumberAsync(string dept, int fiscalYear, DateTime date)
        {
            var q1 = await GetQuarterDateRangeAsync(dept, fiscalYear, 1);
            var q2 = await GetQuarterDateRangeAsync(dept, fiscalYear, 2);
            var q3 = await GetQuarterDateRangeAsync(dept, fiscalYear, 3);
            var q4prev = await GetQuarterDateRangeAsync(dept, fiscalYear - 1, 4);
            if (date >= q4prev.Start && date <= q1.End) return 1;
            if (date > q1.End && date <= q2.End) return 2;
            if (date > q2.End && date <= q3.End) return 3;
            return 4;
        }

        // =========================================================
        // [수정] 강화된 자동 분류 로직 (2025 유년부 실제 데이터 기반)
        // =========================================================
        private string ClassifyTransaction(string text, string type, List<CategoryMapping> mappings)
        {
            // 1순위: DB 사용자 정의 키워드 매칭
            foreach (var m in mappings)
                if (!string.IsNullOrEmpty(m.Keyword) && text.Contains(m.Keyword))
                    return m.Category;

            // 2순위: 수입 기본 규칙
            if (type == "수입")
            {
                if (text.Contains("주정헌금") || text.Contains("주일헌금") || text.Contains("주정힌금") ||
                    text.Contains("주정헌긍") || text.Contains("작정헌금")) return "주일헌금";
                if (text.Contains("인천중앙교회") && !text.Contains("초등부") &&
                    !text.Contains("영유아") && !text.Contains("유치")) return "교회보조금";
                if (text.Contains("후원") || text.Contains("찬조") ||
                    text.Contains("권사회") || text.Contains("집사회")) return "찬조금";
                if (text.Contains("이자") || text.Contains("이자소득") ||
                    text.Contains("예금이자")) return "은행이자";
                if (text.Contains("환급") || text.Contains("반납") ||
                    text.Contains("취소")) return "환급금";
                // 이름만 있는 경우 (개인 송금) → 회비수입
                return "회비수입";
            }

            // 3순위: 지출 기본 규칙
            // 성경학교
            if (text.Contains("성경학교") || text.Contains("볼베어") ||
                text.Contains("웅진플레이") || text.Contains("캠프") ||
                text.Contains("트래블로버") || text.Contains("여행자보험") ||
                text.Contains("손해보험"))
            {
                if (text.Contains("겨울") || text.Contains("12") || text.Contains("1월") ||
                    text.Contains("2월")) return "겨울성경학교";
                return "여름성경학교";
            }
            // 공과비 (매주 공과 관련)
            if (text.Contains("두란노") || text.Contains("세계로") || text.Contains("씨유") ||
                text.Contains("CU") || text.Contains("이마트24") || text.Contains("킹식자재") ||
                text.Contains("탐나는피자") || text.Contains("빵") || text.Contains("컵케익") ||
                text.Contains("약봉투") || text.Contains("점토") || text.Contains("우리동네할인"))
                return "공과비";
            // 교사회의비
            if (text.Contains("파리바게트") || text.Contains("명랑시대") || text.Contains("돌담옥") ||
                text.Contains("뚝배기") || text.Contains("안스") || text.Contains("수푸드") ||
                text.Contains("솔리드퍼퓸") || text.Contains("교사간담"))
                return "교사회의비";
            // 훈련비
            if (text.Contains("QT") || text.Contains("공과책") || text.Contains("훈련") ||
                text.Contains("MT") || text.Contains("춘천") || text.Contains("삼악산") ||
                text.Contains("이디야") || text.Contains("목향원") || text.Contains("KH에너지"))
                return "훈련비";
            // 부서관리비
            if (text.Contains("현수막") || text.Contains("명찰") || text.Contains("마이크") ||
                text.Contains("테이블") || text.Contains("바구니") || text.Contains("테이블보"))
                return "부서관리비";
            // 행사비 (다이소, 쿠팡, 마트류)
            if (text.Contains("다이소") || text.Contains("아트박스") || text.Contains("크로바") ||
                text.Contains("와글") || text.Contains("캣플") || text.Contains("롤링파스타") ||
                text.Contains("101번지") || text.Contains("중화가정") || text.Contains("암송") ||
                text.Contains("꽃") || text.Contains("펜던트") || text.Contains("십자가"))
                return "행사비";
            if (text.Contains("쿠팡") || text.Contains("네이버파이낸셜") ||
                text.Contains("카카오페이") || text.Contains("비바리퍼블리카"))
                return "행사비"; // 쿠팡/간편결제는 행사비 기본 (키워드 매핑으로 세분화 권장)

            return "미분류";
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
            var exists = await _db.BudgetPlans.AnyAsync(b => b.Department == department && b.Year == targetYear);
            if (exists) return false;
            var sourceData = await _db.BudgetPlans.AsNoTracking().Where(b => b.Department == department && b.Year == baseYear).ToListAsync();
            foreach (var plan in sourceData) { _db.BudgetPlans.Add(new BudgetPlan { Department = department, Year = targetYear, Type = plan.Type, Category = plan.Category, SubCategory = plan.SubCategory, CalcDetail = plan.CalcDetail, Amount = plan.Amount }); }
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task EnsureDetailedMappingsAsync(string dept = "유년부")
        {
            // 2025 유년부 실제 거래내역 기반 키워드 매핑
            var mapData = new Dictionary<string, string>
            {
                // 수입
                { "인천중앙교회",    "교회보조금" },
                { "주정헌금",        "주일헌금"   },
                { "주일헌금",        "주일헌금"   },
                { "이자",            "은행이자"   },
                { "후원",            "찬조금"     },
                { "환급",            "환급금"     },
                { "반납",            "환급금"     },
                // 지출 - 공과비
                { "두란노",          "공과비"     },
                { "세계로",          "공과비"     },
                { "씨유",            "공과비"     },
                { "이마트24",        "공과비"     },
                { "우리동네할인",    "공과비"     },
                { "킹식자재",        "공과비"     },
                { "탐나는피자",      "공과비"     },
                // 지출 - 교사회의비
                { "파리바게트",      "교사회의비" },
                { "명랑시대",        "교사회의비" },
                { "돌담옥",          "교사회의비" },
                { "뚝배기이탈리아",  "교사회의비" },
                { "수푸드",          "교사회의비" },
                // 지출 - 행사비
                { "다이소",          "행사비"     },
                { "아트박스",        "행사비"     },
                { "크로바",          "행사비"     },
                { "와글",            "행사비"     },
                { "캣플",            "행사비"     },
                { "롤링파스타",      "행사비"     },
                { "알짜마트",        "행사비"     },
                // 지출 - 부서관리비
                { "현수막",          "부서관리비" },
                { "명찰",            "부서관리비" },
                { "테이블",          "부서관리비" },
                // 지출 - 훈련비
                { "QT",              "훈련비"     },
                { "목향원",          "훈련비"     },
                { "삼악산",          "훈련비"     },
                { "이디야",          "훈련비"     },
                { "KH에너지",        "훈련비"     },
                // 지출 - 여름성경학교
                { "볼베어",          "여름성경학교" },
                { "웅진플레이",      "여름성경학교" },
                { "트래블로버",      "여름성경학교" },
                { "한솥도시락",      "여름성경학교" },
                { "캠프",            "여름성경학교" },
            };
            foreach (var kv in mapData)
            {
                if (!await _db.CategoryMappings.AnyAsync(x => x.Keyword == kv.Key && x.Department == dept))
                    _db.CategoryMappings.Add(new CategoryMapping
                    {
                        Keyword = kv.Key,
                        Category = kv.Value,
                        Department = dept
                    });
            }
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

        // 금액 문자열 파싱 (쉼표, 따옴표, 공백 제거)
        private decimal ParseMoney(string s)
            => decimal.TryParse((s ?? "").Replace(",", "").Replace("\"", "").Trim(), out decimal r) ? r : 0;

        // =========================================================
        // [추가] 월별 장부 페이지용 메서드 (오류 해결용)
        // =========================================================

        // 1. 특정 부서, 특정 연도의 모든 거래 내역 가져오기
        public async Task<List<LedgerEntry>> GetLedgerAsync(string dept, int year)
        {
            return await _db.Transactions
                .Where(t => t.Department == dept && t.FiscalYear == year)
                .OrderBy(t => t.Date) // 날짜순 정렬
                .AsNoTracking()       // 조회 전용 (속도 향상)
                .ToListAsync();
        }

        // 2. 거래 내역 삭제하기
        public async Task DeleteLedgerEntryAsync(int id)
        {
            var entry = await _db.Transactions.FindAsync(id);
            if (entry != null)
            {
                _db.Transactions.Remove(entry);
                await _db.SaveChangesAsync();
            }
        }
    }
}