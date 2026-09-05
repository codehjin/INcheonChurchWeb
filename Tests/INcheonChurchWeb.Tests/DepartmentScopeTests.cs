using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 부서 열람 범위를 고정한다.
///
/// 부서 인자 0 은 '전체 부서'라는 뜻이지만, 전체 조회 권한(CanViewAll)이 없는
/// 사용자에게는 허용되면 안 된다. 실제로 모바일 장부·대시보드가 권한 없는
/// 사용자에게 0 을 넘겨(주석은 "자기 부서로 고정"이라 적혀 있었다) 부서간 장부가
/// 섞였고, 인쇄 화면은 dept 가 URL 쿼리라 임의로 바꿀 수 있었다.
///
/// 그래서 판정을 화면이 아니라 서비스가 단독으로 한다. 아래 테스트는 호출부가
/// 무엇을 넘기든 서비스가 스스로 범위를 좁힌다는 것을 고정한다.
/// </summary>
public class DepartmentScopeTests : IDisposable
{
    private const int 내부서 = 3;
    private const int 남의부서 = 4;
    private const int Year = 2026;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public DepartmentScopeTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private void 거래(int deptId, decimal expense)
    {
        using var db = _db.CreateDbContext();
        db.Transactions.Add(new LedgerEntry
        {
            DepartmentId = deptId,
            Date = DateTime.Parse("2026-03-10"),
            FiscalYear = Year,
            Type = "지출",
            Category = "행사비",
            Expense = expense,
            Description = "테스트"
        });
        db.SaveChanges();
    }

    private void 예산(int deptId, string category, decimal amount)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = deptId, Year = Year, Type = "지출",
            Category = category, Amount = amount
        });
        db.SaveChanges();
    }

    // ── 장부 조회 ────────────────────────────────────────────────────
    [Fact]
    public async Task 권한이_없으면_0을_넘겨도_자기_부서만_본다()
    {
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        var view = await _svc.GetLedgerViewAsync(0, 내부서, canViewAll: false, Year, 0, 3, null);

        Assert.Single(view.Entries);
        Assert.Equal(10_000, view.TotalExpense);
        Assert.All(view.Entries, e => Assert.Equal(내부서, e.DepartmentId));
    }

    [Fact]
    public async Task 권한이_없으면_남의_부서를_지정해도_자기_부서만_본다()
    {
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        // URL 쿼리(?dept=4)를 손으로 바꾼 상황
        var view = await _svc.GetLedgerViewAsync(남의부서, 내부서, canViewAll: false, Year, 0, 3, null);

        Assert.Single(view.Entries);
        Assert.Equal(10_000, view.TotalExpense);
    }

    [Fact]
    public async Task 권한이_있으면_0으로_전체_부서를_본다()
    {
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        var view = await _svc.GetLedgerViewAsync(0, 내부서, canViewAll: true, Year, 0, 3, null);

        Assert.Equal(2, view.Entries.Count);
        Assert.Equal(109_000, view.TotalExpense);
    }

    [Fact]
    public async Task 소속도_권한도_없으면_아무것도_보이지_않는다()
    {
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        var view = await _svc.GetLedgerViewAsync(0, 0, canViewAll: false, Year, 0, 3, null);

        Assert.Empty(view.Entries);
        Assert.Equal(0, view.TotalExpense);
    }

    // ── 대시보드 ─────────────────────────────────────────────────────
    [Fact]
    public async Task 대시보드도_권한이_없으면_자기_부서만_집계한다()
    {
        예산(내부서, "행사비", 1_000_000);
        예산(남의부서, "행사비", 5_000_000);
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        var view = await _svc.GetDashboardAsync(0, 내부서, canViewAll: false, Year);

        Assert.Equal(1_000_000, view.TotalBudget);
        Assert.Equal(10_000, view.TotalExpense);
    }

    [Fact]
    public async Task 대시보드는_권한이_있으면_전체를_집계한다()
    {
        예산(내부서, "행사비", 1_000_000);
        예산(남의부서, "행사비", 5_000_000);
        거래(내부서, 10_000);
        거래(남의부서, 99_000);

        var view = await _svc.GetDashboardAsync(0, 내부서, canViewAll: true, Year);

        Assert.Equal(6_000_000, view.TotalBudget);
        Assert.Equal(109_000, view.TotalExpense);
    }

    // ── 쓰기(삭제·일괄수정) ──────────────────────────────────────────
    private int 거래Id(int deptId, decimal expense)
    {
        거래(deptId, expense);
        using var db = _db.CreateDbContext();
        return db.Transactions.OrderBy(t => t.Id).Last().Id;
    }

    private int 남은건수(int deptId)
    {
        using var db = _db.CreateDbContext();
        return db.Transactions.Count(t => t.DepartmentId == deptId);
    }

    [Fact]
    public async Task 권한이_없으면_남의_부서_단건은_삭제되지_않는다()
    {
        int 남의행 = 거래Id(남의부서, 99_000);

        bool deleted = await _svc.DeleteTransactionAsync(남의행, 내부서, canViewAll: false);

        Assert.False(deleted);
        Assert.Equal(1, 남은건수(남의부서));
    }

    [Fact]
    public async Task 선택_삭제는_자기_부서_행만_지운다()
    {
        int 내행 = 거래Id(내부서, 10_000);
        int 남의행 = 거래Id(남의부서, 99_000);

        int deleted = await _svc.DeleteTransactionsAsync(new[] { 내행, 남의행 }, 내부서, canViewAll: false);

        Assert.Equal(1, deleted);
        Assert.Equal(0, 남은건수(내부서));
        Assert.Equal(1, 남은건수(남의부서));
    }

    [Fact]
    public async Task 권한이_있으면_선택_삭제가_전부_적용된다()
    {
        int 내행 = 거래Id(내부서, 10_000);
        int 남의행 = 거래Id(남의부서, 99_000);

        int deleted = await _svc.DeleteTransactionsAsync(new[] { 내행, 남의행 }, 내부서, canViewAll: true);

        Assert.Equal(2, deleted);
    }

    [Fact]
    public async Task 일괄수정은_자기_부서_행만_고친다()
    {
        int 내행 = 거래Id(내부서, 10_000);
        int 남의행 = 거래Id(남의부서, 99_000);

        int updated = await _svc.BulkUpdateCategoryAsync(
            new[] { 내행, 남의행 }, "바뀐분류", null, 내부서, canViewAll: false);

        Assert.Equal(1, updated);

        using var db = _db.CreateDbContext();
        Assert.Equal("바뀐분류", db.Transactions.First(t => t.Id == 내행).Category);
        Assert.Equal("행사비", db.Transactions.First(t => t.Id == 남의행).Category);
    }

    [Fact]
    public async Task 일괄수정은_세부가_비면_기존_값을_그대로_둔다()
    {
        int 내행 = 거래Id(내부서, 10_000);
        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == 내행).SubCategory = "원래세부";
            db.SaveChanges();
        }

        await _svc.BulkUpdateCategoryAsync(new[] { 내행 }, "바뀐분류", "  ", 내부서, canViewAll: false);

        using var check = _db.CreateDbContext();
        var row = check.Transactions.First(t => t.Id == 내행);
        Assert.Equal("바뀐분류", row.Category);
        Assert.Equal("원래세부", row.SubCategory);
    }

    [Fact]
    public async Task 인라인_편집도_남의_부서_행은_건드리지_못한다()
    {
        int 남의행 = 거래Id(남의부서, 99_000);

        bool saved = await _svc.SaveLedgerEntryEditAsync(
            남의행, DateTime.Parse("2026-03-11"), "지출", "가로챈분류", null,
            "가로채기", null, 1, 내부서, canViewAll: false);

        Assert.False(saved);

        using var db = _db.CreateDbContext();
        var row = db.Transactions.First(t => t.Id == 남의행);
        Assert.Equal("행사비", row.Category);
        Assert.Equal(99_000, row.Expense);
    }

    [Fact]
    public async Task 권한이_없으면_부서를_옮길_수_없다()
    {
        int 내행 = 거래Id(내부서, 10_000);

        await _svc.SaveLedgerEntryEditAsync(
            내행, DateTime.Parse("2026-03-11"), "지출", "행사비", null,
            "테스트", null, 10_000, 내부서, canViewAll: false, departmentId: 남의부서);

        using var db = _db.CreateDbContext();
        Assert.Equal(내부서, db.Transactions.First(t => t.Id == 내행).DepartmentId);
    }

    // ── 분류 후보 ────────────────────────────────────────────────────
    [Fact]
    public async Task 분류_후보도_권한이_없으면_자기_부서_예산만_쓴다()
    {
        예산(내부서, "행사비", 100);
        예산(남의부서, "남의부서만의분류", 100);

        var maps = await _svc.GetCategoryMapsAsync(0, 내부서, canViewAll: false, Year);

        Assert.Contains("행사비", maps.ExpenseCategories);
        Assert.DoesNotContain("남의부서만의분류", maps.ExpenseCategories);
    }
}
