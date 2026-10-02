using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 행사 재정이 회차별로 갈리는 방식, 그리고 행사 목록의 제외·대분류를 고정한다.
///
///  · 지출은 각 회차의 행사 기간 안에 있는 거래만 센다.
///  · 기간을 정하기 전에는 어느 회차 몫인지 가를 수 없으므로 집계하지 않는다(null).
///  · 회차를 더하면 편성예산은 앞 회차 값을 그대로 복사한다.
///  · 운영비·환수금처럼 행사가 아닌 분류는 목록에서 뺄 수 있다.
///  · 여름성경학교·겨울성경학교처럼 묶어 볼 분류는 대분류로 함께 묶는다.
/// </summary>
public class EventFinanceRoundTests : IDisposable
{
    private const int DeptId = 3;
    private const int Year = 2026;
    private const string 반데이트 = "반데이트";

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public EventFinanceRoundTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private void 예산(string category, decimal amount)
    {
        using var db = _db.CreateDbContext();
        db.BudgetPlans.Add(new BudgetPlan
        {
            DepartmentId = DeptId, Year = Year, Type = "지출",
            Category = category, Amount = amount
        });
        db.SaveChanges();
    }

    private void 지출(string category, string date, decimal amount)
    {
        using var db = _db.CreateDbContext();
        db.Transactions.Add(new LedgerEntry
        {
            DepartmentId = DeptId, FiscalYear = Year, Type = "지출",
            Category = category, Date = DateTime.Parse(date),
            Expense = amount, Description = "테스트"
        });
        db.SaveChanges();
    }

    private async Task<int> 보고서(int round, string? start, string? end = null,
                                   decimal? budget = null, string eventName = 반데이트)
        => await _svc.SaveEventReportAsync(new EventReport
        {
            DepartmentId = DeptId,
            FiscalYear = Year,
            EventName = eventName,
            Round = round,
            Budget = budget,
            StartDate = start == null ? null : DateTime.Parse(start),
            EndDate = end == null ? null : DateTime.Parse(end)
        }, "tester");

    private async Task<AccountingService.EventFinance> 재정(string name = 반데이트)
    {
        var all = await _svc.GetEventFinancesAsync(DeptId, Year, includeExcluded: true);
        return all.First(f => f.EventName == name);
    }

    // ── 기간으로 가르기 ──────────────────────────────────────────
    [Fact]
    public async Task 지출을_회차_기간으로_갈라_센다()
    {
        예산(반데이트, 1_000_000);
        지출(반데이트, "2026-03-10", 300_000);   // 1차 기간
        지출(반데이트, "2026-03-11", 200_000);   // 1차 기간
        지출(반데이트, "2026-09-05", 400_000);   // 2차 기간

        await 보고서(1, "2026-03-09", "2026-03-12");
        await 보고서(2, "2026-09-01", "2026-09-07");

        var fin = await 재정();

        Assert.Equal(500_000, fin.Rounds[0].Spent);
        Assert.Equal(400_000, fin.Rounds[1].Spent);
        Assert.Equal(900_000, fin.Spent);     // 연간 합계는 그대로
    }

    [Fact]
    public async Task 기간을_정하기_전에는_지출을_집계하지_않는다()
    {
        예산(반데이트, 1_000_000);
        지출(반데이트, "2026-03-10", 300_000);

        await 보고서(1, start: null);

        var fin = await 재정();

        Assert.False(fin.Rounds[0].HasPeriod);
        Assert.Null(fin.Rounds[0].Spent);
    }

    [Fact]
    public async Task 종료일이_없으면_시작일_하루만_센다()
    {
        예산(반데이트, 1_000_000);
        지출(반데이트, "2026-03-10", 300_000);
        지출(반데이트, "2026-03-11", 200_000);

        await 보고서(1, "2026-03-10");

        var fin = await 재정();

        Assert.Equal(300_000, fin.Rounds[0].Spent);
    }

    [Fact]
    public async Task 기간_밖의_지출은_어느_회차에도_들어가지_않는다()
    {
        예산(반데이트, 1_000_000);
        지출(반데이트, "2026-06-15", 777_000);   // 두 회차 사이

        await 보고서(1, "2026-03-09", "2026-03-12");
        await 보고서(2, "2026-09-01", "2026-09-07");

        var fin = await 재정();

        Assert.Equal(0, fin.Rounds[0].Spent);
        Assert.Equal(0, fin.Rounds[1].Spent);
        Assert.Equal(777_000, fin.Spent);     // 연간 합계에는 남는다
    }

    // ── 예산 ────────────────────────────────────────────────────
    [Fact]
    public async Task 회차_예산을_안_적으면_분류의_연간_예산을_쓴다()
    {
        예산(반데이트, 1_000_000);
        await 보고서(1, "2026-03-09", "2026-03-12", budget: null);

        var fin = await 재정();

        Assert.Equal(1_000_000, fin.Rounds[0].Budget);
    }

    [Fact]
    public async Task 회차를_더하면_앞_회차_예산이_복사된다()
    {
        예산(반데이트, 1_000_000);
        await 보고서(1, "2026-03-09", "2026-03-12", budget: 600_000);

        var draft = await _svc.CreateNextRoundDraftAsync(DeptId, Year, 반데이트);

        Assert.Equal(2, draft.Round);
        Assert.Equal(600_000, draft.Budget);
        Assert.Null(draft.StartDate);     // 기간은 비워 둔다
    }

    [Fact]
    public async Task 첫_회차_초안은_분류의_연간_예산을_가져온다()
    {
        예산(반데이트, 1_000_000);

        var draft = await _svc.CreateNextRoundDraftAsync(DeptId, Year, 반데이트);

        Assert.Equal(1, draft.Round);
        Assert.Equal(1_000_000, draft.Budget);
    }

    [Fact]
    public async Task 회차_예산을_넘기면_초과로_표시된다()
    {
        예산(반데이트, 1_000_000);
        지출(반데이트, "2026-03-10", 700_000);

        await 보고서(1, "2026-03-09", "2026-03-12", budget: 500_000);

        var fin = await 재정();

        Assert.True(fin.Rounds[0].IsOver);
    }

    // ── 행사 아님 제외 ───────────────────────────────────────────
    [Fact]
    public async Task 행사가_아닌_분류는_목록에서_뺀다()
    {
        예산("운영비", 500_000);
        예산(반데이트, 1_000_000);

        await _svc.SaveEventCategorySettingAsync(DeptId, "운영비", null, isExcluded: true);

        var list = await _svc.GetEventFinancesAsync(DeptId, Year);

        Assert.DoesNotContain(list, f => f.EventName == "운영비");
        Assert.Contains(list, f => f.EventName == 반데이트);
    }

    [Fact]
    public async Task 뺀_분류도_원하면_함께_가져올_수_있다()
    {
        예산("환수금", 100_000);
        await _svc.SaveEventCategorySettingAsync(DeptId, "환수금", null, isExcluded: true);

        var list = await _svc.GetEventFinancesAsync(DeptId, Year, includeExcluded: true);

        Assert.Contains(list, f => f.EventName == "환수금");
    }

    [Fact]
    public async Task 뺀_분류를_되돌리면_다시_나온다()
    {
        예산("운영비", 500_000);
        await _svc.SaveEventCategorySettingAsync(DeptId, "운영비", null, isExcluded: true);
        await _svc.SaveEventCategorySettingAsync(DeptId, "운영비", null, isExcluded: false);

        var list = await _svc.GetEventFinancesAsync(DeptId, Year);

        Assert.Contains(list, f => f.EventName == "운영비");
    }

    [Fact]
    public async Task 제외는_부서별로_따로_적용된다()
    {
        예산("운영비", 500_000);
        await _svc.SaveEventCategorySettingAsync(DeptId, "운영비", null, isExcluded: true);

        using (var db = _db.CreateDbContext())
        {
            db.BudgetPlans.Add(new BudgetPlan
            {
                DepartmentId = 4, Year = Year, Type = "지출", Category = "운영비", Amount = 500_000
            });
            db.SaveChanges();
        }

        var other = await _svc.GetEventFinancesAsync(4, Year);

        Assert.Contains(other, f => f.EventName == "운영비");
    }

    // ── 대분류 ──────────────────────────────────────────────────
    [Fact]
    public async Task 대분류로_묶으면_같은_이름이_붙는다()
    {
        예산("여름성경학교", 1_000_000);
        예산("겨울성경학교", 800_000);

        await _svc.SaveEventCategorySettingAsync(DeptId, "여름성경학교", "성경학교", isExcluded: false);
        await _svc.SaveEventCategorySettingAsync(DeptId, "겨울성경학교", "성경학교", isExcluded: false);

        var list = await _svc.GetEventFinancesAsync(DeptId, Year);

        Assert.Equal("성경학교", list.First(f => f.EventName == "여름성경학교").GroupName);
        Assert.Equal("성경학교", list.First(f => f.EventName == "겨울성경학교").GroupName);
    }

    [Fact]
    public async Task 대분류를_비우면_묶임이_풀린다()
    {
        예산("여름성경학교", 1_000_000);
        await _svc.SaveEventCategorySettingAsync(DeptId, "여름성경학교", "성경학교", isExcluded: false);
        await _svc.SaveEventCategorySettingAsync(DeptId, "여름성경학교", "", isExcluded: false);

        var list = await _svc.GetEventFinancesAsync(DeptId, Year);

        Assert.Null(list.First(f => f.EventName == "여름성경학교").GroupName);
    }

    [Fact]
    public async Task 설정이_기본값이면_행을_남기지_않는다()
    {
        예산("여름성경학교", 1_000_000);

        await _svc.SaveEventCategorySettingAsync(DeptId, "여름성경학교", null, isExcluded: false);

        Assert.Empty(await _svc.GetEventCategorySettingsAsync(DeptId));
    }

    [Fact]
    public async Task 대분류와_제외를_함께_둘_수_있다()
    {
        예산("운영비", 500_000);
        await _svc.SaveEventCategorySettingAsync(DeptId, "운영비", "관리", isExcluded: true);

        var settings = await _svc.GetEventCategorySettingsAsync(DeptId);
        var row = Assert.Single(settings);

        Assert.Equal("관리", row.GroupName);
        Assert.True(row.IsExcluded);
    }
}
