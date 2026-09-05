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
    // 홈 대시보드 집계
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // ══════════════════════════════════════════════════════════════
        // 📊 홈 대시보드 (모바일 화면용) — 예산 대비 집행/수입 현황을 한 번에 계산
        // ══════════════════════════════════════════════════════════════
        public sealed class CategoryStat
        {
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }        // 실제 집행(또는 수입)
            public decimal BudgetAmount { get; set; }  // 편성액
            public int Percentage { get; set; }
        }

        public sealed class SubsidyRequestItem
        {
            public DateTime RequestDate { get; set; }
            public string Title { get; set; } = "";
            public decimal Amount { get; set; }
            public DateTime? DepositDate { get; set; }
        }

        public sealed class DashboardView
        {
            public decimal TotalBudget { get; set; }
            public decimal TotalExpense { get; set; }
            public decimal TotalIncome { get; set; }
            public decimal SubsidyBudget { get; set; }
            public decimal SubsidyRequested { get; set; }
            public int ExpenseRate { get; set; }   // 예산 대비 집행률(%)
            public int IncomeRate { get; set; }    // 보조금 신청 비율(%)
            public List<CategoryStat> ExpenseStats { get; set; } = new();
            public List<CategoryStat> IncomeStats { get; set; } = new();
            public List<SubsidyRequestItem> SubsidyRequests { get; set; } = new();

            public decimal RemainingBudget => TotalBudget - TotalExpense;
        }

        private static bool IsExpenseType(string t) =>
            string.Equals(t, "Expense", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "지출", StringComparison.OrdinalIgnoreCase);

        private static bool IsIncomeType(string t) =>
            string.Equals(t, "Income", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "수입", StringComparison.OrdinalIgnoreCase);

        // deptId 0 = 전체 부서(관리자·감사). departmentNames는 전체 조회일 때 제목 앞에 [부서명]을 붙이는 용도.
        public async Task<DashboardView> GetDashboardAsync(int deptId, int userDeptId, int year,
                                                           IReadOnlyDictionary<int, string>? departmentNames = null)
        {
            using var db = _dbFactory.CreateDbContext();
            var view = new DashboardView();

            var budgetQuery = db.BudgetPlans.AsNoTracking().Where(b => b.Year == year);
            if (deptId != 0) budgetQuery = budgetQuery.Where(b => b.DepartmentId == deptId);
            var budgetList = await budgetQuery.ToListAsync();

            view.TotalBudget = budgetList.Where(b => IsExpenseType(b.Type)).Sum(b => b.Amount);
            view.SubsidyBudget = budgetList.Where(b => IsIncomeType(b.Type) && b.Category == "교회보조금").Sum(b => b.Amount);

            int queryDeptId = deptId == 0 ? userDeptId : deptId;
            var q1 = await GetQuarterDateRangeAsync(queryDeptId, year, 1);
            var q4 = await GetQuarterDateRangeAsync(queryDeptId, year, 4);
            DateTime start = q1.Start.Date, endExclusive = q4.End.Date.AddDays(1);

            var transQuery = db.Transactions.AsNoTracking()
                .Where(t => t.FiscalYear == year && t.Date >= start && t.Date < endExclusive);
            if (deptId != 0) transQuery = transQuery.Where(t => t.DepartmentId == deptId);
            var transactions = await transQuery.ToListAsync();

            view.TotalExpense = transactions.Where(t => IsExpenseType(t.Type)).Sum(t => t.Expense);
            view.TotalIncome = transactions
                .Where(t => IsIncomeType(t.Type) && t.Category != "은행이자" && t.Category != "환급금")
                .Sum(t => t.Income);

            view.ExpenseRate = view.TotalBudget > 0
                ? (int)Math.Round(view.TotalExpense / view.TotalBudget * 100)
                : 0;

            view.ExpenseStats = BuildCategoryStats(budgetList, transactions, IsExpenseType, t => t.Expense);
            view.IncomeStats = BuildCategoryStats(budgetList, transactions, IsIncomeType, t => t.Income, "은행이자", "환급금");

            // 지출결의서 신청현황 — 신청액과 같은 금액의 수입이 신청일 이후에 잡히면 입금된 것으로 본다
            var reportsQuery = db.ExpenseReports.AsNoTracking().Where(r => r.FiscalYear == year);
            if (deptId != 0) reportsQuery = reportsQuery.Where(r => r.DepartmentId == deptId);
            var reports = await reportsQuery.OrderByDescending(r => r.Date).ToListAsync();

            view.SubsidyRequested = reports.Sum(r => r.TotalAmount);
            view.IncomeRate = view.SubsidyBudget > 0
                ? (int)Math.Round(view.SubsidyRequested / view.SubsidyBudget * 100)
                : 0;

            var incomeQuery = db.Transactions.AsNoTracking().Where(t => t.Type == "수입" || t.Type == "Income");
            if (deptId != 0) incomeQuery = incomeQuery.Where(t => t.DepartmentId == deptId);
            var incomeForMatching = await incomeQuery.OrderBy(t => t.Date).ToListAsync();

            foreach (var rep in reports)
            {
                var matched = incomeForMatching.FirstOrDefault(t =>
                    t.Date.Date >= rep.Date.Date &&
                    t.Income == rep.TotalAmount &&
                    (deptId == 0 || t.DepartmentId == rep.DepartmentId));

                string prefix = "";
                if (deptId == 0 && departmentNames != null && departmentNames.TryGetValue(rep.DepartmentId, out var dn))
                    prefix = $"[{dn}] ";

                view.SubsidyRequests.Add(new SubsidyRequestItem
                {
                    RequestDate = rep.Date,
                    Title = prefix + rep.Title,
                    Amount = rep.TotalAmount,
                    DepositDate = matched?.Date
                });

                if (matched != null) incomeForMatching.Remove(matched);
            }

            return view;
        }

        // 예산과 실거래를 합쳐 항목별 집행/달성 통계를 만든다.
        private static List<CategoryStat> BuildCategoryStats(
            List<BudgetPlan> budgetList, List<LedgerEntry> transactions,
            Func<string, bool> isType, Func<LedgerEntry, decimal> amount,
            params string[] excludeCategories)
        {
            var categories = budgetList.Where(b => isType(b.Type)).Select(b => b.Category)
                .Union(transactions.Where(t => isType(t.Type) && amount(t) > 0).Select(t => t.Category))
                .Where(c => !string.IsNullOrWhiteSpace(c) && !excludeCategories.Contains(c))
                .Distinct();

            return categories.Select(cat =>
            {
                decimal budgetAmount = budgetList.Where(b => isType(b.Type) && b.Category == cat).Sum(b => b.Amount);
                decimal actual = transactions.Where(t => isType(t.Type) && t.Category == cat).Sum(amount);
                return new CategoryStat
                {
                    Category = cat,
                    Amount = actual,
                    BudgetAmount = budgetAmount,
                    // 예산 0인데 실적만 있는 '예산 외' 항목은 100%로 표시한다.
                    // (0으로 두면 화면에서 '아무것도 안 쓴 것'처럼 보인다)
                    Percentage = budgetAmount > 0 ? (int)Math.Round(actual / budgetAmount * 100)
                                                  : (actual > 0 ? 100 : 0)
                };
            }).ToList();
        }
    }
}
