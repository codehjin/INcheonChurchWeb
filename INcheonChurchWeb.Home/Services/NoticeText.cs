using Church.Home.Data;

namespace INcheonChurchWeb.Home.Services
{
    /// <summary>
    /// 통신문을 화면에 그릴 때 쓰는 글자 처리. HTML 을 만들지 않는다 —
    /// 조각만 나눠 주고, 화면(Razor)이 글자로 찍는다. 본문에 태그가 섞여 있어도 글자로 보인다.
    /// </summary>
    public static class NoticeText
    {
        public sealed record Segment(string Text, bool Highlight);

        /// <summary>
        /// 「」로 감싼 말을 강조 조각으로 나눈다 (괄호는 그대로 둔다 — 종이 통신문과 같게).
        /// 닫히지 않은 「 는 그냥 글자로 둔다.
        /// </summary>
        public static List<Segment> Highlight(string? text)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(text)) return result;

            int pos = 0;
            while (pos < text.Length)
            {
                int open = text.IndexOf('「', pos);
                int close = open < 0 ? -1 : text.IndexOf('」', open + 1);
                if (open < 0 || close < 0)
                {
                    result.Add(new Segment(text[pos..], false));
                    break;
                }

                if (open > pos) result.Add(new Segment(text[pos..open], false));
                result.Add(new Segment(text[open..(close + 1)], true));
                pos = close + 1;
            }
            return result;
        }

        /// <summary>목록 카드에 한 줄 — 「」가 든 줄(이달의 안내)이 있으면 그 줄, 없으면 첫 줄.</summary>
        public static string Preview(string? greeting, int max = 70)
        {
            if (string.IsNullOrWhiteSpace(greeting)) return "";
            var lines = greeting.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var line = lines.FirstOrDefault(l => l.Contains('「')) ?? lines.FirstOrDefault() ?? "";
            return line.Length <= max ? line : line[..max] + "…";
        }

        /// <summary>일정표에서 실제로 쓴 열. 모든 줄이 빈 열은 그리지 않는다 (부서마다 쓰는 열이 다르다).</summary>
        public sealed record ScheduleColumns(bool Scripture, bool SermonTitle, bool Activity, bool Offering, bool Prayer);

        public static ScheduleColumns ColumnsOf(IReadOnlyCollection<NoticeWeek> weeks) => new(
            weeks.Any(w => !string.IsNullOrWhiteSpace(w.Scripture)),
            weeks.Any(w => !string.IsNullOrWhiteSpace(w.SermonTitle)),
            weeks.Any(w => !string.IsNullOrWhiteSpace(w.Activity)),
            weeks.Any(w => !string.IsNullOrWhiteSpace(w.Offering)),
            weeks.Any(w => !string.IsNullOrWhiteSpace(w.Prayer)));

        public static string Md(DateTime d) => $"{d.Month}/{d.Day}";

        /// <summary>전화 걸기 링크. 숫자·+·- 만 남긴다.</summary>
        public static string? TelHref(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(c => char.IsAsciiDigit(c) || c == '+' || c == '-').ToArray());
            return digits.Any(char.IsAsciiDigit) ? "tel:" + digits : null;
        }
    }
}
