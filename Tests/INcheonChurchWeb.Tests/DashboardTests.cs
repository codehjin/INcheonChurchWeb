using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 대시보드 집계를 고정한다. (웹_상세_구조문서.md §6.3)
///
/// 웹(<c>Home.razor</c>)과 모바일(<c>MobileHome.razor</c>)이 모두 이 메서드를 쓴다.
/// 예전에는 Home.razor에 같은 계산이 복제되어 있었고 예산 0인 항목의 집행률이 달랐다(0 vs 100).
/// 지금은 100으로 통일했다.
/// </summary>
public class DashboardTests : IDisposable
{
    private const int DeptId = 3;
    private const int Year = 2026;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public DashboardTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private void 예산(string type, string category, decimal amount)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = DeptId, Year = Year, Type = type, Category = category, Amount = amount
        });
        db.SaveChanges();
    }

    private void 거래(string date, string category, decimal income = 0, decimal expense = 0)
    {
        using var db = _db.CreateDbContext();
        db.Transactions.Add(new LedgerEntry
        {
            DepartmentId = DeptId,
            Date = DateTime.Parse(date),
            FiscalYear = Year,                      // 대시보드는 FiscalYear 컬럼으로 거른다
            Type = income > 0 ? "수입" : "지출",
            Category = category,
            Income = income,
            Expense = expense,
            Description = "테스트"
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task 지출_집행률은_실적_나누기_편성이다()
    {
        예산("지출", "여름성경학교", 2_000_000);
        거래("2026-07-10", "여름성경학교", expense: 1_500_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(2_000_000, view.TotalBudget);
        Assert.Equal(1_500_000, view.TotalExpense);
        Assert.Equal(75, view.ExpenseRate);
        Assert.Equal(500_000, view.RemainingBudget);
    }

    [Fact]
    public async Task 예산을_넘겨도_집행률을_100으로_깎지_않는다()
    {
        예산("지출", "여름성경학교", 1_000_000);
        거래("2026-07-10", "여름성경학교", expense: 1_500_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(150, view.ExpenseRate);          // 초과분이 그대로 보여야 한다
        Assert.Equal(-500_000, view.RemainingBudget);
    }

    [Fact]
    public async Task 예산_외_항목은_집행률_100으로_표시한다()
    {
        // 편성이 없는데 쓴 항목. 0%로 두면 화면에서 "안 쓴 것"처럼 보인다.
        거래("2026-07-10", "훈련비", expense: 350_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);
        var stat = Assert.Single(view.ExpenseStats);

        Assert.Equal("훈련비", stat.Category);
        Assert.Equal(0, stat.BudgetAmount);
        Assert.Equal(350_000, stat.Amount);
        Assert.Equal(100, stat.Percentage);
    }

    [Fact]
    public async Task 은행이자와_환급금은_수입에서_뺀다()
    {
        거래("2026-03-10", "회비수입", income: 1_000_000);
        거래("2026-03-11", "은행이자", income: 500);
        거래("2026-03-12", "환급금", income: 20_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(1_000_000, view.TotalIncome);
        Assert.DoesNotContain(view.IncomeStats, s => s.Category == "은행이자");
        Assert.DoesNotContain(view.IncomeStats, s => s.Category == "환급금");
    }

    [Fact]
    public async Task 예산에만_있고_실적이_없는_항목도_집계에_남는다()
    {
        // 편성했는데 아직 안 쓴 항목이 목록에서 사라지면 안 된다
        예산("지출", "겨울성경학교", 2_100_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);
        var stat = Assert.Single(view.ExpenseStats);

        Assert.Equal(2_100_000, stat.BudgetAmount);
        Assert.Equal(0, stat.Amount);
        Assert.Equal(0, stat.Percentage);
    }

    [Fact]
    public async Task 보조금_신청_비율은_결의서_신청액을_교회보조금_예산으로_나눈다()
    {
        예산("수입", "교회보조금", 4_000_000);

        using (var db = _db.CreateDbContext())
        {
            db.ExpenseReports.Add(new ExpenseReport
            {
                DepartmentId = DeptId, FiscalYear = Year, Date = DateTime.Parse("2026-05-28"),
                Title = "2분기 지출결의서", TotalAmount = 1_000_000
            });
            db.SaveChanges();
        }

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(1_000_000, view.SubsidyRequested);
        Assert.Equal(25, view.IncomeRate);
        Assert.Single(view.SubsidyRequests);
        Assert.Null(view.SubsidyRequests[0].DepositDate);   // 아직 입금 전
    }

    [Fact]
    public async Task 신청액과_같은_수입이_나중에_잡히면_입금으로_본다()
    {
        예산("수입", "교회보조금", 4_000_000);
        거래("2026-06-05", "교회보조금", income: 1_000_000);   // 신청일 이후, 같은 금액

        using (var db = _db.CreateDbContext())
        {
            db.ExpenseReports.Add(new ExpenseReport
            {
                DepartmentId = DeptId, FiscalYear = Year, Date = DateTime.Parse("2026-05-28"),
                Title = "2분기 지출결의서", TotalAmount = 1_000_000
            });
            db.SaveChanges();
        }

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(new DateTime(2026, 6, 5), view.SubsidyRequests[0].DepositDate);
    }

    [Fact]
    public async Task 예산이_없으면_집행률은_0이고_나눗셈이_터지지_않는다()
    {
        거래("2026-07-10", "여름성경학교", expense: 100_000);

        var view = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(0, view.TotalBudget);
        Assert.Equal(0, view.ExpenseRate);
        Assert.Equal(0, view.IncomeRate);
    }
}
