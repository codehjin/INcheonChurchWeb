using System.Net;
using Church.Home.Data;
using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 가정통신문 서식 편집기 · 미리보기.
///   - 편집기 HTML 은 허용한 서식만 남아 저장되고, 그대로 학부모에게 간다
///   - [미리보기]는 저장하지 않고, 공개했을 때와 같은 정리·옮겨 적기·화면 부품으로 그린다
/// </summary>
public class ParentNoticeRichTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;

    private static readonly User 유년부장 = new() { Username = "youth_mgr", Role = "Manager", DepartmentId = 유년부 };

    private const string 위험한_인사말 =
        "<p class=\"ql-align-center\"><strong>샬롬!</strong> <span class=\"ql-color-red ql-font-jua\">「봄맞이 야외예배」</span></p>" +
        "<script>alert(1)</script><p onclick=\"steal()\">둘째 줄 <a href=\"javascript:alert(2)\">링크</a><img src=x onerror=alert(3)></p>";

    private readonly TestDb _db = new();
    private readonly HomeTestDb _home = new();
    private readonly ParentPortalService _svc;

    public ParentNoticeRichTests() => _svc = new ParentPortalService(_db, new HomePublisher(_home));

    public void Dispose()
    {
        _db.Dispose();
        _home.Dispose();
    }

    private Task<ParentNotice> 초안(int month = 4) => _svc.GetOrCreateDraftAsync(유년부장, 유년부, 2026, month);

    private static void 위험한_것이_없다(string? html)
    {
        Assert.NotNull(html);
        foreach (var gone in new[] { "<script", "alert", "onclick", "onerror", "<a ", "href", "<img", "style=" })
            Assert.DoesNotContain(gone, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 서식_있는_인사말과_안내는_허용한_서식만_학부모에게_간다()
    {
        var n = await 초안();
        n.Greeting = 위험한_인사말;
        n.Sections.Add(new ParentNoticeSection { Title = "준비물", Body = "<ul><li><strong>성경책</strong></li><li class=\"ql-indent-1\">QT책</li></ul><iframe src=\"https://evil.example\"></iframe>" });

        var r = await _svc.PublishAsync(유년부장, n);

        using var h = _home.CreateDbContext();
        var home = await h.Notices.Include(x => x.Sections).SingleAsync();
        위험한_것이_없다(home.Greeting);
        Assert.Contains("<strong>샬롬!</strong>", home.Greeting);
        Assert.Contains("ql-align-center", home.Greeting);
        Assert.Contains("ql-color-red", home.Greeting);
        Assert.Contains("둘째 줄", home.Greeting);

        var body = Assert.Single(home.Sections).Body;
        Assert.Contains("<li><strong>성경책</strong></li>", body);
        Assert.DoesNotContain("iframe", body);
        Assert.DoesNotContain("evil", body);

        // 원본(church.db)에도 걸러진 것만 남는다
        using var db = _db.CreateDbContext();
        위험한_것이_없다(db.ParentNotices.Single(x => x.Id == r.Id).Greeting);
    }

    [Fact]
    public async Task 빈_편집기는_빈_칸으로_저장된다()
    {
        var n = await 초안();
        n.Greeting = "<p><br></p>";
        n.Sections.Add(new ParentNoticeSection { Title = "", Body = "<p><br></p>" });

        var id = (await _svc.SaveDraftAsync(유년부장, n)).Id;

        var saved = await 초안();
        Assert.Equal(id, saved.Id);
        Assert.Null(saved.Greeting);
        Assert.Empty(saved.Sections);
    }

    [Fact]
    public async Task 너무_긴_본문은_저장하지_않는다()
    {
        var n = await 초안();
        n.Greeting = "<p>" + new string('가', RichText.MaxLength + 10) + "</p>";

        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.SaveDraftAsync(유년부장, n));
    }

    [Fact]
    public async Task 미리보기는_저장하지_않고_공개할_모양_그대로_만든다()
    {
        using (var db = _db.CreateDbContext())
        {
            db.ParentPortalProfiles.Add(new ParentPortalProfile { DepartmentId = 유년부, DisplayName = "하나온 유년부", HeadName = "조부장", HeadPhone = "010-0000-0002" });
            db.SaveChanges();
        }
        var n = await 초안();
        n.Greeting = 위험한_인사말;
        n.Links.Add(new ParentNoticeLink { Title = "사진", Url = "https://photos.app.goo.gl/x" });

        var p = await _svc.BuildPreviewAsync(유년부장, n);

        위험한_것이_없다(p.Notice.Greeting);
        Assert.Equal((유년부, 2026, 4, 4), (p.Notice.DepartmentId, p.Notice.Year, p.Notice.Month, p.Notice.Weeks.Count));
        Assert.Single(p.Notice.Links);
        Assert.Equal(("유년부", "하나온 유년부", "조부장"), (p.DeptName, p.Profile?.DisplayName, p.Profile?.HeadName));

        // 아무것도 저장되지 않았다
        using var check = _db.CreateDbContext();
        Assert.Empty(check.ParentNotices);
        using var h = _home.CreateDbContext();
        Assert.Empty(h.Notices);
    }

    [Fact]
    public async Task 미리보기도_저장과_같은_검사를_한다()
    {
        var n = await 초안();
        n.Links.Add(new ParentNoticeLink { Title = "사진", Url = "javascript:alert(1)" });
        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.BuildPreviewAsync(유년부장, n));

        var other = new ParentNotice { DepartmentId = 초등부, Year = 2026, Month = 4 };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.BuildPreviewAsync(유년부장, other));
    }

    [Fact]
    public async Task 미리보기_화면은_학부모앱의_부품과_스타일로_그린다()
    {
        var n = await 초안();
        n.Greeting = 위험한_인사말;
        n.Sections.Add(new ParentNoticeSection { Title = "봄맞이 야외예배 안내", Body = "<p>장소: 수봉공원</p>" });
        var preview = await _svc.BuildPreviewAsync(유년부장, n);

        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new NoticePreviewRenderer(services, services.GetRequiredService<ILoggerFactory>(), new TestEnv());

        var html = await renderer.RenderAsync(preview, "https://ijch-edu.kro.kr/");
        var text = WebUtility.HtmlDecode(html);

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        Assert.Contains("https://ijch-edu.kro.kr/_content/Church.Home.Ui/home.css", html);
        Assert.Contains("https://ijch-edu.kro.kr/_content/Church.Home.Ui/rich.css", html);
        Assert.Contains("class=\"hp-paper\"", html);                 // 학부모앱과 같은 통신문 부품
        Assert.Contains("4월", text);
        Assert.Contains("봄맞이 야외예배 안내", text);
        Assert.Contains("<mark class=\"hp-em\">「봄맞이 야외예배」</mark>", html);   // 「」 강조까지 학부모 화면과 같게
        // 문서에는 스타일 링크(link href)가 있으니, 본문에서 새면 안 되는 것만 본다
        foreach (var gone in new[] { "<script", "alert", "onclick", "onerror", "<img", "javascript:", "<iframe" })
            Assert.DoesNotContain(gone, html, StringComparison.OrdinalIgnoreCase);
    }
}
