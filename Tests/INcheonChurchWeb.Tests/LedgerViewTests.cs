using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 장부 조회의 핵심 계산을 고정한다. (웹_상세_구조문서.md §3.5)
///
///   이월금 = 예산의 Category.Contains("이월") 금액
///          + (회계연도 시작 이전 모든 거래의 수입 − 지출)
///          + (조회 기간 시작 이전, 회계연도 안의 수입 − 지출)
///   잔액   = 이월금에서 시작해 행마다 (수입 − 지출) 누적
///
/// 이 식이 어긋나면 장부·인쇄·대시보드가 한꺼번에 틀어진다.
/// </summary>
public class LedgerViewTests : IDisposable
{
    private const int DeptId = 3;      // 유년부라고 가정
    private const int Year = 2026;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public LedgerViewTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    // ── 준비 헬퍼 ────────────────────────────────────────────────────
    private void 예산_이월금(decimal amount)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = DeptId, Year = Year, Type = "수입",
            Category = "전년도이월금", Amount = amount
        });
        db.SaveChanges();
    }

    private void 거래(string date, decimal income = 0, decimal expense = 0,
                     string category = "여름성경학교", int deptId = DeptId)
    {
        using var db = _db.CreateDbContext();
        db.Transactions.Add(new LedgerEntry
        {
            DepartmentId = deptId,
            Date = DateTime.Parse(date),
            Type = income > 0 ? GlobalConstants.TransactionTypeIncome : GlobalConstants.TransactionTypeExpense,
            Category = category,
            Income = income,
            Expense = expense,
            Description = $"{date} 거래"
        });
        db.SaveChanges();
    }

    // ── 이월금 ───────────────────────────────────────────────────────
    [Fact]
    public async Task 예산에_등록된_이월금이_출발점이_된다()
    {
        예산_이월금(1_000_000);

        var view = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 0, null);

        Assert.Equal(1_000_000, view.CarryOver);
        Assert.Equal(1_000_000, view.Balance);
    }

    [Fact]
    public async Task 회계연도_이전_거래가_이월금에_합산된다()
    {
        예산_이월금(1_000_000);
        // 2026 회계연도는 2025-11-24에 시작한다. 그 이전 거래는 이월금으로 접힌다.
        거래("2025-06-10", income: 500_000);
        거래("2025-06-20", expense: 200_000);

        var view = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 0, null);

        Assert.Equal(1_300_000, view.CarryOver);   // 100만 + 50만 − 20만
        Assert.Empty(view.Entries);                 // 조회 기간 밖이라 목록에는 없다
    }

    [Fact]
    public async Task 조회_기간_이전_거래도_이월금으로_접힌다()
    {
        예산_이월금(1_000_000);
        거래("2026-03-10", income: 400_000);   // 2분기
        거래("2026-07-10", expense: 100_000);  // 3분기

        var q3 = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 3, 0, null);

        Assert.Equal(1_400_000, q3.CarryOver);          // 3분기 이전 증감이 접힘
        Assert.Single(q3.Entries);
        Assert.Equal(1_300_000, q3.Balance);            // 140만 − 10만
    }

    // ── 잔액 누계 ────────────────────────────────────────────────────
    [Fact]
    public async Task 잔액은_이월금에서_시작해_행마다_누적된다()
    {
        예산_이월금(1_000_000);
        거래("2026-03-05", expense: 100_000);
        거래("2026-03-10", income: 50_000);
        거래("2026-03-15", expense: 30_000);

        var view = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 3, null);

        var balances = view.Entries.Select(e => view.Balances[e.Id]).ToList();

        Assert.Equal(new decimal[] { 900_000, 950_000, 920_000 }, balances);
        Assert.Equal(920_000, view.Balance);
        Assert.Equal(50_000, view.TotalIncome);
        Assert.Equal(130_000, view.TotalExpense);
    }

    // ── 분기·월 스코프 ───────────────────────────────────────────────
    [Fact]
    public async Task 분기_경계일이_정확히_적용된다()
    {
        // 2026 Q1 = 2025-11-24 ~ 2026-02-22
        거래("2026-02-22", expense: 10_000);   // Q1 마지막 날
        거래("2026-02-23", expense: 20_000);   // Q2 첫날

        var q1 = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 1, 0, null);
        var q2 = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 2, 0, null);

        Assert.Single(q1.Entries);
        Assert.Equal(new DateTime(2026, 2, 22), q1.Entries[0].Date);
        Assert.Single(q2.Entries);
        Assert.Equal(new DateTime(2026, 2, 23), q2.Entries[0].Date);
    }

    [Fact]
    public async Task 월이_지정되면_분기보다_우선한다()
    {
        거래("2026-03-10", expense: 10_000);
        거래("2026-04-10", expense: 20_000);

        var march = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 2, 3, null);

        Assert.Single(march.Entries);
        Assert.Equal(3, march.Entries[0].Date.Month);
    }

    [Fact]
    public async Task 십이월은_앞선_달력연도에_속한다()
    {
        // 2026 회계연도의 12월은 2025년 12월이다
        거래("2025-12-10", expense: 10_000);

        var dec = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 12, null);

        Assert.Single(dec.Entries);
        Assert.Equal(2025, dec.Entries[0].Date.Year);
    }

    // ── 필터 ─────────────────────────────────────────────────────────
    [Fact]
    public async Task 구분_필터는_목록만_줄이고_이월금은_건드리지_않는다()
    {
        예산_이월금(1_000_000);
        거래("2026-03-05", expense: 100_000);
        거래("2026-03-10", income: 50_000);

        var onlyExpense = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 3, GlobalConstants.TransactionTypeExpense);

        Assert.Single(onlyExpense.Entries);
        Assert.Equal(1_000_000, onlyExpense.CarryOver);   // 이월금은 필터와 무관
        Assert.Equal(0, onlyExpense.TotalIncome);
    }

    [Fact]
    public async Task 부서를_지정하면_그_부서만_본다()
    {
        거래("2026-03-05", expense: 10_000, deptId: DeptId);
        거래("2026-03-06", expense: 99_000, deptId: 99);

        var mine = await _svc.GetLedgerViewAsync(DeptId, DeptId, canViewAll: true, Year, 0, 3, null);

        Assert.Single(mine.Entries);
        Assert.Equal(10_000, mine.TotalExpense);
    }

    [Fact]
    public async Task 부서_0은_전체_부서를_본다()
    {
        거래("2026-03-05", expense: 10_000, deptId: DeptId);
        거래("2026-03-06", expense: 99_000, deptId: 99);

        var all = await _svc.GetLedgerViewAsync(0, DeptId, canViewAll: true, Year, 0, 3, null);

        Assert.Equal(2, all.Entries.Count);
        Assert.Equal(109_000, all.TotalExpense);
    }
}
