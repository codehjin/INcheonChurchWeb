using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace INcheonChurchWeb.Home.Tests;

/// <summary>
/// 학부모앱을 통째로 띄워 HTTP 로 확인한다 — 로그인 폼 → 쿠키 → 화면 → 정지까지.
/// 화면을 하나 더해도 로그인 없이는 열리지 않는지(기본 잠금)가 여기서 고정된다.
/// </summary>
public class HomeSiteTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;

    private readonly HomeFileDb _db = new();
    private readonly WebApplicationFactory<Program> _app;

    public HomeSiteTests()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:HomeConnection", $"Data Source={_db.Path}");
            b.UseSetting("DataProtection:KeysPath", _db.KeysDir);
        });

        _db.Account(1, 유년부, "hanaon", "hanaon91");
        _db.Notice(10, 유년부, 2026, 4, "4월은 「봄맞이 야외예배」가 진행됩니다.");
        _db.Notice(20, 초등부, 2026, 4, "초등부만 보는 글");
    }

    public void Dispose()
    {
        _app.Dispose();
        SqliteConnection.ClearAllPools();
        _db.Dispose();
    }

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>로그인 화면을 열어 폼의 숨은 칸(위조 방지 토큰 등)을 그대로 담아 보낸다 — 브라우저와 같게.</summary>
    private static async Task<HttpResponseMessage> 로그인(HttpClient client, string username, string password, string? returnUrl = null)
    {
        var page = await client.GetStringAsync("/login" + (returnUrl == null ? "" : $"?ReturnUrl={Uri.EscapeDataString(returnUrl)}"));

        var fields = Regex.Matches(page, "<input[^>]*type=\"hidden\"[^>]*>")
            .Select(m => (Name: Attr(m.Value, "name"), Value: Attr(m.Value, "value")))
            .Where(f => f.Name != null)
            .ToDictionary(f => f.Name!, f => WebUtility.HtmlDecode(f.Value ?? ""));
        fields["Input.Username"] = username;
        fields["Input.Password"] = password;

        var url = "/login" + (returnUrl == null ? "" : $"?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    /// <summary>
    /// 리다이렉트 목적지의 경로. 절대 주소로 오면 이 사이트(localhost)인지부터 확인한다 —
    /// 다른 사이트로 보내는 응답이면 그 주소를 그대로 돌려 테스트가 실패하게 한다.
    /// </summary>
    private static string 이동할_곳(HttpResponseMessage r)
    {
        var loc = r.Headers.Location ?? throw new Exception($"리다이렉트가 아니다: {(int)r.StatusCode}");
        if (!loc.IsAbsoluteUri) return loc.OriginalString;
        return loc.Host == "localhost" ? loc.PathAndQuery : loc.ToString();
    }

    private static string? Attr(string tag, string name)
        => Regex.Match(tag, $"{name}=\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : null;

    [Fact]
    public async Task 로그인하지_않으면_로그인_화면으로_보낸다()
    {
        using var client = Client();

        foreach (var path in new[] { "/", "/notice/10" })
        {
            var r = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.StartsWith("http://localhost/login", r.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task 로그인_화면과_healthz는_로그인_없이_열린다()
    {
        using var client = Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
        Assert.Equal("ok", await client.GetStringAsync("/healthz"));
    }

    [Fact]
    public async Task 로그인하면_자기_부서_통신문이_보이고_정지하면_바로_끊긴다()
    {
        using var client = Client();

        var login = await 로그인(client, "hanaon", "hanaon91");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", 이동할_곳(login));

        var home = await client.GetStringAsync("/");
        Assert.Contains("4월 가정통신문", home);
        Assert.DoesNotContain("초등부만 보는 글", home);

        var detail = await client.GetAsync("/notice/10");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("「봄맞이 야외예배」", await detail.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/notice/20")).StatusCode);   // 다른 부서 번호

        // 관리자가 계정을 정지하면, 이미 받은 쿠키로도 다음 요청부터 못 들어온다
        _db.Change(db => db.ParentAccounts.Single().IsActive = false);

        var after = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("http://localhost/login", after.Headers.Location!.ToString());
    }

    [Fact]
    public async Task 틀린_비밀번호는_쿠키를_주지_않는다()
    {
        using var client = Client();

        var r = await 로그인(client, "hanaon", "wrong-pass");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("아이디 또는 비밀번호가 맞지 않습니다", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task 로그인_뒤_다른_사이트로_보내지_않는다()
    {
        using var client = Client();

        var r = await 로그인(client, "hanaon", "hanaon91", returnUrl: "//evil.example/");

        Assert.Equal("/", 이동할_곳(r));
    }

    [Fact]
    public async Task 재정앱_화면은_학부모앱에_없다()
    {
        using var client = Client();
        await 로그인(client, "hanaon", "hanaon91");

        foreach (var path in new[] { "/monthly", "/dashboard", "/settings", "/notices", "/uploads/receipts/a.jpg" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task 본문의_태그는_글자로_나가고_한글은_그대로_나간다()
    {
        _db.Change(db => db.Notices.Single(n => n.Id == 10).Greeting = "<script>alert(1)</script> 「야외예배」");
        using var client = Client();
        await 로그인(client, "hanaon", "hanaon91");

        var html = await client.GetStringAsync("/notice/10");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("「야외예배」", html);   // 문자 코드(&#x...;)로 부풀리지 않는다
    }

    [Fact]
    public async Task 보안_헤더가_붙는다()
    {
        using var client = Client();

        var r = await client.GetAsync("/login");

        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task 로그아웃은_위조_방지_토큰이_있어야_한다()
    {
        using var client = Client();
        await 로그인(client, "hanaon", "hanaon91");

        // 다른 사이트에서 몰래 보낸 요청 (토큰 없음)
        var forged = await client.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);   // 여전히 로그인 상태
    }
}
