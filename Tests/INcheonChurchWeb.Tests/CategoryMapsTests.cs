using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 장부 입력 자동완성(분류항목 → 세부) 규칙을 고정한다. (웹_상세_구조문서.md §6.4)
/// 2축 원칙: 지출은 분류=행사/세부=비용, 수입은 분류=회비수입 등/세부=행사명.
/// </summary>
public class CategoryMapsTests : IDisposable
{
    private const int DeptId = 3;
    private const int OtherDept = 99;
    private const int Year = 2026;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public CategoryMapsTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private void 예산(string type, string category, string? sub = null, int deptId = DeptId)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = deptId, Year = Year, Type = type,
            Category = category, SubCategory = sub, Amount = 100
        });
        db.SaveChanges();
    }

    private void 거래(string category, string sub, decimal income = 0, decimal expense = 0,
                     string? type = null, int deptId = DeptId)
    {
        using var db = _db.CreateDbContext();
        db.Transactions.Add(new LedgerEntry
        {
            DepartmentId = deptId, Date = DateTime.Parse("2026-03-10"), FiscalYear = Year,
            Type = type ?? (income > 0 ? "수입" : "지출"),
            Category = category, SubCategory = sub, Income = income, Expense = expense,
            Description = "테스트"
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task 예산에서_분류와_세부_후보를_만든다()
    {
        예산("지출", "여름성경학교", "식사");
        예산("지출", "여름성경학교", "공과교재");
        예산("수입", "회비수입");

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(new[] { "여름성경학교" }, maps.ExpenseCategories);
        Assert.Equal(new[] { "회비수입" }, maps.IncomeCategories);
        Assert.Equal(new[] { "공과교재", "식사" }, maps.SubsFor("지출", "여름성경학교"));
    }

    [Fact]
    public async Task 수입의_세부는_행사명_목록이다()
    {
        // 2축 원칙 — 수입은 "어느 행사의 회비인가"를 세부에 적는다
        예산("지출", "여름성경학교");
        예산("지출", "겨울성경학교");
        예산("수입", "회비수입");

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(new[] { "겨울성경학교", "여름성경학교" }, maps.SubsFor("수입", "회비수입").OrderBy(x => x));
    }

    [Fact]
    public async Task 예산에_없어도_장부에_쓰인_값은_후보에_들어간다()
    {
        거래("훈련비", "교사훈련(MT)", expense: 500_000);

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Contains("훈련비", maps.ExpenseCategories);
        Assert.Contains("교사훈련(MT)", maps.SubsFor("지출", "훈련비"));
    }

    [Fact]
    public async Task 구분은_Type_문자열보다_금액이_든_컬럼을_먼저_믿는다()
    {
        // 레거시 데이터에 Type이 비어 있어도 수입으로 잡혀야 한다
        거래("회비수입", "여름성경학교", income: 300_000, type: "");

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Contains("회비수입", maps.IncomeCategories);
        Assert.DoesNotContain("회비수입", maps.ExpenseCategories);
    }

    [Fact]
    public async Task 영문_Type도_수입으로_본다()
    {
        // 금액이 0인 이상 데이터라도 Type이 Income이면 수입으로
        거래("찬조금", "달란트행사", income: 0, expense: 0, type: "Income");

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Contains("찬조금", maps.IncomeCategories);
    }

    [Fact]
    public async Task 부서를_지정하면_그_부서_것만_본다()
    {
        예산("지출", "여름성경학교", "식사", deptId: DeptId);
        예산("지출", "타부서행사", "식사", deptId: OtherDept);

        var mine = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(new[] { "여름성경학교" }, mine.ExpenseCategories);
    }

    [Fact]
    public async Task 부서_0은_전체_부서를_본다()
    {
        // 월별 장부에서 "전체 부서"로 볼 때 쓰는 경로
        예산("지출", "여름성경학교", "식사", deptId: DeptId);
        예산("지출", "타부서행사", "식사", deptId: OtherDept);

        var all = await _svc.GetCategoryMapsAsync(0, DeptId, canViewAll: true, Year);

        Assert.Contains("여름성경학교", all.ExpenseCategories);
        Assert.Contains("타부서행사", all.ExpenseCategories);
    }

    [Fact]
    public async Task 분류를_고르지_않으면_전체_세부를_제안한다()
    {
        예산("지출", "여름성경학교", "식사");
        예산("지출", "교사회의비", "교사간식");

        var maps = await _svc.GetCategoryMapsAsync(DeptId, DeptId, canViewAll: true, Year);

        Assert.Equal(new[] { "교사간식", "식사" }, maps.SubsFor("지출", null));
    }
}
