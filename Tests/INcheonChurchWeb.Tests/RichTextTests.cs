using Church.Home.Data;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 가정통신문 서식(인사말·안내)의 허용 규칙을 고정한다.
/// 편집기가 만든 서식은 살리고, 학부모 화면으로 새면 안 되는 것(스크립트·링크·이미지·style·이벤트)은 남기지 않는다.
/// </summary>
public class RichTextTests
{
    [Fact]
    public void 편집기_서식은_남는다()
    {
        var html = "<p class=\"ql-align-center\"><strong>굵게</strong> <em>기울임</em> <u>밑줄</u> " +
                   "<span class=\"ql-color-red ql-font-jua\">빨강</span> <span class=\"ql-size-large ql-bg-yellow\">크게</span></p>" +
                   "<ul><li>하나</li><li class=\"ql-indent-1\">둘</li></ul><ol><li>첫째</li></ol>";

        var clean = RichText.Sanitize(html);

        foreach (var keep in new[] { "<strong>굵게</strong>", "<em>기울임</em>", "<u>밑줄</u>", "ql-align-center", "ql-color-red", "ql-font-jua",
                                     "ql-size-large", "ql-bg-yellow", "<ul>", "<ol>", "<li>하나</li>", "ql-indent-1" })
            Assert.Contains(keep, clean);
    }

    [Fact]
    public void 스크립트_링크_이미지_style_이벤트는_남지_않는다()
    {
        var html = "<p onclick=\"steal()\">본문<script>alert(1)</script>" +
                   "<img src=\"x\" onerror=\"alert(2)\"><a href=\"javascript:alert(3)\">링크</a>" +
                   "<span style=\"color:red;background:url(javascript:alert(4))\">색</span>" +
                   "<iframe src=\"https://evil.example\"></iframe><style>body{display:none}</style></p>";

        var clean = RichText.Sanitize(html);

        foreach (var gone in new[] { "script", "alert", "onclick", "onerror", "<img", "<a", "href", "style", "iframe", "evil", "display:none" })
            Assert.DoesNotContain(gone, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("본문", clean);
        Assert.Contains("링크", clean);   // 링크 글자는 남고 링크만 빠진다
        Assert.Contains("색", clean);
    }

    [Fact]
    public void 허용하지_않은_class와_속성은_지운다()
    {
        Assert.Equal("<p><span class=\"ql-color-red\">x</span></p>",
            RichText.Sanitize("<p><span class=\"ql-color-red evil-class\" data-x=\"1\" id=\"a\" title=\"t\">x</span></p>"));
        Assert.Equal("<p>y</p>", RichText.Sanitize("<p class=\"evil\">y</p>"));
    }

    [Fact]
    public void 모르는_태그는_벗기고_글자는_살린다()
    {
        var clean = RichText.Sanitize("<p><font color=\"red\">글자</font> <h1>제목</h1></p>");

        Assert.DoesNotContain("<font", clean);
        Assert.DoesNotContain("<h1", clean);
        Assert.Contains("글자", clean);
        Assert.Contains("제목", clean);
    }

    [Fact]
    public void 줄바꿈_안되는_공백은_보통_공백으로_바꾼다()
    {
        // 폰에서 줄이 넘어가지 않아 화면 밖으로 밀려나는 것을 막는다
        Assert.Equal("<p>가 나 다</p>", RichText.Sanitize("<p>가&nbsp;나 다</p>"));
    }

    [Fact]
    public void 빈_편집기는_빈_칸으로_본다()
    {
        Assert.Null(RichText.Clean("<p><br></p>"));
        Assert.Null(RichText.Clean("<p></p><p>   </p>"));
        Assert.Null(RichText.Clean("<p><script>alert(1)</script></p>"));
        Assert.Null(RichText.Clean(null));
    }

    [Fact]
    public void 서식_없는_글은_글자로_둔다()
    {
        Assert.False(RichText.IsHtml("<공지> 이번 주 안내"));
        Assert.False(RichText.IsHtml("샬롬!"));
        Assert.True(RichText.IsHtml("  <p>샬롬!</p>"));
        Assert.Equal("<공지> 이번 주 안내", RichText.Clean("  <공지> 이번 주 안내  "));
    }

    [Fact]
    public void 글자만_뽑으면_문단과_항목이_한_줄씩()
    {
        var text = RichText.ToPlainText("<p>샬롬!</p><p>4월은 <strong>「야외예배」</strong>가</p><ul><li>하나</li><li>둘<ul><li>둘-하나</li></ul></li></ul>");

        Assert.Equal("샬롬!\n4월은 「야외예배」가\n· 하나\n· 둘\n· 둘-하나", text);
    }

    [Fact]
    public void 화면에_그릴_때_괄호_강조를_입힌다()
    {
        var html = RichText.ForDisplay("<p>4월은 <strong>「봄맞이 야외예배」</strong>가 진행됩니다.</p>");

        Assert.Contains("<mark class=\"hp-em\">「봄맞이 야외예배」</mark>", html);
        Assert.Contains("가 진행됩니다.", html);
    }

    [Fact]
    public void 화면에_그릴_때도_다시_거른다()
    {
        var html = RichText.ForDisplay("<p>본문</p><script>alert(1)</script><p onclick=\"x\">둘</p>");

        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.Contains("둘", html);
    }

    [Fact]
    public void 서식_없는_글은_줄마다_문단으로_편집기에_넣는다()
    {
        Assert.Equal("<p>첫 줄</p><p><br></p><p>&lt;둘째&gt;</p>", RichText.ToEditorHtml("첫 줄\n\n<둘째>"));
        Assert.Equal("", RichText.ToEditorHtml(null));
    }
}
