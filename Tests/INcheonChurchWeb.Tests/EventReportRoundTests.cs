using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 행사보고서 회차를 고정한다.
///
/// 반데이트·달란트처럼 한 해에 두 번 하는 행사가 있다. (부서 · 회계연도 · 행사명 · 회차)가
/// 보고서 한 건을 가리키며, 회차를 추가해도 앞 회차를 덮어쓰지 않아야 한다.
/// 기존 보고서는 모두 1차로 본다.
/// </summary>
public class EventReportRoundTests : IDisposable
{
    private const int DeptId = 3;
    private const int Year = 2026;
    private const string 반데이트 = "반데이트";

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public EventReportRoundTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private async Task<int> 보고서(int round, string? roundTitle = null, string? location = null, string eventName = 반데이트)
        => await _svc.SaveEventReportAsync(new EventReport
        {
            DepartmentId = DeptId,
            FiscalYear = Year,
            EventName = eventName,
            Round = round,
            RoundTitle = roundTitle,
            Location = location,
            StartDate = DateTime.Parse("2026-03-10")
        }, "tester");

    [Fact]
    public async Task 보고서를_새로_쓰면_1차가_된다()
    {
        int next = await _svc.GetNextEventReportRoundAsync(DeptId, Year, 반데이트);

        Assert.Equal(1, next);
    }

    [Fact]
    public async Task 첫_회차를_쓰면_다음은_2차다()
    {
        await 보고서(1);

        int next = await _svc.GetNextEventReportRoundAsync(DeptId, Year, 반데이트);

        Assert.Equal(2, next);
    }

    [Fact]
    public async Task 둘째_회차를_써도_앞_회차가_덮어쓰이지_않는다()
    {
        await 보고서(1, location: "본당");
        await 보고서(2, location: "야외");

        var rounds = await _svc.GetEventReportRoundsAsync(DeptId, Year, 반데이트);

        Assert.Equal(2, rounds.Count);
        Assert.Equal("본당", rounds[0].Location);
        Assert.Equal("야외", rounds[1].Location);
    }

    [Fact]
    public async Task 회차는_번호순으로_돌아온다()
    {
        await 보고서(2);
        await 보고서(1);
        await 보고서(3);

        var rounds = await _svc.GetEventReportRoundsAsync(DeptId, Year, 반데이트);

        Assert.Equal(new[] { 1, 2, 3 }, rounds.Select(r => r.Round).ToArray());
    }

    [Fact]
    public async Task 회차를_지정해_한_건만_가져온다()
    {
        await 보고서(1, location: "본당");
        await 보고서(2, location: "야외");

        var second = await _svc.GetEventReportByNameAsync(DeptId, Year, 반데이트, round: 2);

        Assert.NotNull(second);
        Assert.Equal("야외", second!.Location);
    }

    [Fact]
    public async Task 회차를_안_주면_1차를_가져온다()
    {
        await 보고서(1, location: "본당");
        await 보고서(2, location: "야외");

        var first = await _svc.GetEventReportByNameAsync(DeptId, Year, 반데이트);

        Assert.NotNull(first);
        Assert.Equal("본당", first!.Location);
    }

    [Fact]
    public async Task 회차_제목을_적으면_저장되고_수정된다()
    {
        int id = await 보고서(1, roundTitle: "상반기");

        var saved = await _svc.GetEventReportAsync(id);
        Assert.Equal("상반기", saved!.RoundTitle);

        saved.RoundTitle = "1학기";
        await _svc.SaveEventReportAsync(saved, "tester");

        var again = await _svc.GetEventReportAsync(id);
        Assert.Equal("1학기", again!.RoundTitle);
    }

    [Fact]
    public async Task 다른_행사의_회차는_섞이지_않는다()
    {
        await 보고서(1, eventName: 반데이트);
        await 보고서(1, eventName: "달란트");
        await 보고서(2, eventName: "달란트");

        Assert.Single(await _svc.GetEventReportRoundsAsync(DeptId, Year, 반데이트));
        Assert.Equal(2, (await _svc.GetEventReportRoundsAsync(DeptId, Year, "달란트")).Count);
        Assert.Equal(2, await _svc.GetNextEventReportRoundAsync(DeptId, Year, 반데이트));
    }

    [Fact]
    public async Task 연도가_다르면_회차가_다시_1부터_시작한다()
    {
        await 보고서(1);
        await 보고서(2);

        int nextNextYear = await _svc.GetNextEventReportRoundAsync(DeptId, Year + 1, 반데이트);

        Assert.Equal(1, nextNextYear);
    }

    [Fact]
    public async Task 삭제한_회차는_목록에서_빠진다()
    {
        await 보고서(1);
        int second = await 보고서(2);

        await _svc.DeleteEventReportAsync(second, "tester");

        var rounds = await _svc.GetEventReportRoundsAsync(DeptId, Year, 반데이트);
        Assert.Single(rounds);
        Assert.Equal(1, rounds[0].Round);
    }
}
