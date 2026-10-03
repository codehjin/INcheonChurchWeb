using System.Security.Cryptography;
using System.Text.RegularExpressions;
using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using PasswordHasher = Church.Home.Data.PasswordHasher;
using SafeUrl = Church.Home.Data.SafeUrl;
using RichText = Church.Home.Data.RichText;
using HomeNotice = Church.Home.Data.Notice;
using HomeProfile = Church.Home.Data.PortalProfile;

namespace INcheonChurchWeb.Services
{
    /// <summary>저장·공개 결과. Saved=false 면 아무것도 바뀌지 않았다.</summary>
    public sealed class PortalOutcome
    {
        public int Id { get; init; }
        public bool Saved { get; init; } = true;

        /// <summary>home.db(학부모 화면)가 지금 상태를 반영하고 있는가</summary>
        public bool Delivered { get; init; }

        public string Message { get; init; } = "";
    }

    /// <summary>입력이 잘못되어 저장하지 않았다. Message 를 그대로 화면에 보여 준다.</summary>
    public class PortalValidationException : Exception
    {
        public PortalValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// 🚀 학부모 포털 — 가정통신문 · 부서 소개 · 학부모 계정의 작성과 공개.
    ///
    /// 권한은 화면이 아니라 여기서 판단한다 (화면이 넘긴 부서·Id 는 조작될 수 있다).
    ///   - 조회: 전체 조회 권한이 없으면 자기 부서로 좁힌다
    ///   - 작성·공개: CanEdit. 다른 부서는 CanViewAll(최고관리자)만
    ///   - 학부모 계정 · 전체 재동기화: 최고관리자만
    ///
    /// church.db 에 먼저 저장하고, home.db 로는 HomePublisher 를 통해서만 내보낸다.
    /// 내보내기가 실패해도 원본 저장은 지키고 HomeSynced=false 로 남겨 목록에 경고를 띄운다.
    /// </summary>
    public class ParentPortalService
    {
        /// <summary>학부모 사이트 주소 — 화면 안내문에 쓴다.</summary>
        public const string ParentSiteUrl = "https://ijch-school.kro.kr";

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly HomePublisher _publisher;
        private readonly ILogger<ParentPortalService>? _log;

        public ParentPortalService(IDbContextFactory<AppDbContext> dbFactory, HomePublisher publisher,
                                   ILogger<ParentPortalService>? log = null)
        {
            _dbFactory = dbFactory;
            _publisher = publisher;
            _log = log;
        }

        // ══════════════════════════════════════════════════════════════
        // 권한
        // ══════════════════════════════════════════════════════════════

        // 어떤 부서 Id 와도 일치하지 않는 값 — 소속 없는 사용자의 조회 결과를 비운다.
        private const int NoDepartment = -1;

        private static int ViewDept(User actor, int requestedDeptId)
        {
            if (actor.CanViewAll) return requestedDeptId;
            return actor.DepartmentId > 0 ? actor.DepartmentId : NoDepartment;
        }

        private static int EditDept(User actor, int requestedDeptId)
        {
            if (!actor.CanEdit)
                throw new UnauthorizedAccessException("가정통신문을 고칠 권한이 없습니다.");
            if (actor.CanViewAll) return requestedDeptId;
            if (requestedDeptId != actor.DepartmentId)
                throw new UnauthorizedAccessException("다른 부서의 가정통신문은 고칠 수 없습니다.");
            return requestedDeptId;
        }

        private static void RequireAdmin(User actor)
        {
            if (!actor.IsSystemAdmin)
                throw new UnauthorizedAccessException("최고 관리자만 할 수 있습니다.");
        }

        /// <summary>학부모 포털을 쓰는 부서 — 관리자·시스템용 부서는 뺀다 (분기 일괄 설정과 같은 기준).</summary>
        public async Task<List<Department>> GetPortalDepartmentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Departments.AsNoTracking()
                .Where(d => d.Name != "관리자" && d.Name != "시스템")
                .OrderBy(d => d.Id)
                .ToListAsync();
        }

        // ══════════════════════════════════════════════════════════════
        // 가정통신문
        // ══════════════════════════════════════════════════════════════

        /// <summary>부서·연도의 통신문 목록 (자식 제외). 월 순.</summary>
        public async Task<List<ParentNotice>> GetNoticesAsync(User actor, int deptId, int year)
        {
            int dept = ViewDept(actor, deptId);
            using var db = _dbFactory.CreateDbContext();
            return await db.ParentNotices.AsNoTracking()
                .Where(n => n.DepartmentId == dept && n.Year == year)
                .OrderBy(n => n.Month)
                .ToListAsync();
        }

        /// <summary>
        /// 그달 통신문을 연다. 없으면 저장하지 않은 새 초안을 돌려준다:
        ///   - 일정표: 그달 주일마다 한 줄 + 연간계획 행사명을 '활동' 칸에
        ///   - 표 아래 메모: 지난 통신문에서 그대로 (활동약어처럼 달마다 같다)
        /// </summary>
        public async Task<ParentNotice> GetOrCreateDraftAsync(User actor, int deptId, int year, int month)
        {
            CheckMonth(month);
            int dept = ViewDept(actor, deptId);
            using var db = _dbFactory.CreateDbContext();

            var existing = await db.ParentNotices.AsNoTracking()
                .Include(n => n.Weeks.OrderBy(w => w.SortOrder))
                .Include(n => n.Sections.OrderBy(s => s.SortOrder))
                .Include(n => n.Links.OrderBy(l => l.SortOrder))
                .AsSplitQuery()
                .FirstOrDefaultAsync(n => n.DepartmentId == dept && n.Year == year && n.Month == month);
            if (existing != null) return existing;

            var previous = await PreviousNoticeQuery(db, dept, year, month).FirstOrDefaultAsync();

            return new ParentNotice
            {
                DepartmentId = dept,
                Year = year,
                Month = month,
                ScheduleNote = previous?.ScheduleNote,
                Weeks = await BuildWeeksAsync(db, dept, year, month)
            };
        }

        /// <summary>그달 일정표를 새로 만든다 — 화면의 [빈 활동 칸 채우기]가 쓴다.</summary>
        public async Task<List<ParentNoticeWeek>> BuildWeeksAsync(User actor, int deptId, int year, int month)
        {
            CheckMonth(month);
            int dept = ViewDept(actor, deptId);
            using var db = _dbFactory.CreateDbContext();
            return await BuildWeeksAsync(db, dept, year, month);
        }

        /// <summary>바로 앞 통신문의 안내 꼭지 — [지난 안내 가져오기] (달란트 안내처럼 매달 실리는 글).</summary>
        public async Task<List<ParentNoticeSection>> GetPreviousSectionsAsync(User actor, int deptId, int year, int month)
        {
            int dept = ViewDept(actor, deptId);
            using var db = _dbFactory.CreateDbContext();
            var previous = await PreviousNoticeQuery(db, dept, year, month)
                .Include(n => n.Sections.OrderBy(s => s.SortOrder))
                .FirstOrDefaultAsync();

            return previous?.Sections
                .Select(s => new ParentNoticeSection { Title = s.Title, Body = s.Body, SortOrder = s.SortOrder })
                .ToList() ?? new();
        }

        private static IQueryable<ParentNotice> PreviousNoticeQuery(AppDbContext db, int dept, int year, int month)
            => db.ParentNotices.AsNoTracking()
                .Where(n => n.DepartmentId == dept && (n.Year < year || (n.Year == year && n.Month < month)))
                .OrderByDescending(n => n.Year).ThenByDescending(n => n.Month);

        /// <summary>
        /// [임시저장] — church.db 에만 저장한다. 학부모에게는 보이지 않는다.
        ///
        /// 🔒 공개 중인 통신문은 임시저장할 수 없다. [공개]로 바로 고치거나 먼저 [비공개로 전환]한다.
        ///    church.db 에는 내용이 한 벌뿐이라, 공개본 위에 임시저장을 허락하면
        ///    다음 [전체 재동기화](기동 때마다 돈다)가 그 미완성 내용을 학부모에게 내보낸다.
        ///    이 규칙 덕분에 '공개 중인 통신문의 church.db 내용 = 학부모가 받은 내용'이 늘 성립한다.
        /// </summary>
        public async Task<PortalOutcome> SaveDraftAsync(User actor, ParentNotice input)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await SaveContentAsync(db, actor, input);

            if (target.IsPublished)
                throw new PortalValidationException(
                    "공개 중인 통신문은 임시저장할 수 없습니다. [공개]로 바로 고치거나, 먼저 [비공개로 전환]해 주세요.");

            await db.SaveChangesAsync();

            return new PortalOutcome
            {
                Id = target.Id,
                Delivered = target.HomeSynced,
                Message = "임시저장했습니다. 학부모에게는 보이지 않습니다."
            };
        }

        /// <summary>[공개] — 저장하고 학부모 화면에 내보낸다.</summary>
        public async Task<PortalOutcome> PublishAsync(User actor, ParentNotice input)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await SaveContentAsync(db, actor, input);

            target.IsPublished = true;
            target.PublishedAt = DateTime.Now;
            target.HomeSynced = false;
            await db.SaveChangesAsync();

            bool delivered = await TryHomeAsync(() => _publisher.PublishNoticeAsync(target));
            if (delivered)
            {
                target.HomeSynced = true;
                await db.SaveChangesAsync();
            }

            return new PortalOutcome
            {
                Id = target.Id,
                Delivered = delivered,
                Message = delivered
                    ? "공개했습니다. 학부모 화면에 반영되었습니다."
                    : "저장은 되었지만 학부모 화면에 반영하지 못했습니다. 잠시 후 [공개]를 다시 눌러 주세요."
            };
        }

        /// <summary>미리보기에 그릴 것 — 학부모 화면과 같은 모양으로 옮겨 적은 통신문 · 부서 소개 · 부서명.</summary>
        public sealed record NoticePreview(HomeNotice Notice, HomeProfile? Profile, string DeptName);

        /// <summary>
        /// [미리보기] — 지금 편집 중인 내용을 학부모 화면에 나갈 모양 그대로 만든다. 저장하지 않는다.
        /// 저장·공개와 같은 정리(Normalize)와 같은 옮겨 적기(HomePublisher.ToHome)를 거친다 —
        /// 미리보기에 보이는 것이 곧 공개했을 때 학부모가 보는 것이다.
        /// </summary>
        public async Task<NoticePreview> BuildPreviewAsync(User actor, ParentNotice input)
        {
            int dept = EditDept(actor, input.DepartmentId);
            var content = Normalize(input);

            using var db = _dbFactory.CreateDbContext();
            var profile = await db.ParentPortalProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.DepartmentId == dept);
            string deptName = (await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dept))?.Name ?? "";

            var draft = new ParentNotice
            {
                Id = input.Id,
                DepartmentId = dept,
                Year = input.Year,
                Month = input.Month,
                Greeting = content.Greeting,
                ScheduleNote = content.ScheduleNote,
                Weeks = content.Weeks,
                Sections = content.Sections,
                Links = content.Links,
                PublishedAt = DateTime.Now
            };

            return new NoticePreview(
                HomePublisher.ToHome(draft),
                profile == null ? null : HomePublisher.ToHome(profile, deptName),
                deptName);
        }

        /// <summary>[비공개로 전환] — 학부모 화면에서 내리고 임시저장 상태로 돌린다.</summary>
        public async Task<PortalOutcome> UnpublishAsync(User actor, int noticeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await FindNoticeForEditAsync(db, actor, noticeId);

            target.IsPublished = false;
            target.HomeSynced = false;
            target.UpdatedBy = actor.Username;
            await db.SaveChangesAsync();

            bool delivered = await TryHomeAsync(() => _publisher.RemoveNoticeAsync(target.Id));
            if (delivered)
            {
                target.HomeSynced = true;
                await db.SaveChangesAsync();
            }

            return new PortalOutcome
            {
                Id = target.Id,
                Delivered = delivered,
                Message = delivered
                    ? "비공개로 돌렸습니다. 학부모 화면에서 내려갔습니다."
                    : "비공개로 바꿨지만 학부모 화면에서 아직 내리지 못했습니다. 잠시 후 다시 눌러 주세요."
            };
        }

        /// <summary>
        /// 삭제 — 학부모 화면에서 먼저 내리고, 성공해야 지운다.
        /// (지운 통신문은 목록에 안 보이므로, 내리지 못한 채 지우면 경고를 띄울 자리가 없다)
        /// </summary>
        public async Task<PortalOutcome> DeleteNoticeAsync(User actor, int noticeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await FindNoticeForEditAsync(db, actor, noticeId);

            if (!await TryHomeAsync(() => _publisher.RemoveNoticeAsync(target.Id)))
            {
                return new PortalOutcome
                {
                    Id = target.Id,
                    Saved = false,
                    Delivered = false,
                    Message = "학부모 화면에서 내리지 못해 삭제하지 않았습니다. 잠시 후 다시 시도해 주세요."
                };
            }

            target.IsDeleted = true;
            target.IsPublished = false;
            target.HomeSynced = true;
            target.UpdatedBy = actor.Username;
            await db.SaveChangesAsync();

            return new PortalOutcome { Id = target.Id, Delivered = true, Message = "삭제했습니다." };
        }

        private static async Task<ParentNotice> FindNoticeForEditAsync(AppDbContext db, User actor, int noticeId)
        {
            var target = await db.ParentNotices.FirstOrDefaultAsync(n => n.Id == noticeId)
                         ?? throw new PortalValidationException("통신문을 찾을 수 없습니다.");
            EditDept(actor, target.DepartmentId);
            return target;
        }

        /// <summary>
        /// 내용을 church.db 에 옮겨 적는다 (SaveChanges 는 호출부가). 자식 3종은 통째로 바꾼다.
        /// 빈 줄은 버리고, 링크 주소는 http(s) 만 받는다.
        /// </summary>
        /// <summary>저장·미리보기가 함께 쓰는 정리 결과.</summary>
        private sealed record NoticeContent(string? Greeting, string? ScheduleNote,
            List<ParentNoticeWeek> Weeks, List<ParentNoticeSection> Sections, List<ParentNoticeLink> Links);

        /// <summary>
        /// 입력을 저장할 모양으로 다듬는다 — 빈 줄은 버리고, 인사말·안내의 서식은 허용한 것만 남기고(RichText),
        /// 링크 주소는 http(s) 만 받는다. 저장과 [미리보기]가 같은 규칙을 쓴다.
        /// </summary>
        private static NoticeContent Normalize(ParentNotice input)
        {
            CheckMonth(input.Month);

            // 주차는 날짜 순서로 매긴다 (중간에 줄을 더해도 표가 뒤섞이지 않게)
            var weeks = input.Weeks
                .OrderBy(w => w.Date.Date)
                .Select((w, i) => new ParentNoticeWeek
                {
                    Date = w.Date.Date,
                    Scripture = Clean(w.Scripture),
                    SermonTitle = Clean(w.SermonTitle),
                    Activity = Clean(w.Activity),
                    Offering = Clean(w.Offering),
                    Prayer = Clean(w.Prayer),
                    SortOrder = i
                }).ToList();

            var sections = input.Sections
                .Select(s => (Title: (s.Title ?? "").Trim(), Body: RichBody(s.Body, "안내 글이 너무 깁니다. 꼭지를 나눠 주세요.")))
                .Where(s => s.Title.Length > 0 || s.Body != null)
                .Select((s, i) => new ParentNoticeSection { Title = s.Title, Body = s.Body, SortOrder = i })
                .ToList();

            var links = new List<ParentNoticeLink>();
            foreach (var l in input.Links.Where(l => !string.IsNullOrWhiteSpace(l.Title) || !string.IsNullOrWhiteSpace(l.Url)))
            {
                if (!SafeUrl.IsHttp(l.Url))
                    throw new PortalValidationException(
                        $"사진 링크 주소는 https:// 로 시작해야 합니다: {l.Url}\n구글포토 앨범에서 [공유] → [링크 복사]한 주소를 붙여 넣어 주세요.");
                links.Add(new ParentNoticeLink
                {
                    Title = string.IsNullOrWhiteSpace(l.Title) ? "사진 보기" : l.Title.Trim(),
                    Url = l.Url.Trim(),
                    SortOrder = links.Count
                });
            }

            return new NoticeContent(
                RichBody(input.Greeting, "인사말이 너무 깁니다. 일부를 안내 꼭지로 옮겨 주세요."),
                Clean(input.ScheduleNote), weeks, sections, links);
        }

        /// <summary>인사말·안내 본문 — 서식은 허용한 것만, 빈 편집기는 빈 칸으로.</summary>
        private static string? RichBody(string? content, string tooLongMessage)
        {
            var clean = RichText.Clean(content);
            if (clean != null && clean.Length > RichText.MaxLength)
                throw new PortalValidationException(tooLongMessage);
            return clean;
        }

        private static async Task<ParentNotice> SaveContentAsync(AppDbContext db, User actor, ParentNotice input)
        {
            var content = Normalize(input);

            ParentNotice target;
            if (input.Id == 0)
            {
                int dept = EditDept(actor, input.DepartmentId);
                bool exists = await db.ParentNotices.AnyAsync(n => n.DepartmentId == dept && n.Year == input.Year && n.Month == input.Month);
                if (exists)
                    throw new PortalValidationException($"{input.Year}년 {input.Month}월 통신문이 이미 있습니다. 목록에서 다시 열어 주세요.");

                target = new ParentNotice
                {
                    DepartmentId = dept,
                    Year = input.Year,
                    Month = input.Month,
                    CreatedBy = actor.Username
                };
                db.ParentNotices.Add(target);
            }
            else
            {
                target = await db.ParentNotices
                    .Include(n => n.Weeks).Include(n => n.Sections).Include(n => n.Links)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(n => n.Id == input.Id)
                    ?? throw new PortalValidationException("통신문을 찾을 수 없습니다.");
                EditDept(actor, target.DepartmentId);   // 다른 부서 Id 를 넣어 보내도 여기서 막힌다

                db.ParentNoticeWeeks.RemoveRange(target.Weeks);
                db.ParentNoticeSections.RemoveRange(target.Sections);
                db.ParentNoticeLinks.RemoveRange(target.Links);
                target.UpdatedBy = actor.Username;
            }

            target.Greeting = content.Greeting;
            target.ScheduleNote = content.ScheduleNote;
            target.Weeks = content.Weeks;
            target.Sections = content.Sections;
            target.Links = content.Links;
            return target;
        }

        // ── 일정표 미리 채우기 ────────────────────────────────────────

        /// <summary>연간계획에서 읽는 것은 이 세 칸뿐이다.</summary>
        public record PlanEvent(DateTime Start, DateTime? End, string Title);

        private static async Task<List<ParentNoticeWeek>> BuildWeeksAsync(AppDbContext db, int dept, int year, int month)
        {
            var first = new DateTime(year, month, 1);
            var next = first.AddMonths(1);

            // 🔒 날짜와 행사명만 쿼리에 올린다. 금액(PlannedIncome/Expense)과
            //    내부 협의가 담길 수 있는 추진 내용(Description)은 읽지도 않는다.
            var rows = await db.AnnualPlans.AsNoTracking()
                .Where(p => p.DepartmentId == dept && p.EventDate < next && (p.EndDate ?? p.EventDate) >= first)
                .Select(p => new { p.EventDate, p.EndDate, p.Title })
                .ToListAsync();

            return BuildWeeks(year, month, rows.Select(r => new PlanEvent(r.EventDate, r.EndDate, r.Title)));
        }

        /// <summary>그달의 주일들.</summary>
        public static List<DateTime> SundaysOf(int year, int month)
        {
            var d = new DateTime(year, month, 1);
            d = d.AddDays(((int)DayOfWeek.Sunday - (int)d.DayOfWeek + 7) % 7);

            var result = new List<DateTime>();
            for (; d.Month == month; d = d.AddDays(7)) result.Add(d);
            return result;
        }

        /// <summary>
        /// 주일마다 한 줄. 행사는 시작일이 속한 주(주일~토요일) 줄의 '활동' 칸에 들어간다.
        /// 그달 첫 주일 전에 시작한 행사는 첫 줄로. 주일 하루짜리가 아니면 날짜를 붙인다.
        /// </summary>
        public static List<ParentNoticeWeek> BuildWeeks(int year, int month, IEnumerable<PlanEvent> events)
        {
            var sundays = SundaysOf(year, month);
            var first = new DateTime(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var labels = sundays.ToDictionary(s => s, _ => new List<string>());

            foreach (var e in events.OrderBy(e => e.Start).ThenBy(e => e.Title))
            {
                if (string.IsNullOrWhiteSpace(e.Title)) continue;

                var start = e.Start.Date;
                var end = (e.End ?? e.Start).Date;
                if (end < start) end = start;
                if (start > last || end < first) continue;

                var anchor = start < first ? first : start;
                var sunday = sundays.LastOrDefault(s => s <= anchor);
                if (sunday == default) sunday = sundays[0];

                labels[sunday].Add(ActivityLabel(e.Title.Trim(), start, end, sunday));
            }

            return sundays.Select((s, i) => new ParentNoticeWeek
            {
                Date = s,
                Activity = labels[s].Count == 0 ? null : string.Join(", ", labels[s]),
                SortOrder = i
            }).ToList();
        }

        private static string ActivityLabel(string title, DateTime start, DateTime end, DateTime sunday)
        {
            if (start == end) return start == sunday ? title : $"{title}({Md(start)})";
            return $"{title}({Md(start)}~{Md(end)})";
        }

        // "M/d" 서식은 문화권 날짜 구분자를 따라가 ko-KR 에서 "7-24" 가 된다. 직접 붙인다.
        private static string Md(DateTime d) => $"{d.Month}/{d.Day}";

        // ══════════════════════════════════════════════════════════════
        // 부서 소개
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 부서 소개를 연다. 처음이면 임원 명단(교역자 · 부장(팀장))의 이름만 채운 새 초안.
        /// 연락처는 임원 테이블에 없으므로 직접 적는다.
        /// </summary>
        public async Task<ParentPortalProfile> GetProfileAsync(User actor, int deptId)
        {
            int dept = ViewDept(actor, deptId);
            using var db = _dbFactory.CreateDbContext();

            var saved = await db.ParentPortalProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.DepartmentId == dept);
            if (saved != null) return saved;

            var today = DateTime.Today;
            var officers = await db.DepartmentOfficers.AsNoTracking()
                .Where(o => o.DepartmentId == dept)
                .ToListAsync();

            // 지금 임기 중인 사람, 없으면 가장 최근에 시작한 사람
            string? NameOf(string role) => officers
                .Where(o => o.Role == role)
                .OrderByDescending(o => o.StartDate <= today && today <= o.EndDate)
                .ThenByDescending(o => o.StartDate)
                .FirstOrDefault()?.Name;

            return new ParentPortalProfile
            {
                DepartmentId = dept,
                MinisterName = NameOf("교역자"),
                HeadName = NameOf("부장(팀장)")
            };
        }

        /// <summary>부서 소개 저장 — 저장이 곧 공개다.</summary>
        public async Task<PortalOutcome> SaveProfileAsync(User actor, ParentPortalProfile input)
        {
            int dept = EditDept(actor, input.DepartmentId);

            CheckLink(input.OpenChatUrl, "오픈채팅방 주소");
            CheckLink(input.YouTubeUrl, "유튜브 주소");
            CheckLink(input.PhotoAlbumUrl, "사진첩(구글포토) 주소");

            using var db = _dbFactory.CreateDbContext();
            var target = await db.ParentPortalProfiles.FirstOrDefaultAsync(p => p.DepartmentId == dept);
            if (target == null)
            {
                target = new ParentPortalProfile { DepartmentId = dept };
                db.ParentPortalProfiles.Add(target);
            }

            target.DisplayName = Clean(input.DisplayName);
            target.Motto = Clean(input.Motto);
            target.MottoVerse = Clean(input.MottoVerse);
            target.OpenChatUrl = Clean(input.OpenChatUrl);
            target.OpenChatCode = Clean(input.OpenChatCode);
            target.YouTubeUrl = Clean(input.YouTubeUrl);
            target.PhotoAlbumUrl = Clean(input.PhotoAlbumUrl);
            target.MinisterTitle = Clean(input.MinisterTitle) ?? "전도사";
            target.MinisterName = Clean(input.MinisterName);
            target.MinisterPhone = Clean(input.MinisterPhone);
            target.HeadName = Clean(input.HeadName);
            target.HeadPhone = Clean(input.HeadPhone);
            target.UpdatedAt = DateTime.Now;
            target.UpdatedBy = actor.Username;
            target.HomeSynced = false;
            await db.SaveChangesAsync();

            string deptName = (await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dept))?.Name ?? "";
            bool delivered = await TryHomeAsync(() => _publisher.UpsertProfileAsync(target, deptName));
            if (delivered)
            {
                target.HomeSynced = true;
                await db.SaveChangesAsync();
            }

            return new PortalOutcome
            {
                Id = dept,
                Delivered = delivered,
                Message = delivered
                    ? "저장했습니다. 학부모 화면에 반영되었습니다."
                    : "저장은 되었지만 학부모 화면에 반영하지 못했습니다. 잠시 후 다시 저장해 주세요."
            };
        }

        // ══════════════════════════════════════════════════════════════
        // 학부모 계정 (최고관리자 전용)
        // ══════════════════════════════════════════════════════════════

        public async Task<List<ParentPortalAccount>> GetAccountsAsync(User actor)
        {
            RequireAdmin(actor);
            using var db = _dbFactory.CreateDbContext();
            return await db.ParentPortalAccounts.AsNoTracking()
                .OrderBy(a => a.DepartmentId).ThenBy(a => a.Username)
                .ToListAsync();
        }

        public async Task<PortalOutcome> CreateAccountAsync(User actor, int deptId, string username, string password)
        {
            RequireAdmin(actor);
            username = (username ?? "").Trim().ToLowerInvariant();
            if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
                throw new PortalValidationException("아이디는 영문 소문자·숫자·밑줄(_)로 3~20자로 정해 주세요.");
            CheckPassword(password);

            using var db = _dbFactory.CreateDbContext();
            if (!await db.Departments.AnyAsync(d => d.Id == deptId))
                throw new PortalValidationException("부서를 찾을 수 없습니다.");
            if (await db.ParentPortalAccounts.AnyAsync(a => a.Username == username))
                throw new PortalValidationException("이미 쓰고 있는 학부모 아이디입니다.");
            // 학부모 아이디로는 재정앱에 로그인할 수 없다. 헷갈리지 않게 직원 아이디와도 겹치지 않게 한다.
            if (await db.Users.AnyAsync(u => u.Username.ToLower() == username))
                throw new PortalValidationException("직원 계정과 같은 아이디는 쓸 수 없습니다.");

            var account = new ParentPortalAccount
            {
                DepartmentId = deptId,
                Username = username,
                PasswordHash = PasswordHasher.Hash(password.Trim()),
                IsActive = true,
                UpdatedBy = actor.Username,
                HomeSynced = false
            };
            db.ParentPortalAccounts.Add(account);
            await db.SaveChangesAsync();

            return await DeliverAccountAsync(db, account, "계정을 만들었습니다.");
        }

        public async Task<PortalOutcome> ResetAccountPasswordAsync(User actor, int accountId, string newPassword)
        {
            RequireAdmin(actor);
            CheckPassword(newPassword);

            using var db = _dbFactory.CreateDbContext();
            var account = await db.ParentPortalAccounts.FirstOrDefaultAsync(a => a.Id == accountId)
                          ?? throw new PortalValidationException("계정을 찾을 수 없습니다.");

            account.PasswordHash = PasswordHasher.Hash(newPassword.Trim());
            account.UpdatedAt = DateTime.Now;
            account.UpdatedBy = actor.Username;
            account.HomeSynced = false;
            await db.SaveChangesAsync();

            return await DeliverAccountAsync(db, account, "비밀번호를 바꿨습니다.");
        }

        /// <summary>정지 · 해제. 비번이 퍼진 공용 계정을 끊는 유일한 수단이다.</summary>
        public async Task<PortalOutcome> SetAccountActiveAsync(User actor, int accountId, bool isActive)
        {
            RequireAdmin(actor);

            using var db = _dbFactory.CreateDbContext();
            var account = await db.ParentPortalAccounts.FirstOrDefaultAsync(a => a.Id == accountId)
                          ?? throw new PortalValidationException("계정을 찾을 수 없습니다.");

            account.IsActive = isActive;
            account.UpdatedAt = DateTime.Now;
            account.UpdatedBy = actor.Username;
            account.HomeSynced = false;
            await db.SaveChangesAsync();

            return await DeliverAccountAsync(db, account, isActive ? "계정을 다시 열었습니다." : "계정을 정지했습니다.");
        }

        private async Task<PortalOutcome> DeliverAccountAsync(AppDbContext db, ParentPortalAccount account, string done)
        {
            string deptName = (await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == account.DepartmentId))?.Name ?? "";
            bool delivered = await TryHomeAsync(() => _publisher.UpsertAccountAsync(account, deptName));
            if (delivered)
            {
                account.HomeSynced = true;
                await db.SaveChangesAsync();
            }

            return new PortalOutcome
            {
                Id = account.Id,
                Delivered = delivered,
                Message = delivered
                    ? $"{done} 학부모 사이트에 반영되었습니다."
                    : $"{done} 다만 학부모 사이트에 아직 반영하지 못했습니다. [전체 재동기화]를 눌러 주세요."
            };
        }

        /// <summary>단체방에 공지하기 쉬운 8자리 비밀번호 (헷갈리는 0/o·1/l/i 제외).</summary>
        public static string GeneratePassword()
        {
            const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
            return string.Create(8, chars, (span, set) =>
            {
                for (int i = 0; i < span.Length; i++) span[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 전체 재동기화
        // ══════════════════════════════════════════════════════════════

        /// <summary>[전체 재동기화] — home.db 를 church.db 의 공개분과 정확히 맞춘다.</summary>
        public Task<PortalOutcome> ResyncAllAsync(User actor)
        {
            RequireAdmin(actor);
            return ResyncAllCoreAsync();
        }

        /// <summary>
        /// 재정앱 기동 시 한 번 — home.db 를 처음 만들었거나 지난번 내보내기가 실패했어도
        /// 배포·재시작만으로 학부모 화면이 원본과 다시 맞춰진다.
        /// </summary>
        public Task<PortalOutcome> ResyncAllOnStartupAsync() => ResyncAllCoreAsync();

        private async Task<PortalOutcome> ResyncAllCoreAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            var published = await db.ParentNotices.AsNoTracking()
                .Where(n => n.IsPublished)
                .Include(n => n.Weeks).Include(n => n.Sections).Include(n => n.Links)
                .AsSplitQuery()
                .ToListAsync();
            var profiles = await db.ParentPortalProfiles.AsNoTracking().ToListAsync();
            var accounts = await db.ParentPortalAccounts.AsNoTracking().ToListAsync();
            var deptNames = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);

            bool delivered = await TryHomeAsync(() => _publisher.ReplaceAllAsync(published, profiles, accounts, deptNames));
            if (delivered)
            {
                await db.ParentNotices.ExecuteUpdateAsync(s => s.SetProperty(n => n.HomeSynced, true));
                await db.ParentPortalProfiles.ExecuteUpdateAsync(s => s.SetProperty(p => p.HomeSynced, true));
                await db.ParentPortalAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.HomeSynced, true));
            }

            return new PortalOutcome
            {
                Delivered = delivered,
                Saved = delivered,
                Message = delivered
                    ? $"통신문 {published.Count}건 · 부서 소개 {profiles.Count}건 · 학부모 계정 {accounts.Count}개를 다시 내보냈습니다."
                    : "학부모 DB에 쓰지 못했습니다. 서버의 homedata 폴더를 확인해 주세요."
            };
        }

        // ══════════════════════════════════════════════════════════════
        // 공통
        // ══════════════════════════════════════════════════════════════

        private async Task<bool> TryHomeAsync(Func<Task> export)
        {
            try
            {
                await export();
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "home.db 내보내기 실패");
                return false;
            }
        }

        private static void CheckMonth(int month)
        {
            if (month is < 1 or > 12) throw new PortalValidationException("월이 올바르지 않습니다.");
        }

        private static void CheckLink(string? url, string label)
        {
            if (!string.IsNullOrWhiteSpace(url) && !SafeUrl.IsHttp(url))
                throw new PortalValidationException($"{label}는 https:// 로 시작해야 합니다: {url}");
        }

        private static void CheckPassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 6)
                throw new PortalValidationException("비밀번호는 6자 이상으로 정해 주세요.");
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
