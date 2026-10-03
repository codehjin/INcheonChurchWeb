using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Church.Home.Data
{
    /// <summary>
    /// 가정통신문 본문(인사말 · 안내)의 서식.
    ///
    /// 재정앱 편집기(Quill)가 만든 HTML 이 학부모 화면에 그대로 나가므로, 허용한 서식만 남긴다.
    ///   - 태그: 문단 · 줄바꿈 · 굵게/기울임/밑줄/취소선 · span · 말머리 목록(ul/ol/li)
    ///   - 속성: class 하나뿐. 그것도 편집기 툴바가 만드는 이름(<see cref="AllowedClasses"/>)만
    ///   - 링크 · 이미지 · 스크립트 · style 속성 · 이벤트 속성은 하나도 남지 않는다
    /// 재정앱은 저장할 때, 학부모앱은 그릴 때 — 양쪽에서 이 규칙 하나로 거른다.
    ///
    /// 서식 없이 저장된 글(예전 글 · 시험 데이터)은 그대로 글자로 다룬다. <see cref="IsHtml"/> 로 가른다.
    /// </summary>
    public static class RichText
    {
        /// <summary>한 칸에 담을 수 있는 최대 길이 (서식 포함)</summary>
        public const int MaxLength = 20_000;

        /// <summary>편집기 툴바가 만드는 class — 글꼴 · 크기 · 글자색 · 배경색 · 정렬 · 들여쓰기</summary>
        public static readonly IReadOnlyList<string> AllowedClasses = new[]
        {
            "ql-font-myeongjo", "ql-font-jua", "ql-font-pen",
            "ql-size-small", "ql-size-large", "ql-size-huge",
            "ql-color-red", "ql-color-blue", "ql-color-green", "ql-color-orange", "ql-color-purple", "ql-color-gray",
            "ql-bg-yellow", "ql-bg-pink", "ql-bg-lightblue", "ql-bg-lightgreen",
            "ql-align-center", "ql-align-right", "ql-align-justify",
            "ql-indent-1", "ql-indent-2", "ql-indent-3", "ql-indent-4",
        };

        private static readonly string[] AllowedTags = { "p", "br", "strong", "b", "em", "i", "u", "s", "span", "ul", "ol", "li" };

        // 걸러낼 때 알맹이째 버릴 태그 (껍데기만 벗기면 스크립트 내용이 글자로 남는다)
        private static readonly string[] DropWithContent = { "script", "style", "template", "iframe", "object", "embed", "noscript", "svg", "math", "title", "textarea", "select" };

        private static readonly HtmlSanitizer Sanitizer = BuildSanitizer();

        private static HtmlSanitizer BuildSanitizer()
        {
            var s = new HtmlSanitizer();

            s.AllowedTags.Clear();
            foreach (var t in AllowedTags) s.AllowedTags.Add(t);

            s.AllowedAttributes.Clear();
            s.AllowedAttributes.Add("class");

            s.AllowedClasses.Clear();          // 비어 있으면 '모두 허용'이라 반드시 채운다
            foreach (var c in AllowedClasses) s.AllowedClasses.Add(c);

            s.AllowedCssProperties.Clear();
            s.AllowedAtRules.Clear();
            s.AllowedSchemes.Clear();
            s.UriAttributes.Clear();
            s.AllowDataAttributes = false;
            s.KeepChildNodes = true;           // 모르는 태그는 벗기고 글자는 살린다
            return s;
        }

        /// <summary>편집기가 만든 서식 HTML 인가 (Quill 은 늘 문단·목록으로 시작한다).</summary>
        public static bool IsHtml(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            var t = content.AsSpan().TrimStart();
            return t.StartsWith("<p", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("<ul", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("<ol", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 저장할 때. 서식 HTML 이면 허용 서식만 남기고, 글자가 하나도 없으면(빈 편집기) null.
        /// 서식 없는 글은 앞뒤 공백만 다듬는다.
        /// </summary>
        public static string? Clean(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            if (!IsHtml(input)) return input.Trim();

            var html = Sanitize(input);
            return string.IsNullOrWhiteSpace(ToPlainText(html)) ? null : html;
        }

        /// <summary>허용 서식만 남긴 HTML.</summary>
        public static string Sanitize(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            // 1) 스크립트 같은 것은 알맹이째 버린다
            var doc = Parse(html);
            foreach (var el in doc.Body!.QuerySelectorAll(string.Join(",", DropWithContent)).ToList())
                el.Remove();

            // 2) 허용 목록으로 거른다
            var clean = Sanitizer.Sanitize(doc.Body.InnerHtml);

            // 3) 다듬기 — 줄바꿈 안 되는 공백(&nbsp;)은 보통 공백으로 (폰에서 줄이 안 넘어가 화면 밖으로 나간다),
            //    블록 사이의 빈 글자 조각은 지운다 (줄바꿈을 살리는 표시 방식에서 빈 줄로 보인다)
            var tidy = Parse(clean);
            foreach (var text in Descendants<IText>(tidy.Body!).ToList())
            {
                if (text.Data.Contains(' ')) text.Data = text.Data.Replace(' ', ' ');
                if (string.IsNullOrWhiteSpace(text.Data) && text.Parent is IElement p && p.LocalName is "body" or "ul" or "ol")
                    text.Remove();
            }
            foreach (var el in tidy.Body!.QuerySelectorAll("[class=\"\"]").ToList())
                el.RemoveAttribute("class");

            return tidy.Body.InnerHtml;
        }

        /// <summary>
        /// 학부모 화면에 그릴 HTML — 그릴 때 한 번 더 거르고, 「」로 감싼 말에 강조 표시를 입힌다
        /// (서식 없는 글과 같은 규칙).
        /// </summary>
        public static string ForDisplay(string html)
        {
            var doc = Parse(Sanitize(html));

            foreach (var text in Descendants<IText>(doc.Body!).ToList())
            {
                var parts = SplitBrackets(text.Data);
                if (!parts.Any(p => p.Highlight)) continue;

                var parent = text.Parent!;
                foreach (var (segment, highlight) in parts)
                {
                    if (highlight)
                    {
                        var mark = doc.CreateElement("mark");
                        mark.ClassName = "hp-em";
                        mark.TextContent = segment;
                        parent.InsertBefore(mark, text);
                    }
                    else
                    {
                        parent.InsertBefore(doc.CreateTextNode(segment), text);
                    }
                }
                text.Remove();
            }

            return doc.Body!.InnerHtml;
        }

        /// <summary>글자만 — 문단·목록 하나가 한 줄. 목록 화면의 미리보기 글, 빈 칸 판정에 쓴다.</summary>
        public static string ToPlainText(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            if (!IsHtml(content)) return content;

            var lines = new List<string>();
            Walk(Parse(content).Body!, lines);
            return string.Join("\n", lines);
        }

        /// <summary>편집기에 넣을 HTML — 서식 없는 글은 줄마다 문단으로 바꾼다.</summary>
        public static string ToEditorHtml(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            if (IsHtml(content)) return Sanitize(content);

            return string.Concat(content.Replace("\r\n", "\n").Split('\n')
                .Select(line => line.Length == 0 ? "<p><br></p>" : $"<p>{WebUtility.HtmlEncode(line)}</p>"));
        }

        // ── 안쪽 ─────────────────────────────────────────

        private static AngleSharp.Html.Dom.IHtmlDocument Parse(string html)
            => new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");

        private static IEnumerable<T> Descendants<T>(INode node) where T : INode
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is T t) yield return t;
                foreach (var d in Descendants<T>(child)) yield return d;
            }
        }

        private static void Walk(INode node, List<string> lines)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IElement el)
                {
                    switch (el.LocalName)
                    {
                        case "ul" or "ol":
                            Walk(el, lines);
                            break;
                        case "li":
                            lines.Add("· " + InlineText(el));
                            foreach (var nested in el.Children.Where(c => c.LocalName is "ul" or "ol")) Walk(nested, lines);
                            break;
                        default:
                            lines.Add(InlineText(el));
                            break;
                    }
                }
                else if (child is IText t && !string.IsNullOrWhiteSpace(t.Data))
                {
                    lines.Add(t.Data.Trim());
                }
            }
        }

        // 한 문단·항목의 글자 (안에 든 하위 목록은 빼고)
        private static string InlineText(IElement el)
            => string.Concat(el.ChildNodes.Select(c => c switch
            {
                IElement e when e.LocalName is "ul" or "ol" => "",
                IElement e when e.LocalName is "br" => "",
                IElement e => InlineText(e),
                _ => c.TextContent
            })).Trim();

        private static List<(string Text, bool Highlight)> SplitBrackets(string text)
        {
            var result = new List<(string, bool)>();
            int pos = 0;
            while (pos < text.Length)
            {
                int open = text.IndexOf('「', pos);
                int close = open < 0 ? -1 : text.IndexOf('」', open + 1);
                if (open < 0 || close < 0)
                {
                    result.Add((text[pos..], false));
                    break;
                }
                if (open > pos) result.Add((text[pos..open], false));
                result.Add((text[open..(close + 1)], true));
                pos = close + 1;
            }
            return result;
        }
    }
}
