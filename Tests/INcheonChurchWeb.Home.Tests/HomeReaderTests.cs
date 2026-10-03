using Church.Home.Data;
using INcheonChurchWeb.Home.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Home.Tests;

/// <summary>
/// 학부모앱이 home.db 에서 읽는 규칙을 고정한다.
///   - 연결은 읽기 전용이다 (설정이 틀려도)
///   - 해시로 저장된 비밀번호로만, 정지되지 않은 계정만 들어온다
///   - 정지·비번 재발급은 이미 로그인한 세션도 끊는다
///   - 자기 부서의 최근 3개만 — 번호를 바꿔 넣어도 넓어지지 않는다
/// </summary>
public class HomeReaderTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;

    private readonly HomeFileDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<ParentSession> 로그인(string username = "hanaon", string password = "hanaon91")
        => await _db.Reader().LoginAsync(username, password) ?? throw new Exception("로그인 실패");

    // ── 읽기 전용 ─────────────────────────────────────

    [Fact]
    public void 읽기_전용_연결에서는_쓰기가_거부된다()
    {
        using var db = _db.ReadOnly().CreateDbContext();

        db.Notices.Add(new Notice { Id = 1, DepartmentId = 유년부, Year = 2026, Month = 4 });
        var ex = Assert.Throws<DbUpdateException>(() => db.SaveChanges());

        Assert.Equal(8, Assert.IsType<SqliteException>(ex.InnerException).SqliteErrorCode);   // SQLITE_READONLY
    }

    [Fact]
    public void 설정에서_ReadOnly를_빼먹거나_바꿔도_읽기_전용으로_연다()
    {
        Assert.Equal(SqliteOpenMode.ReadOnly, new SqliteConnectionStringBuilder(HomeConnection.ReadOnly("Data Source=x.db")).Mode);
        Assert.Equal(SqliteOpenMode.ReadOnly, new SqliteConnectionStringBuilder(HomeConnection.ReadOnly("Data Source=x.db;Mode=ReadWriteCreate")).Mode);
    }

    [Fact]
    public void 상대_경로는_실행_위치가_아니라_앱_폴더_기준으로_푼다()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "app");

        var ds = new SqliteConnectionStringBuilder(HomeConnection.ReadOnly("Data Source=homedata/home.db", baseDir)).DataSource;

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "homedata/home.db")), ds);
    }

    // ── 로그인 ────────────────────────────────────────

    [Fact]
    public async Task 맞는_비밀번호로만_로그인된다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        var reader = _db.Reader();

        Assert.NotNull(await reader.LoginAsync("hanaon", "hanaon91"));
        Assert.NotNull(await reader.LoginAsync(" HanaOn ", "hanaon91"));   // 폰 자동 대문자·공백
        Assert.Null(await reader.LoginAsync("hanaon", "hanaon92"));
        Assert.Null(await reader.LoginAsync("nobody", "hanaon91"));
        Assert.Null(await reader.LoginAsync("", ""));
    }

    [Fact]
    public async Task 정지된_계정은_로그인되지_않는다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91", active: false);

        Assert.Null(await _db.Reader().LoginAsync("hanaon", "hanaon91"));
    }

    [Fact]
    public async Task 평문으로_저장된_비밀번호는_통하지_않는다()
    {
        _db.Change(db => db.ParentAccounts.Add(new ParentAccount { Id = 1, DepartmentId = 유년부, Username = "plain", PasswordHash = "1234", IsActive = true }));

        Assert.Null(await _db.Reader().LoginAsync("plain", "1234"));
    }

    // ── 세션 ──────────────────────────────────────────

    [Fact]
    public async Task 정지되면_로그인해_둔_세션도_끊긴다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        var session = await 로그인();

        _db.Change(db => db.ParentAccounts.Single().IsActive = false);

        Assert.Null(await _db.Reader().RestoreAsync(session));
        await Assert.ThrowsAsync<ParentSessionEndedException>(() => _db.Reader().GetHomeAsync(session));
    }

    [Fact]
    public async Task 비밀번호를_바꾸면_로그인해_둔_세션이_끊긴다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        var session = await 로그인();
        Assert.NotNull(await _db.Reader().RestoreAsync(session));

        // 비번이 단체방 밖으로 퍼져 관리자가 재발급한 경우
        _db.Change(db => db.ParentAccounts.Single().PasswordHash = PasswordHasher.Hash("newpass77"));

        Assert.Null(await _db.Reader().RestoreAsync(session));
    }

    [Fact]
    public async Task 지문을_고친_세션은_통하지_않는다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        var session = await 로그인();

        Assert.Null(await _db.Reader().RestoreAsync(session with { Stamp = "0000000000000000" }));
        Assert.Null(await _db.Reader().RestoreAsync(session with { AccountId = 2 }));
    }

    // ── 범위 ──────────────────────────────────────────

    [Fact]
    public async Task 학부모는_자기_부서_것만_본다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        _db.Notice(10, 유년부, 2026, 4, "유년부 4월");
        _db.Notice(20, 초등부, 2026, 4, "초등부 4월");
        var session = await 로그인();
        var reader = _db.Reader();

        var home = await reader.GetHomeAsync(session);

        Assert.Equal(new[] { 10 }, home.Notices.Select(n => n.Id));
        Assert.NotNull(await reader.GetNoticeAsync(session, 10));
        Assert.Null(await reader.GetNoticeAsync(session, 20));   // 주소의 번호만 바꿔 넣은 경우
    }

    [Fact]
    public async Task 최근_3개만_보이고_오래된_통신문은_번호로도_열리지_않는다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        _db.Notice(1, 유년부, 2025, 12);
        _db.Notice(2, 유년부, 2026, 1);
        _db.Notice(3, 유년부, 2026, 2);
        _db.Notice(4, 유년부, 2026, 3);
        var session = await 로그인();
        var reader = _db.Reader();

        var home = await reader.GetHomeAsync(session);

        Assert.Equal(new[] { 4, 3, 2 }, home.Notices.Select(n => n.Id));   // 최신부터
        Assert.Null(await reader.GetNoticeAsync(session, 1));
    }

    [Fact]
    public async Task 부서는_세션이_아니라_DB의_계정에서_읽는다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        _db.Notice(10, 유년부, 2026, 4);
        _db.Notice(20, 초등부, 2026, 4);
        var session = await 로그인();

        // 관리자가 계정의 부서를 옮기면, 이미 로그인한 세션도 새 부서 것을 본다
        _db.Change(db => db.ParentAccounts.Single().DepartmentId = 초등부);

        Assert.Equal(new[] { 20 }, (await _db.Reader().GetHomeAsync(session)).Notices.Select(n => n.Id));
    }

    [Fact]
    public async Task 통신문을_열면_일정_안내_사진이_순서대로_온다()
    {
        _db.Account(1, 유년부, "hanaon", "hanaon91");
        _db.Notice(10, 유년부, 2026, 4, fill: n =>
        {
            n.Weeks.Add(new NoticeWeek { Date = new DateTime(2026, 4, 12), SortOrder = 1 });
            n.Weeks.Add(new NoticeWeek { Date = new DateTime(2026, 4, 5), SortOrder = 0 });
            n.Sections.Add(new NoticeSection { Title = "달란트", SortOrder = 1 });
            n.Sections.Add(new NoticeSection { Title = "야외예배", SortOrder = 0 });
            n.Links.Add(new NoticeLink { Title = "부활절", Url = "https://photos.app.goo.gl/x", SortOrder = 0 });
        });
        _db.Change(db => db.PortalProfiles.Add(new PortalProfile { DepartmentId = 유년부, DeptName = "유년부", DisplayName = "하나온 유년부" }));
        var session = await 로그인();

        var view = await _db.Reader().GetNoticeAsync(session, 10);

        Assert.NotNull(view);
        Assert.Equal(new[] { 5, 12 }, view.Notice.Weeks.Select(w => w.Date.Day));
        Assert.Equal(new[] { "야외예배", "달란트" }, view.Notice.Sections.Select(s => s.Title));
        Assert.Single(view.Notice.Links);
        Assert.Equal("하나온 유년부", view.Profile?.DisplayName);
    }

    // ── 경계 ──────────────────────────────────────────

    [Fact]
    public void 학부모앱은_재정앱을_참조하지_않는다()
    {
        var refs = typeof(HomeReader).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("INcheonChurchWeb", refs);
        Assert.Contains("Church.Home.Data", refs);
    }
}
