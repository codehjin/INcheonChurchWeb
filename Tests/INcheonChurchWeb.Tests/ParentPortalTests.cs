using Church.Home.Data;
using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 학부모 포털 2단계 — 재정앱에서 쓰고, 공개한 것만 home.db 로 내보낸다.
///
/// 고정하는 약속:
///   - 임시저장은 학부모에게 가지 않는다. 공개 중인 통신문의 원본 = 학부모가 받은 내용
///   - 비공개·삭제하면 학부모 DB 에서 사라진다
///   - 내보내기가 실패해도 원본은 지키고 '미전달'로 남는다 → 재동기화로 복구
///   - 연간계획에서는 날짜·행사명만 가져온다 (금액·추진내용 X)
///   - 부서 경계와 역할 권한은 서비스가 판단한다
/// </summary>
public class ParentPortalTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;
    private const int Year = 2026;

    private static readonly User 관리자 = new() { Username = "admin", Role = "Admin", DepartmentId = 99 };
    private static readonly User 유년부장 = new() { Username = "youth_mgr", Role = "Manager", DepartmentId = 유년부 };
    private static readonly User 유년부회계 = new() { Username = "youth_acc", Role = "User", DepartmentId = 유년부 };

    private readonly TestDb _db = new();
    private readonly HomeTestDb _home = new();
    private readonly ParentPortalService _svc;

    public ParentPortalTests() => _svc = new ParentPortalService(_db, new HomePublisher(_home));

    public void Dispose()
    {
        _db.Dispose();
        _home.Dispose();
    }

    private ParentPortalService 학부모DB가_고장난_서비스() => new(_db, new HomePublisher(new BrokenHomeDb()));

    // ── 도우미 ─────────────────────────────────────────

    private Task<ParentNotice> 초안(int month = 4, int dept = 유년부, User? who = null)
        => _svc.GetOrCreateDraftAsync(who ?? 유년부장, dept, Year, month);

    private async Task<int> 공개(int month = 4, string greeting = "샬롬!")
    {
        var n = await 초안(month);
        n.Greeting = greeting;
        var r = await _svc.PublishAsync(유년부장, n);
        Assert.True(r.Delivered);
        return r.Id;
    }

    private void 연간계획(int dept, string date, string title, string? end = null,
                      string? description = null, decimal expense = 0, bool deleted = false)
    {
        using var db = _db.CreateDbContext();
        db.AnnualPlans.Add(new AnnualPlan
        {
            DepartmentId = dept,
            EventDate = DateTime.Parse(date),
            EndDate = end == null ? null : DateTime.Parse(end),
            FiscalYear = Year,
            Title = title,
            Description = description,
            PlannedExpense = expense,
            IsDeleted = deleted
        });
        db.SaveChanges();
    }

    private ParentNotice 원본(int id)
    {
        using var db = _db.CreateDbContext();
        return db.ParentNotices.IgnoreQueryFilters().AsNoTracking().Single(n => n.Id == id);
    }

    private List<Notice> 학부모가_보는_통신문()
    {
        using var h = _home.CreateDbContext();
        return h.Notices.AsNoTracking()
            .Include(n => n.Weeks).Include(n => n.Sections).Include(n => n.Links)
            .ToList();
    }

    /// <summary>학부모 DB 에 들어간 통신문의 모든 글자를 한 줄로 — '새면 안 되는 말'을 찾는다.</summary>
    private static string 모든_글자(Notice n)
    {
        var parts = new List<object>();
        parts.Add(n);
        parts.AddRange(n.Weeks);
        parts.AddRange(n.Sections);
        parts.AddRange(n.Links);

        return string.Join("|", parts.SelectMany(o => o.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(o) ?? "")));
    }

    // ══════════════════════════════════════════════════════════════
    // 일정표 미리 채우기
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void 일정표는_그달_주일마다_한_줄이다()
    {
        Assert.Equal(new[] { 5, 12, 19, 26 }, ParentPortalService.SundaysOf(2026, 4).Select(d => d.Day));
        Assert.Equal(new[] { 3, 10, 17, 24, 31 }, ParentPortalService.SundaysOf(2026, 5).Select(d => d.Day));
        Assert.All(ParentPortalService.SundaysOf(2026, 11), d => Assert.Equal(DayOfWeek.Sunday, d.DayOfWeek));
    }

    [Fact]
    public async Task 새달_통신문을_열면_주일마다_한_줄이_준비된다()
    {
        var n = await 초안(4);

        Assert.Equal(0, n.Id);   // 아직 저장하지 않은 초안
        Assert.Equal(new[] { 5, 12, 19, 26 }, n.Weeks.Select(w => w.Date.Day));
        Assert.Equal(new[] { 0, 1, 2, 3 }, n.Weeks.Select(w => w.SortOrder));
    }

    [Fact]
    public async Task 연간계획_행사명이_그_주_활동칸에_들어간다()
    {
        연간계획(유년부, "2026-04-12", "봄맞이 야외예배");                   // 주일 하루 → 이름만
        연간계획(유년부, "2026-04-18", "교사 MT");                          // 토요일 → 그 주(12일) 줄, 날짜 붙임
        연간계획(유년부, "2026-04-24", "부활절 행사", end: "2026-04-25");     // 여러 날
        연간계획(유년부, "2026-04-01", "개학 예배");                        // 첫 주일 전 → 첫 줄
        연간계획(유년부, "2026-03-29", "봄방학 캠프", end: "2026-04-02");     // 지난달에 시작해 넘어옴 → 첫 줄
        연간계획(초등부, "2026-04-12", "초등부 소풍");                       // 다른 부서
        연간계획(유년부, "2026-04-26", "취소된 행사", deleted: true);         // 지운 계획
        연간계획(유년부, "2026-05-03", "어린이날 행사");                     // 다음 달

        var n = await 초안(4);

        Assert.Equal(new string?[]
        {
            "봄방학 캠프(3/29~4/2), 개학 예배(4/1)",
            "봄맞이 야외예배, 교사 MT(4/18)",
            "부활절 행사(4/24~4/25)",
            null
        }, n.Weeks.Select(w => w.Activity));
    }

    [Fact]
    public async Task 연간계획_추진내용과_금액은_가져오지_않는다()
    {
        연간계획(유년부, "2026-04-12", "야외예배", description: "보조금 30만원 협의 중", expense: 300000);

        var n = await 초안(4);
        Assert.Equal("야외예배", n.Weeks[1].Activity);
        Assert.DoesNotContain(n.Weeks, w => (w.Activity ?? "").Contains("보조금") || (w.Activity ?? "").Contains("300"));

        await _svc.PublishAsync(유년부장, n);

        var text = 모든_글자(Assert.Single(학부모가_보는_통신문()));
        Assert.DoesNotContain("보조금", text);
        Assert.DoesNotContain("300000", text);
        Assert.DoesNotContain("30만원", text);
    }

    [Fact]
    public async Task 지난_통신문의_표_아래_메모를_이어받는다()
    {
        var march = await 초안(3);
        march.ScheduleNote = "활동약어 : 반(반별활동), 생(생일잔치), 레(레크레이션)";
        await _svc.SaveDraftAsync(유년부장, march);

        var april = await 초안(4);

        Assert.Equal(march.ScheduleNote, april.ScheduleNote);
    }

    // ══════════════════════════════════════════════════════════════
    // 임시저장 · 공개 · 비공개 · 삭제
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task 임시저장은_전달되지_않는다()
    {
        var n = await 초안();
        n.Greeting = "아직 쓰는 중";

        var r = await _svc.SaveDraftAsync(유년부장, n);

        Assert.True(r.Id > 0);
        Assert.False(원본(r.Id).IsPublished);
        Assert.Empty(학부모가_보는_통신문());
    }

    [Fact]
    public async Task 공개하면_학부모_DB에_그대로_들어간다()
    {
        var n = await 초안();
        n.Greeting = "샬롬! 4월은 「봄맞이 야외예배」가 진행됩니다.";
        n.ScheduleNote = "활동약어 : 반(반별활동)";
        n.Weeks[0].Scripture = "마태복음 28:5~10";
        n.Weeks[0].SermonTitle = "예수님의 부활을 기쁘게 전해요";
        n.Weeks[0].Activity = "반";
        n.Weeks[0].Offering = "박소율";
        n.Weeks[0].Prayer = "한사미";
        n.Sections.Add(new ParentNoticeSection { Title = "봄맞이 야외예배 안내", Body = "시간: 11:00-13:20\n장소: 수봉공원" });
        n.Sections.Add(new ParentNoticeSection { Title = "달란트 프로그램 안내", Body = "매주 담임선생님이 체크합니다." });
        n.Links.Add(new ParentNoticeLink { Title = "3월 부활절 사진", Url = "https://photos.app.goo.gl/7GcHrzh2JwaH1gyeA" });

        var r = await _svc.PublishAsync(유년부장, n);

        Assert.True(r.Delivered);
        var home = Assert.Single(학부모가_보는_통신문());
        Assert.Equal(r.Id, home.Id);
        Assert.Equal((유년부, 2026, 4), (home.DepartmentId, home.Year, home.Month));
        Assert.Equal(n.Greeting, home.Greeting);
        Assert.Equal(n.ScheduleNote, home.ScheduleNote);

        var weeks = home.Weeks.OrderBy(w => w.SortOrder).ToList();
        Assert.Equal(new[] { 5, 12, 19, 26 }, weeks.Select(w => w.Date.Day));
        Assert.Equal(("마태복음 28:5~10", "예수님의 부활을 기쁘게 전해요", "반", "박소율", "한사미"),
            (weeks[0].Scripture, weeks[0].SermonTitle, weeks[0].Activity, weeks[0].Offering, weeks[0].Prayer));

        Assert.Equal(new[] { "봄맞이 야외예배 안내", "달란트 프로그램 안내" },
            home.Sections.OrderBy(s => s.SortOrder).Select(s => s.Title));
        Assert.Equal("https://photos.app.goo.gl/7GcHrzh2JwaH1gyeA", Assert.Single(home.Links).Url);

        var src = 원본(r.Id);
        Assert.True(src.IsPublished);
        Assert.True(src.HomeSynced);
        Assert.NotNull(src.PublishedAt);
    }

    [Fact]
    public async Task 다시_공개하면_이전_공개본을_통째로_바꾼다()
    {
        var n = await 초안();
        n.Sections.Add(new ParentNoticeSection { Title = "안내 1" });
        n.Sections.Add(new ParentNoticeSection { Title = "안내 2" });
        var id = (await _svc.PublishAsync(유년부장, n)).Id;

        var again = await 초안();
        Assert.Equal(id, again.Id);
        again.Sections.RemoveAt(1);
        again.Greeting = "고친 인사말";
        await _svc.PublishAsync(유년부장, again);

        var home = Assert.Single(학부모가_보는_통신문());
        Assert.Equal("고친 인사말", home.Greeting);
        Assert.Equal("안내 1", Assert.Single(home.Sections).Title);
        Assert.Equal(4, home.Weeks.Count);   // 일정표가 겹쳐 쌓이지 않는다

        using var db = _db.CreateDbContext();
        Assert.Equal(4, db.ParentNoticeWeeks.Count(w => w.ParentNoticeId == id));
    }

    [Fact]
    public async Task 공개_중인_통신문은_임시저장할_수_없다()
    {
        var id = await 공개(greeting: "공개본");

        var n = await 초안();
        n.Greeting = "쓰다 만 수정본";

        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.SaveDraftAsync(유년부장, n));

        // 원본도 학부모 DB 도 공개본 그대로 — 다음 재동기화가 미완성 내용을 내보낼 일이 없다
        using var db = _db.CreateDbContext();
        Assert.Equal("공개본", db.ParentNotices.Single(x => x.Id == id).Greeting);
        Assert.Equal("공개본", Assert.Single(학부모가_보는_통신문()).Greeting);
    }

    [Fact]
    public async Task 비공개로_되돌리면_학부모_DB에서_사라진다()
    {
        var id = await 공개();

        var r = await _svc.UnpublishAsync(유년부장, id);

        Assert.True(r.Delivered);
        Assert.Empty(학부모가_보는_통신문());
        var src = 원본(id);
        Assert.False(src.IsPublished);
        Assert.True(src.HomeSynced);
    }

    [Fact]
    public async Task 삭제하면_학부모_DB에서도_사라지고_같은_달을_다시_쓸_수_있다()
    {
        var id = await 공개();

        var r = await _svc.DeleteNoticeAsync(유년부장, id);

        Assert.True(r.Saved);
        Assert.Empty(학부모가_보는_통신문());
        Assert.True(원본(id).IsDeleted);
        Assert.Empty(await _svc.GetNoticesAsync(유년부장, 유년부, Year));

        // 지운 달에 새로 쓸 수 있다 (유니크 인덱스가 지운 행을 빼고 따진다)
        var fresh = await 초안();
        Assert.Equal(0, fresh.Id);
        Assert.True((await _svc.PublishAsync(유년부장, fresh)).Delivered);
    }

    [Fact]
    public async Task 학부모_DB_쓰기가_실패하면_미전달로_남고_재동기화로_복구된다()
    {
        var n = await 초안();
        n.Greeting = "샬롬!";

        var r = await 학부모DB가_고장난_서비스().PublishAsync(유년부장, n);

        Assert.True(r.Saved);
        Assert.False(r.Delivered);
        var src = 원본(r.Id);
        Assert.True(src.IsPublished);      // 원본 저장은 지킨다
        Assert.False(src.HomeSynced);      // 목록에 '미전달' 경고가 뜬다
        Assert.Empty(학부모가_보는_통신문());

        var resync = await _svc.ResyncAllAsync(관리자);

        Assert.True(resync.Delivered);
        Assert.Equal("샬롬!", Assert.Single(학부모가_보는_통신문()).Greeting);
        Assert.True(원본(r.Id).HomeSynced);
    }

    [Fact]
    public async Task 학부모_DB에서_내리지_못하면_삭제하지_않는다()
    {
        var id = await 공개();

        var r = await 학부모DB가_고장난_서비스().DeleteNoticeAsync(유년부장, id);

        Assert.False(r.Saved);
        Assert.False(원본(id).IsDeleted);   // 지웠다면 목록에서 사라져 경고를 띄울 자리가 없다
        Assert.Single(학부모가_보는_통신문());
    }

    [Fact]
    public async Task 재동기화하면_공개분과_정확히_일치한다()
    {
        var published = await 공개(month: 4);
        var draft = await 초안(month: 5);
        await _svc.SaveDraftAsync(유년부장, draft);

        // 학부모 DB 에 원본에 없는 찌꺼기가 남아 있다 (지난번 삭제의 내보내기 실패 등)
        using (var h = _home.CreateDbContext())
        {
            h.Notices.Add(new Notice { Id = 999, DepartmentId = 초등부, Year = Year, Month = 4, Greeting = "남은 찌꺼기" });
            h.SaveChanges();
        }

        await _svc.ResyncAllAsync(관리자);

        Assert.Equal(new[] { published }, 학부모가_보는_통신문().Select(n => n.Id));
    }

    [Fact]
    public async Task 위험한_링크는_저장되지_않는다()
    {
        var n = await 초안();
        n.Links.Add(new ParentNoticeLink { Title = "사진", Url = "javascript:alert(document.cookie)" });

        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.PublishAsync(유년부장, n));

        Assert.Empty(await _svc.GetNoticesAsync(유년부장, 유년부, Year));
        Assert.Empty(학부모가_보는_통신문());
    }

    [Fact]
    public async Task 빈_안내와_빈_링크_줄은_버린다()
    {
        var n = await 초안();
        n.Sections.Add(new ParentNoticeSection { Title = " ", Body = "" });
        n.Sections.Add(new ParentNoticeSection { Title = "달란트 안내", Body = "본문" });
        n.Links.Add(new ParentNoticeLink { Title = "", Url = " " });

        var id = (await _svc.SaveDraftAsync(유년부장, n)).Id;

        var saved = await 초안();
        Assert.Equal(id, saved.Id);
        Assert.Equal("달란트 안내", Assert.Single(saved.Sections).Title);
        Assert.Empty(saved.Links);
    }

    // ══════════════════════════════════════════════════════════════
    // 권한 · 부서 경계
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task 조회만_하는_계정은_통신문을_저장할_수_없다()
    {
        var n = await 초안(who: 유년부회계);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveDraftAsync(유년부회계, n));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.PublishAsync(유년부회계, n));
    }

    [Fact]
    public async Task 부서운영자는_다른_부서_통신문을_고칠_수_없다()
    {
        // 화면 주소를 고쳐 다른 부서로 새로 쓰려는 경우
        var other = new ParentNotice { DepartmentId = 초등부, Year = Year, Month = 4 };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.PublishAsync(유년부장, other));

        // 다른 부서 통신문의 Id 를 들고 와 자기 부서라고 우기는 경우
        var 초등부통신문 = await _svc.GetOrCreateDraftAsync(관리자, 초등부, Year, 4);
        int id = (await _svc.PublishAsync(관리자, 초등부통신문)).Id;

        var forged = new ParentNotice { Id = id, DepartmentId = 유년부, Year = Year, Month = 4, Greeting = "덮어쓰기" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.PublishAsync(유년부장, forged));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.UnpublishAsync(유년부장, id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.DeleteNoticeAsync(유년부장, id));

        Assert.Null(Assert.Single(학부모가_보는_통신문()).Greeting);
    }

    [Fact]
    public async Task 부서운영자가_다른_부서를_조회하면_자기_부서로_좁혀진다()
    {
        await _svc.PublishAsync(관리자, await _svc.GetOrCreateDraftAsync(관리자, 초등부, Year, 4));
        await 공개(month: 5);

        var list = await _svc.GetNoticesAsync(유년부장, 초등부, Year);

        Assert.All(list, n => Assert.Equal(유년부, n.DepartmentId));
        Assert.Equal(유년부, (await _svc.GetOrCreateDraftAsync(유년부장, 초등부, Year, 4)).DepartmentId);
    }

    [Fact]
    public async Task 학부모_계정과_재동기화는_최고관리자만_할_수_있다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.CreateAccountAsync(유년부장, 유년부, "youth", "hanaon91"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.GetAccountsAsync(유년부장));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.ResyncAllAsync(유년부장));
    }

    // ══════════════════════════════════════════════════════════════
    // 부서 소개
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task 부서_소개를_처음_열면_지금_교역자와_부장_이름이_채워진다()
    {
        using (var db = _db.CreateDbContext())
        {
            var today = DateTime.Today;
            db.DepartmentOfficers.AddRange(
                new DepartmentOfficer { DepartmentId = 유년부, Year = Year - 1, Role = "교역자", Name = "이전 전도사", StartDate = today.AddYears(-2), EndDate = today.AddYears(-1) },
                new DepartmentOfficer { DepartmentId = 유년부, Year = Year, Role = "교역자", Name = "김다은", StartDate = today.AddMonths(-3), EndDate = today.AddMonths(6) },
                new DepartmentOfficer { DepartmentId = 유년부, Year = Year, Role = "부장(팀장)", Name = "조형진", StartDate = today.AddMonths(-3), EndDate = today.AddMonths(6) },
                new DepartmentOfficer { DepartmentId = 유년부, Year = Year, Role = "총무", Name = "홍태윤", StartDate = today.AddMonths(-3), EndDate = today.AddMonths(6) });
            db.SaveChanges();
        }

        var p = await _svc.GetProfileAsync(유년부장, 유년부);

        Assert.Equal(("전도사", "김다은", "조형진"), (p.MinisterTitle, p.MinisterName, p.HeadName));
        Assert.Null(p.MinisterPhone);   // 연락처는 임원 명단에 없다 — 직접 적는다
        Assert.Null(p.UpdatedAt);       // 아직 저장 전
    }

    [Fact]
    public async Task 부서_소개를_저장하면_학부모_DB에_반영된다()
    {
        var p = await _svc.GetProfileAsync(유년부장, 유년부);
        p.DisplayName = "하나온 유년부";
        p.Motto = "한마음으로 사랑하고, 한뜻으로 하나되자!";
        p.OpenChatUrl = "https://open.kakao.com/o/hanaon";
        p.OpenChatCode = "9191";
        p.PhotoAlbumUrl = "https://photos.app.goo.gl/7GcHrzh2JwaH1gyeA";
        p.MinisterName = "김다은";
        p.MinisterPhone = "010-0000-0001";
        p.HeadName = "조형진";
        p.HeadPhone = "010-0000-0002";

        var r = await _svc.SaveProfileAsync(유년부장, p);

        Assert.True(r.Delivered);
        using var h = _home.CreateDbContext();
        var home = await h.PortalProfiles.SingleAsync();
        Assert.Equal(("유년부", "하나온 유년부", "9191"), (home.DeptName, home.DisplayName, home.OpenChatCode));
        Assert.Equal("https://photos.app.goo.gl/7GcHrzh2JwaH1gyeA", home.PhotoAlbumUrl);
        Assert.Equal(("김다은", "010-0000-0001", "조형진", "010-0000-0002"),
            (home.MinisterName, home.MinisterPhone, home.HeadName, home.HeadPhone));
    }

    [Fact]
    public async Task 부서_소개의_위험한_주소는_저장되지_않는다()
    {
        var p = await _svc.GetProfileAsync(유년부장, 유년부);
        p.YouTubeUrl = "javascript:alert(1)";

        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.SaveProfileAsync(유년부장, p));

        using var db = _db.CreateDbContext();
        Assert.Empty(db.ParentPortalProfiles);
    }

    [Fact]
    public async Task 부서운영자는_다른_부서_소개를_고칠_수_없다()
    {
        var forged = new ParentPortalProfile { DepartmentId = 초등부, DisplayName = "덮어쓰기" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveProfileAsync(유년부장, forged));
    }

    // ══════════════════════════════════════════════════════════════
    // 학부모 계정
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task 학부모_계정은_해시로만_저장되고_전달된다()
    {
        var r = await _svc.CreateAccountAsync(관리자, 유년부, " Hanaon ", "hanaon91");

        Assert.True(r.Delivered);
        var src = Assert.Single(await _svc.GetAccountsAsync(관리자));
        Assert.Equal("hanaon", src.Username);
        Assert.True(PasswordHasher.IsHashed(src.PasswordHash));
        Assert.DoesNotContain("hanaon91", src.PasswordHash);

        using var h = _home.CreateDbContext();
        var home = await h.ParentAccounts.SingleAsync();
        Assert.Equal((src.Id, "hanaon", "유년부", true), (home.Id, home.Username, home.DeptName, home.IsActive));
        Assert.True(PasswordHasher.VerifyHashed("hanaon91", home.PasswordHash));
    }

    [Fact]
    public async Task 정지하면_학부모_DB에도_정지로_반영된다()
    {
        int id = (await _svc.CreateAccountAsync(관리자, 유년부, "hanaon", "hanaon91")).Id;

        await _svc.SetAccountActiveAsync(관리자, id, false);

        using var h = _home.CreateDbContext();
        Assert.False((await h.ParentAccounts.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task 비밀번호를_바꾸면_새_비밀번호만_통한다()
    {
        int id = (await _svc.CreateAccountAsync(관리자, 유년부, "hanaon", "hanaon91")).Id;

        await _svc.ResetAccountPasswordAsync(관리자, id, "newpass77");

        using var h = _home.CreateDbContext();
        var stored = (await h.ParentAccounts.SingleAsync()).PasswordHash;
        Assert.True(PasswordHasher.VerifyHashed("newpass77", stored));
        Assert.False(PasswordHasher.VerifyHashed("hanaon91", stored));
    }

    [Fact]
    public async Task 직원_아이디와_같은_학부모_아이디는_만들_수_없다()
    {
        using (var db = _db.CreateDbContext())
        {
            db.Users.Add(new User { Username = "child", Password = "x", Role = "User", DepartmentId = 유년부 });
            db.SaveChanges();
        }

        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.CreateAccountAsync(관리자, 유년부, "child", "hanaon91"));
        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.CreateAccountAsync(관리자, 유년부, "a b", "hanaon91"));
        await Assert.ThrowsAsync<PortalValidationException>(() => _svc.CreateAccountAsync(관리자, 유년부, "hanaon", "123"));
    }

    [Fact]
    public void 자동_생성_비밀번호는_8자이고_헷갈리는_글자가_없다()
    {
        for (int i = 0; i < 50; i++)
        {
            var pw = ParentPortalService.GeneratePassword();
            Assert.Equal(8, pw.Length);
            Assert.DoesNotContain(pw, c => "0o1liI".Contains(c));
        }
    }
}
