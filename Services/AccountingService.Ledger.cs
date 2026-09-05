using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using ClosedXML.Excel;
using ExcelDataReader;
using System.Data;
using System.Globalization;

namespace INcheonChurchWeb.Services
{
    // 장부 조회·수정 / 분류 자동완성 / 장부 뷰
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
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

        // =========================================================
        // 장부 입력 자동완성 — 분류항목 후보와 분류별 세부 후보를 함께 돌려준다.
        //   지출: 분류=행사, 세부=비용 종류 / 수입: 분류=회비수입·찬조금 등, 세부=행사명 (2축 원칙)
        public sealed class CategoryMaps
        {
            public List<string> IncomeCategories { get; set; } = new();
            public List<string> ExpenseCategories { get; set; } = new();
            public Dictionary<string, List<string>> IncomeSubs { get; set; } = new();
            public Dictionary<string, List<string>> ExpenseSubs { get; set; } = new();

            public List<string> CategoriesFor(string type) => type == "수입" ? IncomeCategories : ExpenseCategories;

            public List<string> SubsFor(string type, string? category)
            {
                var map = type == "수입" ? IncomeSubs : ExpenseSubs;
                if (!string.IsNullOrWhiteSpace(category) && map.TryGetValue(category.Trim(), out var list)) return list;
                return map.Values.SelectMany(v => v).Distinct().OrderBy(s => s).ToList();
            }
        }

        /// <summary>viewDeptId 0 = 전체 부서 (GetLedgerViewAsync와 같은 규칙).
        /// canViewAll 이 false 면 무엇이 넘어오든 userDeptId 로 좁혀진다.</summary>
        public async Task<CategoryMaps> GetCategoryMapsAsync(int viewDeptId, int userDeptId, bool canViewAll, int year)
        {
            int deptId = ResolveViewDepartmentId(viewDeptId, userDeptId, canViewAll);

            using var db = _dbFactory.CreateDbContext();

            var budgetQuery = db.BudgetPlans.AsNoTracking().Where(b => b.Year == year);
            if (deptId != 0) budgetQuery = budgetQuery.Where(b => b.DepartmentId == deptId);
            var bp = await budgetQuery.ToListAsync();

            var maps = new CategoryMaps
            {
                IncomeCategories = bp.Where(b => b.Type == "Income" || b.Type == "수입")
                                     .Select(b => b.Category.Trim()).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList(),
                ExpenseCategories = bp.Where(b => b.Type == "Expense" || b.Type == "지출")
                                      .Select(b => b.Category.Trim()).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList()
            };

            maps.ExpenseSubs = bp.Where(b => (b.Type == "Expense" || b.Type == "지출")
                                             && !string.IsNullOrWhiteSpace(b.Category)
                                             && !string.IsNullOrWhiteSpace(b.SubCategory))
                                 .GroupBy(b => b.Category.Trim())
                                 .ToDictionary(g => g.Key,
                                               g => g.Select(b => b.SubCategory!.Trim()).Distinct().OrderBy(s => s).ToList());

            // 수입의 세부는 "어느 행사의 회비/찬조금인가" → 지출 분류(행사) 목록을 그대로 쓴다
            var eventNames = maps.ExpenseCategories.ToList();
            maps.IncomeSubs = maps.IncomeCategories.ToDictionary(c => c, _ => new List<string>(eventNames));

            // 장부에 이미 쓰인 값도 후보에 넣는다 (예산에 없는 항목 대응)
            var usedQuery = db.Transactions.AsNoTracking()
                .Where(t => t.Category != null && t.SubCategory != null);
            if (deptId != 0) usedQuery = usedQuery.Where(t => t.DepartmentId == deptId);

            var used = await usedQuery
                .Select(t => new { t.Type, t.Income, t.Category, t.SubCategory })
                .Distinct().ToListAsync();

            foreach (var t in used)
            {
                if (string.IsNullOrWhiteSpace(t.Category) || string.IsNullOrWhiteSpace(t.SubCategory)) continue;

                // 구분 판정은 Type 문자열보다 "금액이 든 컬럼"을 1차 근거로 삼는다.
                // 레거시 데이터에 Type이 비어 있거나 영문("Income")인 경우가 있어서다.
                bool isIncome = t.Income > 0
                    || (t.Income == 0 && (t.Type == "수입" || t.Type == "Income"));

                var map = isIncome ? maps.IncomeSubs : maps.ExpenseSubs;
                var cats = isIncome ? maps.IncomeCategories : maps.ExpenseCategories;

                var key = t.Category.Trim();
                if (!cats.Contains(key)) cats.Add(key);
                if (!map.TryGetValue(key, out var list)) { list = new List<string>(); map[key] = list; }
                var sub = t.SubCategory.Trim();
                if (!list.Contains(sub)) { list.Add(sub); list.Sort(StringComparer.Ordinal); }
            }

            maps.IncomeCategories.Sort(StringComparer.Ordinal);
            maps.ExpenseCategories.Sort(StringComparer.Ordinal);
            return maps;
        }

        // 장부 한 건 추가 — 수정과 같은 규칙으로 회계연도·분기를 날짜에서 계산한다.
        public async Task<int> CreateLedgerEntryAsync(int deptId, DateTime date, string type, string? category,
                                                      string? subCategory, string description, string? note,
                                                      decimal amount)
        {
            using var db = _dbFactory.CreateDbContext();

            var entry = new LedgerEntry
            {
                DepartmentId = deptId,
                Date = date,
                Type = type,
                Category = string.IsNullOrWhiteSpace(category) ? GlobalConstants.CategoryUnclassified : category.Trim(),
                SubCategory = string.IsNullOrWhiteSpace(subCategory) ? null : subCategory.Trim(),
                Description = description ?? "",
                Note = note ?? "",
                ReceiptPath = ""
            };

            if (type == GlobalConstants.TransactionTypeIncome) { entry.Income = amount; entry.Expense = 0; }
            else { entry.Expense = amount; entry.Income = 0; }

            entry.FiscalYear = await CalculateFiscalYearAsync(deptId, date);
            entry.Quarter = await GetQuarterNumberAsync(deptId, entry.FiscalYear, date);

            db.Transactions.Add(entry);
            await db.SaveChangesAsync();
            return entry.Id;
        }

        // 장부 한 건 수정 — 회계연도·분기는 날짜에서 다시 계산하고, 증빙 경로·감사 표시는 건드리지 않는다.
        public async Task SaveLedgerEntryEditAsync(int id, DateTime date, string type, string? category,
                                                   string? subCategory, string description, string? note,
                                                   decimal amount, int? departmentId = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var ex = await db.Transactions.FindAsync(id);
            if (ex == null) return;

            if (departmentId.HasValue && departmentId.Value > 0) ex.DepartmentId = departmentId.Value;

            ex.Date = date;
            ex.Type = type;
            ex.Category = string.IsNullOrWhiteSpace(category) ? GlobalConstants.CategoryUnclassified : category.Trim();
            ex.SubCategory = string.IsNullOrWhiteSpace(subCategory) ? null : subCategory.Trim();
            ex.Description = description ?? "";
            ex.Note = note ?? "";

            if (type == GlobalConstants.TransactionTypeIncome) { ex.Income = amount; ex.Expense = 0; }
            else { ex.Expense = amount; ex.Income = 0; }

            ex.FiscalYear = await CalculateFiscalYearAsync(ex.DepartmentId, date);
            ex.Quarter = await GetQuarterNumberAsync(ex.DepartmentId, ex.FiscalYear, date);

            await db.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════════════
        // 📒 월별 장부 조회 (모바일 화면용) — 회계연도 범위 · 전월 이월금 · 잔액 누계를 한 번에 계산
        // ══════════════════════════════════════════════════════════════
        public sealed class LedgerView
        {
            public List<LedgerEntry> Entries { get; set; } = new();
            public decimal CarryOver { get; set; }                          // 전월 이월금 (회계연도 기준)
            public Dictionary<int, decimal> Balances { get; set; } = new(); // 항목 Id → 그 시점 잔액
            public DateTime RangeStart { get; set; }
            public DateTime RangeEnd { get; set; }

            public decimal TotalIncome => Entries.Sum(e => e.Income);
            public decimal TotalExpense => Entries.Sum(e => e.Expense);
            public decimal Balance => CarryOver + TotalIncome - TotalExpense;
        }

        // viewDeptId 0 = 전체 부서(관리자·감사). month > 0이면 월 우선, 아니면 quarter, 둘 다 0이면 회계연도 전체.
        // type은 "수입"/"지출"/null(전체).
        // canViewAll 이 false 면 viewDeptId 가 무엇이든 userDeptId 로 좁혀진다 → 부서간 열람 차단.
        public async Task<LedgerView> GetLedgerViewAsync(int viewDeptId, int userDeptId, bool canViewAll, int year, int quarter, int month, string? type)
        {
            viewDeptId = ResolveViewDepartmentId(viewDeptId, userDeptId, canViewAll);

            using var db = _dbFactory.CreateDbContext();

            int qDeptId = viewDeptId == 0 ? userDeptId : viewDeptId;
            var q1 = await GetQuarterDateRangeAsync(qDeptId, year, 1);
            var q4 = await GetQuarterDateRangeAsync(qDeptId, year, 4);
            DateTime fyStart = q1.Start.Date, fyEnd = q4.End.Date;

            var query = db.Transactions.AsNoTracking().Include(t => t.DepartmentInfo)
                          .Where(t => t.Date >= fyStart && t.Date <= fyEnd);
            if (viewDeptId != 0) query = query.Where(t => t.DepartmentId == viewDeptId);
            var yearData = await query.OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync();

            // 예산에 등록된 이월금 + 회계연도 이전의 모든 증감
            var bq = db.BudgetPlans.AsNoTracking().Where(b => b.Year == year);
            if (viewDeptId != 0) bq = bq.Where(b => b.DepartmentId == viewDeptId);
            decimal baseCarryOver = (await bq.ToListAsync())
                .FirstOrDefault(b => b.Category.Contains("이월"))?.Amount ?? 0;

            var beforeFy = await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == qDeptId && t.Date < fyStart)
                .Select(t => new { t.Income, t.Expense })
                .ToListAsync();

            var view = new LedgerView
            {
                CarryOver = baseCarryOver + beforeFy.Sum(x => x.Income) - beforeFy.Sum(x => x.Expense),
                RangeStart = fyStart,
                RangeEnd = fyEnd
            };

            IEnumerable<LedgerEntry> scoped = yearData;
            var beforeRange = new List<LedgerEntry>();

            if (month > 0)
            {
                // 회계연도는 12월에 시작하므로 11·12월은 앞선 달력 연도에 속한다
                int calYear = (month == 11 || month == 12) ? year - 1 : year;
                DateTime ms = new DateTime(calYear, month, 1);
                DateTime me = new DateTime(calYear, month, DateTime.DaysInMonth(calYear, month));

                if (month == 11)
                {
                    if (ms < fyStart && calYear == year - 1) ms = fyStart;
                    if (me > fyEnd && calYear == year) me = fyEnd;
                }

                scoped = yearData.Where(t => t.Date.Date >= ms && t.Date.Date <= me);
                beforeRange = yearData.Where(t => t.Date.Date < ms).ToList();
                view.RangeStart = ms; view.RangeEnd = me;
            }
            else if (quarter > 0)
            {
                var qr = await GetQuarterDateRangeAsync(qDeptId, year, quarter);
                scoped = yearData.Where(t => t.Date.Date >= qr.Start.Date && t.Date.Date <= qr.End.Date);
                beforeRange = yearData.Where(t => t.Date.Date < qr.Start.Date).ToList();
                view.RangeStart = qr.Start.Date; view.RangeEnd = qr.End.Date;
            }

            view.CarryOver += beforeRange.Sum(x => x.Income) - beforeRange.Sum(x => x.Expense);

            if (!string.IsNullOrEmpty(type)) scoped = scoped.Where(t => t.Type == type);
            view.Entries = scoped.ToList();

            decimal running = view.CarryOver;
            foreach (var e in view.Entries)
            {
                running += e.Income - e.Expense;
                view.Balances[e.Id] = running;
            }

            return view;
        }
    }
}
