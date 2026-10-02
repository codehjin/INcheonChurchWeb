using Church.Home.Data;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 재정앱과 학부모앱이 함께 쓰는 보안 도우미를 고정한다.
/// (PasswordHasher 는 재정앱에서 공유 라이브러리로 옮겨졌다. 저장 형식이 바뀌면 기존 스태프 로그인이 깨진다.)
/// </summary>
public class HomeSharedHelperTests
{
    // ── PasswordHasher ─────────────────────────────────

    [Fact]
    public void 해시한_비밀번호는_맞는_비밀번호로만_통과한다()
    {
        var stored = PasswordHasher.Hash("hanaon!2026");

        Assert.StartsWith("pbkdf2$100000$", stored);
        Assert.True(PasswordHasher.Verify("hanaon!2026", stored));
        Assert.False(PasswordHasher.Verify("hanaon!2027", stored));
    }

    [Fact]
    public void 재정앱_평문_레거시는_Verify가_여전히_받아준다()
    {
        // 스태프 계정 자동 이행(Login.razor)이 이 동작에 기댄다.
        Assert.True(PasswordHasher.Verify("1234", "1234"));
    }

    [Fact]
    public void 학부모앱용_VerifyHashed는_평문_저장값을_거부한다()
    {
        Assert.False(PasswordHasher.VerifyHashed("1234", "1234"));
        Assert.False(PasswordHasher.VerifyHashed("", ""));
        Assert.False(PasswordHasher.VerifyHashed("x", null));
        Assert.True(PasswordHasher.VerifyHashed("1234", PasswordHasher.Hash("1234")));
    }

    // ── SafeUrl ────────────────────────────────────────

    [Theory]
    [InlineData("https://photos.app.goo.gl/7GcHrzh2JwaH1gyeA")]
    [InlineData("https://open.kakao.com/o/abc")]
    [InlineData("http://youtube.com/@hanaon")]
    [InlineData("  https://photos.app.goo.gl/x  ")]
    public void http_주소는_링크로_내보낼_수_있다(string url)
    {
        Assert.True(SafeUrl.IsHttp(url));
        Assert.Equal(url.Trim(), SafeUrl.OrNull(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/notice/1")]
    [InlineData("photos.app.goo.gl/x")]
    [InlineData("ftp://example.com/a")]
    [InlineData("")]
    [InlineData(null)]
    public void http_가_아닌_주소는_막는다(string? url)
    {
        Assert.False(SafeUrl.IsHttp(url));
        Assert.Null(SafeUrl.OrNull(url));
    }
}
