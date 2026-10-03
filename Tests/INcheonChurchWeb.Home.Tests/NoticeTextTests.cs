using Church.Home.Data;
using INcheonChurchWeb.Home.Components.Pages;
using INcheonChurchWeb.Home.Services;

namespace INcheonChurchWeb.Home.Tests;

/// <summary>통신문을 그릴 때의 글자 처리 — 강조, 미리보기, 빈 열 숨기기, 전화 링크, 로그인 뒤 이동.</summary>
public class NoticeTextTests
{
    [Fact]
    public void 괄호_안의_말만_강조된다()
    {
        var s = NoticeText.Highlight("4월은 「봄맞이 야외예배」가 진행됩니다.");

        Assert.Equal(new[] { ("4월은 ", false), ("「봄맞이 야외예배」", true), ("가 진행됩니다.", false) },
            s.Select(x => (x.Text, x.Highlight)));
    }

    [Fact]
    public void 닫히지_않은_괄호는_그냥_글자로_둔다()
    {
        var s = NoticeText.Highlight("「닫지 않음");

        Assert.Equal(new[] { ("「닫지 않음", false) }, s.Select(x => (x.Text, x.Highlight)));
    }

    [Fact]
    public void 태그가_섞인_본문도_조각으로만_나눈다()
    {
        // HTML 을 만들지 않는다 — 화면은 이 조각을 글자로 찍는다
        var s = NoticeText.Highlight("<script>alert(1)</script>「주의」");

        Assert.Equal("<script>alert(1)</script>", s[0].Text);
        Assert.False(s[0].Highlight);
    }

    [Fact]
    public void 목록_미리보기는_이달의_안내_줄을_고른다()
    {
        var greeting = "샬롬! 주님의 이름으로 문안드립니다.\n4월은 「봄맞이 야외예배」가 진행됩니다.";

        Assert.Equal("4월은 「봄맞이 야외예배」가 진행됩니다.", NoticeText.Preview(greeting));
        Assert.Equal("샬롬!", NoticeText.Preview("샬롬!\n둘째 줄"));
        Assert.Equal("", NoticeText.Preview(null));
    }

    [Fact]
    public void 모든_줄이_빈_열은_숨긴다()
    {
        var weeks = new[]
        {
            new NoticeWeek { Scripture = "시편 95:1~11", Activity = "반" },
            new NoticeWeek { Scripture = "시편 101:5~8", Offering = " " },
        };

        var cols = NoticeText.ColumnsOf(weeks);

        Assert.Equal(new NoticeText.ScheduleColumns(Scripture: true, SermonTitle: false, Activity: true, Offering: false, Prayer: false), cols);
    }

    [Fact]
    public void 전화_링크에는_숫자와_기호만_남는다()
    {
        Assert.Equal("tel:010-9320-5802", NoticeText.TelHref("010-9320-5802"));
        Assert.Equal("tel:01093205802", NoticeText.TelHref("010 9320 5802"));
        Assert.Equal("tel:010-1234-5678", NoticeText.TelHref("javascript:010-1234-5678"));
        Assert.Null(NoticeText.TelHref("문의는 단체방으로"));
        Assert.Null(NoticeText.TelHref(null));
    }

    [Theory]
    [InlineData("/notice/3", "/notice/3")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public void 로그인_뒤에는_이_사이트_안으로만_돌아간다(string? returnUrl, string expected)
    {
        Assert.Equal(expected, Login.LocalOnly(returnUrl));
    }
}
