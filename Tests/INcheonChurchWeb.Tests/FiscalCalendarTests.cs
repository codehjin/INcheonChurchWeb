using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 회계 달력 규칙을 고정한다.
/// 이 시스템의 회계연도는 1~12월이 아니라 <b>11월 넷째 주 일요일</b>에 끝나고,
/// 분기 경계는 2·5·8·11월 넷째 주 일요일이다. (웹_상세_구조문서.md §3.2)
/// 여기 있는 값이 바뀌면 장부 잔액·이월금·대시보드 집계가 전부 흔들린다.
/// </summary>
public class FiscalCalendarTests
{
    // ── 넷째 주 일요일 ───────────────────────────────────────────────
    // 달력으로 직접 확인한 값. "첫 일요일 + 21일" 규칙이므로
    // 1일이 일요일인 달은 22일이 넷째 주 일요일이 된다.
    [Theory]
    [InlineData(2024, 11, 24)]
    [InlineData(2025, 11, 23)]
    [InlineData(2026, 11, 22)]
    [InlineData(2027, 11, 28)]
    public void 넷째주_일요일을_구한다(int year, int month, int expectedDay)
    {
        var actual = AccountingService.GetFourthSundayOfMonth(year, month);

        Assert.Equal(new DateTime(year, month, expectedDay), actual);
        Assert.Equal(DayOfWeek.Sunday, actual.DayOfWeek);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void 넷째주_일요일은_항상_22일에서_28일_사이다(int year)
    {
        for (int month = 1; month <= 12; month++)
        {
            var d = AccountingService.GetFourthSundayOfMonth(year, month);

            Assert.Equal(DayOfWeek.Sunday, d.DayOfWeek);
            Assert.InRange(d.Day, 22, 28);
            Assert.Equal(month, d.Month);
        }
    }

    [Fact]
    public void 회계연도_종료일은_11월_넷째주_일요일이다()
    {
        // DateHelper와 AccountingService가 같은 날을 가리켜야 한다
        for (int year = 2020; year <= 2030; year++)
        {
            Assert.Equal(
                AccountingService.GetFourthSundayOfMonth(year, 11),
                DateHelper.GetFiscalYearEndDate(year));
        }
    }

    // ── 회계연도 판정 ────────────────────────────────────────────────
    [Theory]
    [InlineData("2026-11-22", 2026)]  // 종료일 당일 = 아직 2026 회계연도
    [InlineData("2026-11-23", 2027)]  // 종료일 다음날 = 2027 회계연도 시작
    [InlineData("2026-12-01", 2027)]  // 12월은 다음 회계연도
    [InlineData("2027-01-15", 2027)]
    [InlineData("2027-11-28", 2027)]  // 2027 종료일 당일
    [InlineData("2027-11-29", 2028)]
    public void 회계연도를_판정한다(string date, int expected)
    {
        Assert.Equal(expected, DateHelper.CalculateFiscalYear(DateTime.Parse(date)));
    }

    // ── 기본 분기 범위 ───────────────────────────────────────────────
    [Fact]
    public void 분기는_넷째주_일요일_다음날에_시작해_다음_넷째주_일요일에_끝난다()
    {
        var q1 = AccountingService.GetDefaultQuarterRange(2026, 1);
        var q2 = AccountingService.GetDefaultQuarterRange(2026, 2);
        var q3 = AccountingService.GetDefaultQuarterRange(2026, 3);
        var q4 = AccountingService.GetDefaultQuarterRange(2026, 4);

        Assert.Equal(new DateTime(2025, 11, 24), q1.Start);  // 2025-11-23(넷째 주일) 다음날
        Assert.Equal(new DateTime(2026, 2, 22), q1.End);
        Assert.Equal(new DateTime(2026, 2, 23), q2.Start);
        Assert.Equal(new DateTime(2026, 5, 24), q2.End);
        Assert.Equal(new DateTime(2026, 5, 25), q3.Start);
        Assert.Equal(new DateTime(2026, 8, 23), q3.End);
        Assert.Equal(new DateTime(2026, 8, 24), q4.Start);
        Assert.Equal(new DateTime(2026, 11, 22), q4.End);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void 네_분기는_빈틈도_겹침도_없이_회계연도를_덮는다(int year)
    {
        var ranges = Enumerable.Range(1, 4)
            .Select(q => AccountingService.GetDefaultQuarterRange(year, q))
            .ToList();

        for (int i = 0; i < 3; i++)
        {
            // 앞 분기 종료일 다음날 = 뒤 분기 시작일
            Assert.Equal(ranges[i].End.AddDays(1), ranges[i + 1].Start);
        }

        var whole = AccountingService.GetDefaultQuarterRange(year, 0);
        Assert.Equal(ranges[0].Start, whole.Start);
        Assert.Equal(ranges[3].End, whole.End);
    }

    [Fact]
    public void 회계연도_전체_범위는_직전_회계연도_종료일_다음날부터다()
    {
        var whole = AccountingService.GetDefaultQuarterRange(2026, 0);

        Assert.Equal(DateHelper.GetFiscalYearEndDate(2025).AddDays(1), whole.Start);
        Assert.Equal(DateHelper.GetFiscalYearEndDate(2026), whole.End);
    }
}
