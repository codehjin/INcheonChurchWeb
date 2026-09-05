using System.Text.RegularExpressions;

namespace INcheonChurchWeb.Models
{
    public enum SearchTokenKind
    {
        Text,       // 행사명·본문 부분일치
        Date,       // 2026-07-28
        YearMonth,  // 2026-07
        Year,       // 2026
        MonthDay,   // 07.28 → 모든 연도의 7월 28일
        Quarter     // 3분기 / Q3
    }

    public sealed class SearchToken
    {
        public SearchTokenKind Kind { get; set; } = SearchTokenKind.Text;
        public string Text { get; set; } = "";
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Quarter { get; set; }
    }

    // 연간계획·주간회의록 검색창 한 칸에 들어온 입력을 토큰으로 해석한다.
    // 공백으로 쪼갠 뒤 토큰마다 형태를 보고 날짜/분기/텍스트로 나누며, 해석에 실패하면 전부 텍스트로 떨어뜨린다.
    public static class BoardSearch
    {
        private static readonly Regex RxDate = new(@"^(\d{4})[-./](\d{1,2})[-./](\d{1,2})$", RegexOptions.Compiled);
        private static readonly Regex RxYearMonth = new(@"^(\d{4})[-./](\d{1,2})$", RegexOptions.Compiled);
        private static readonly Regex RxMonthDay = new(@"^(\d{1,2})[-./](\d{1,2})$", RegexOptions.Compiled);
        private static readonly Regex RxYear = new(@"^(\d{4})$", RegexOptions.Compiled);
        private static readonly Regex RxQuarter = new(@"^(?:([1-4])분기|[Qq]([1-4]))$", RegexOptions.Compiled);

        public static List<SearchToken> Parse(string? raw)
        {
            var result = new List<SearchToken>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
                result.Add(ParseOne(part));

            return result;
        }

        private static SearchToken ParseOne(string s)
        {
            var m = RxQuarter.Match(s);
            if (m.Success)
            {
                var q = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return new SearchToken { Kind = SearchTokenKind.Quarter, Quarter = int.Parse(q), Text = s };
            }

            m = RxDate.Match(s);
            if (m.Success)
            {
                int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
                if (IsYear(y) && IsMonth(mo) && IsDay(d))
                    return new SearchToken { Kind = SearchTokenKind.Date, Year = y, Month = mo, Day = d, Text = s };
            }

            m = RxYearMonth.Match(s);
            if (m.Success)
            {
                int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
                if (IsYear(y) && IsMonth(mo))
                    return new SearchToken { Kind = SearchTokenKind.YearMonth, Year = y, Month = mo, Text = s };
            }

            m = RxYear.Match(s);
            if (m.Success)
            {
                int y = int.Parse(m.Groups[1].Value);
                if (IsYear(y))
                    return new SearchToken { Kind = SearchTokenKind.Year, Year = y, Text = s };
            }

            m = RxMonthDay.Match(s);
            if (m.Success)
            {
                int mo = int.Parse(m.Groups[1].Value), d = int.Parse(m.Groups[2].Value);
                if (IsMonth(mo) && IsDay(d))
                    return new SearchToken { Kind = SearchTokenKind.MonthDay, Month = mo, Day = d, Text = s };
            }

            return new SearchToken { Kind = SearchTokenKind.Text, Text = s };
        }

        private static bool IsYear(int y) => y >= 1900 && y <= 2200;
        private static bool IsMonth(int m) => m >= 1 && m <= 12;
        private static bool IsDay(int d) => d >= 1 && d <= 31;

        // 텍스트 토큰이 주어진 필드 중 하나에라도 부분일치하는지 (대소문자 무시, null 안전).
        public static bool MatchesText(SearchToken t, params string?[] fields)
        {
            if (string.IsNullOrEmpty(t.Text)) return true;
            foreach (var f in fields)
            {
                if (!string.IsNullOrEmpty(f) && f.Contains(t.Text, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // 날짜 계열 토큰 매칭. endDate가 있으면 start~end 기간에 걸치는지 본다.
        public static bool MatchesDate(SearchToken t, DateTime start, DateTime? endDate = null)
        {
            var from = start.Date;
            var to = (endDate?.Date ?? from);
            if (to < from) to = from;

            switch (t.Kind)
            {
                case SearchTokenKind.Date:
                    var target = SafeDate(t.Year, t.Month, t.Day);
                    return target.HasValue && from <= target.Value && target.Value <= to;

                case SearchTokenKind.YearMonth:
                    return EachDay(from, to).Any(d => d.Year == t.Year && d.Month == t.Month);

                case SearchTokenKind.Year:
                    return EachDay(from, to).Any(d => d.Year == t.Year);

                case SearchTokenKind.MonthDay:
                    return EachDay(from, to).Any(d => d.Month == t.Month && d.Day == t.Day);

                default:
                    return false;
            }
        }

        // 행사 기간은 길어야 며칠이므로 하루씩 훑어도 비용이 없다. 비정상적으로 긴 기간은 안전하게 잘라낸다.
        private static IEnumerable<DateTime> EachDay(DateTime from, DateTime to)
        {
            var limit = from.AddDays(400);
            if (to > limit) to = limit;
            for (var d = from; d <= to; d = d.AddDays(1))
                yield return d;
        }

        private static DateTime? SafeDate(int y, int m, int d)
        {
            if (d > DateTime.DaysInMonth(y, m)) return null;
            return new DateTime(y, m, d);
        }
    }
}
