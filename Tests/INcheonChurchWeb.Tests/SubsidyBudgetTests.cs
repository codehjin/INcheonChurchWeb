using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 지출결의서의 '총 예산(보조금)'이 대시보드의 '수입 예산 달성률(교회보조금)' 및
/// 환경설정 수입예산의 교회보조금 항목과 같은 수치임을 고정한다.
///
/// 예전에는 지출결의서만 수입 예산 '총액'(주일헌금·회비까지 포함)을 한도로 써서
/// 화면마다 다른 금액이 보였다. 지출결의서는 교회에 보조금을 신청하는 문서이므로
/// 한도는 보조금 예산이어야 한다.
/// </summary>
public class SubsidyBudgetTests : IDisposable
{
    private const int DeptId = 3;
    private const int Year = 2026;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public SubsidyBudgetTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private void 예산(string type, string category, decimal amount, int deptId = DeptId)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = deptId, Year = Year, Type = type,
            Category = category, Amount = amount
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task 총예산은_교회보조금_항목만_센다()
    {
        예산("수입", "교회보조금", 3_000_000);
        예산("수입", "주일헌금", 500_000);
        예산("수입", "회비수입", 200_000);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);

        Assert.Equal(3_000_000, totalReceived);
    }

    [Fact]
    public async Task 지출결의서와_대시보드가_같은_금액을_본다()
    {
        예산("수입", "교회보조금", 3_000_000);
        예산("수입", "주일헌금", 500_000);
        예산("지출", "행사비", 1_000_000);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);
        var dash = await _svc.GetDashboardAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(dash.SubsidyBudget, totalReceived);
    }

    [Fact]
    public async Task 영문_Income_타입으로_저장돼도_센다()
    {
        예산("Income", "교회보조금", 1_500_000);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);

        Assert.Equal(1_500_000, totalReceived);
    }

    [Fact]
    public async Task 보조금_항목이_여러_줄이면_합산한다()
    {
        예산("수입", "교회보조금", 1_000_000);
        예산("수입", "교회보조금", 2_000_000);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);

        Assert.Equal(3_000_000, totalReceived);
    }

    [Fact]
    public async Task 다른_부서의_보조금은_섞이지_않는다()
    {
        예산("수입", "교회보조금", 3_000_000);
        예산("수입", "교회보조금", 9_000_000, deptId: 4);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);

        Assert.Equal(3_000_000, totalReceived);
    }

    [Fact]
    public async Task 보조금_예산이_없으면_0이다()
    {
        예산("수입", "주일헌금", 500_000);

        var (totalReceived, _) = await _svc.GetSubsidyStatusForReportAsync(DeptId, Year);

        Assert.Equal(0, totalReceived);
    }
}
