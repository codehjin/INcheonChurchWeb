using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 회계 달력이 <b>한 벌</b>이라는 것을 지킨다.
///
/// 예전에는 분기 판정이 두 벌이었다.
///   · DateHelper.GetQuarter        — 달(月) 번호로 판정 (임포트가 저장할 때 사용)
///   · GetDefaultQuarterRange       — 넷째 주 일요일이 경계 (화면이 조회할 때 사용)
/// 그래서 분기 경계마다 5~8일씩 어긋났고(2025~2027 합계 56일),
/// 은행 엑셀로 넣은 거래의 저장된 Quarter 값과 화면 조회 범위가 갈렸다.
///
/// 지금은 DateHelper가 유일한 기준이고 서비스는 위임만 한다.
/// 이 테스트가 그 통합을 지킨다 — 어느 한쪽만 손대면 즉시 실패한다.
/// </summary>
public class FiscalCalendarConsistencyTests
{
    [Theory]
    [InlineData(2024)]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    [InlineData(2028)]
    public void 분기_판정과_분기_범위가_모든_날짜에서_일치한다(int year)
    {
        var mismatches = new List<string>();

        for (int quarter = 1; quarter <= 4; quarter++)
        {
            var (start, end) = DateHelper.GetDefaultQuarterRange(year, quarter);

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                int byHelper = DateHelper.GetQuarter(d);
                if (byHelper != quarter)
                    mismatches.Add($"{d:yyyy-MM-dd} 범위 Q{quarter} ≠ 판정 Q{byHelper}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void 회계연도_판정과_회계연도_범위가_일치한다(int year)
    {
        var (start, end) = DateHelper.GetDefaultQuarterRange(year, 0);

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            Assert.Equal(year, DateHelper.CalculateFiscalYear(d));
        }

        // 범위 바깥은 다른 회계연도여야 한다
        Assert.Equal(year - 1, DateHelper.CalculateFiscalYear(start.AddDays(-1)));
        Assert.Equal(year + 1, DateHelper.CalculateFiscalYear(end.AddDays(1)));
    }

    [Fact]
    public void 서비스의_static_헬퍼는_DateHelper에_위임한다()
    {
        for (int year = 2024; year <= 2028; year++)
        {
            Assert.Equal(DateHelper.GetFiscalYearEndDate(year),
                         AccountingService.GetFourthSundayOfNovember(year));

            for (int month = 1; month <= 12; month++)
            {
                Assert.Equal(DateHelper.GetFourthSundayOfMonth(year, month),
                             AccountingService.GetFourthSundayOfMonth(year, month));
            }

            for (int quarter = 0; quarter <= 4; quarter++)
            {
                Assert.Equal(DateHelper.GetDefaultQuarterRange(year, quarter),
                             AccountingService.GetDefaultQuarterRange(year, quarter));
            }
        }
    }

    [Theory]
    // 통합 전에는 어긋나던 날짜들. 이제 두 값이 같아야 한다.
    [InlineData("2026-02-23", 2)]
    [InlineData("2026-02-28", 2)]
    [InlineData("2026-05-25", 3)]
    [InlineData("2026-05-31", 3)]
    [InlineData("2026-08-24", 4)]
    [InlineData("2026-08-31", 4)]
    [InlineData("2025-02-24", 2)]
    [InlineData("2025-08-25", 4)]
    public void 예전에_어긋나던_경계일이_이제_맞는다(string date, int expectedQuarter)
    {
        var d = DateTime.Parse(date);

        Assert.Equal(expectedQuarter, DateHelper.GetQuarter(d));

        var (start, end) = DateHelper.GetDefaultQuarterRange(DateHelper.CalculateFiscalYear(d), expectedQuarter);
        Assert.InRange(d, start, end);
    }
}
