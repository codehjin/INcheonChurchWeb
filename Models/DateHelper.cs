namespace INcheonChurchWeb.Models
{
    /// <summary>
    /// 교육국 회계 달력의 <b>단일 기준</b>(부서별 재정의가 없을 때의 기본 규칙).
    ///
    /// 회계연도는 1~12월이 아니라 <b>11월 넷째 주 일요일</b>에 끝나고,
    /// 분기 경계는 2·5·8·11월 넷째 주 일요일이다. 경계 다음날(월요일)이 새 분기 시작.
    ///
    /// ⚠️ 부서가 분기 경계를 직접 설정한 경우에는
    ///    <c>AccountingService.GetQuarterDateRangeAsync</c>(DB 조회)를 써야 한다.
    ///    여기 있는 것은 그 설정이 없을 때 적용되는 기본 규칙이다.
    /// </summary>
    public static class DateHelper
    {
        /// <summary>특정 월의 넷째 주 일요일. (첫 일요일 + 21일)</summary>
        public static DateTime GetFourthSundayOfMonth(int year, int month)
        {
            DateTime first = new DateTime(year, month, 1);
            int daysToSunday = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(daysToSunday).AddDays(21);
        }

        /// <summary>회계연도 종료일 = 그 해 11월 넷째 주 일요일.</summary>
        public static DateTime GetFiscalYearEndDate(int year) => GetFourthSundayOfMonth(year, 11);

        /// <summary>
        /// 기본 분기 범위.
        ///   Q1: 전년 11월 넷째 주일 다음날 ~ 2월 넷째 주일
        ///   Q2: 2월 넷째 주일 다음날     ~ 5월 넷째 주일
        ///   Q3: 5월 넷째 주일 다음날     ~ 8월 넷째 주일
        ///   Q4: 8월 넷째 주일 다음날     ~ 11월 넷째 주일
        /// quarter가 1~4가 아니면 회계연도 전체를 돌려준다.
        /// </summary>
        public static (DateTime Start, DateTime End) GetDefaultQuarterRange(int year, int quarter)
        {
            DateTime prevNov = GetFourthSundayOfMonth(year - 1, 11);
            DateTime feb = GetFourthSundayOfMonth(year, 2);
            DateTime may = GetFourthSundayOfMonth(year, 5);
            DateTime aug = GetFourthSundayOfMonth(year, 8);
            DateTime nov = GetFourthSundayOfMonth(year, 11);

            return quarter switch
            {
                1 => (prevNov.AddDays(1), feb),
                2 => (feb.AddDays(1), may),
                3 => (may.AddDays(1), aug),
                4 => (aug.AddDays(1), nov),
                _ => (prevNov.AddDays(1), nov)   // 회계연도 전체
            };
        }

        /// <summary>날짜가 속한 회계연도.</summary>
        public static int CalculateFiscalYear(DateTime date)
        {
            int calendarYear = date.Year;

            if (date.Date > GetFiscalYearEndDate(calendarYear).Date)
            {
                return calendarYear + 1;
            }

            return date.Date > GetFiscalYearEndDate(calendarYear - 1).Date
                ? calendarYear
                : calendarYear - 1;
        }

        /// <summary>
        /// 날짜가 속한 분기(1~4).
        ///
        /// ⚠️ 예전에는 달(月) 번호로 판정했는데(3~5월=Q2 …), 그러면
        ///    화면이 쓰는 <see cref="GetDefaultQuarterRange"/>와 분기 경계마다 며칠씩 어긋났다.
        ///    (2026년 기준 연 21일) 지금은 같은 범위에서 역산하므로 두 값이 항상 일치한다.
        /// </summary>
        public static int GetQuarter(DateTime date)
        {
            int fiscalYear = CalculateFiscalYear(date);

            for (int quarter = 1; quarter <= 3; quarter++)
            {
                var (start, end) = GetDefaultQuarterRange(fiscalYear, quarter);
                if (date.Date >= start.Date && date.Date <= end.Date) return quarter;
            }

            return 4;
        }
    }
}
