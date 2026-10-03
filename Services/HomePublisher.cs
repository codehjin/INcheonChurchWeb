using Church.Home.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using Home = Church.Home.Data;

namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// 🔒 church.db → home.db 단방향 내보내기. home.db 에 쓰는 코드는 여기에만 있다.
    ///
    /// 받는 것은 학부모 포털 원본(ParentNotice·ParentPortalProfile·ParentPortalAccount)뿐이고,
    /// 옮겨 적는 칸은 아래 To... 메서드에 하나씩 적혀 있다. 장부·예산·연간계획은 받지도 않는다.
    ///
    /// 권한·부서 판단은 하지 않는다 — ParentPortalService 가 거른 뒤에 부른다.
    /// 실패하면 예외를 그대로 던진다. 호출부가 HomeSynced 를 false 로 남겨 목록에 경고를 띄운다.
    /// </summary>
    public class HomePublisher
    {
        private readonly IDbContextFactory<HomeDbContext> _homeFactory;

        public HomePublisher(IDbContextFactory<HomeDbContext> homeFactory) => _homeFactory = homeFactory;

        /// <summary>통신문 공개 — 같은 통신문(또는 같은 부서·달)의 이전 공개본을 통째로 바꾼다.</summary>
        public async Task PublishNoticeAsync(ParentNotice src)
        {
            using var home = _homeFactory.CreateDbContext();
            await using var tx = await home.Database.BeginTransactionAsync();

            // 같은 달에 Id 가 다른 공개본이 남아 있을 수 있다 (지난번 삭제의 내보내기가 실패한 경우).
            // 부서·달 유니크에 걸리지 않게 함께 지운다. 자식은 Cascade.
            await home.Notices
                .Where(n => n.Id == src.Id
                         || (n.DepartmentId == src.DepartmentId && n.Year == src.Year && n.Month == src.Month))
                .ExecuteDeleteAsync();

            home.Notices.Add(ToHome(src));
            await home.SaveChangesAsync();
            await tx.CommitAsync();
        }

        /// <summary>비공개 · 삭제 — 학부모 화면에서 내린다. 원래 없으면 아무 일도 없다.</summary>
        public async Task RemoveNoticeAsync(int noticeId)
        {
            using var home = _homeFactory.CreateDbContext();
            await home.Notices.Where(n => n.Id == noticeId).ExecuteDeleteAsync();
        }

        public async Task UpsertProfileAsync(ParentPortalProfile src, string deptName)
        {
            using var home = _homeFactory.CreateDbContext();
            var target = await home.PortalProfiles.FindAsync(src.DepartmentId);
            if (target == null)
                home.PortalProfiles.Add(ToHome(src, deptName));
            else
                home.Entry(target).CurrentValues.SetValues(ToHome(src, deptName));
            await home.SaveChangesAsync();
        }

        public async Task UpsertAccountAsync(ParentPortalAccount src, string deptName)
        {
            using var home = _homeFactory.CreateDbContext();
            var target = await home.ParentAccounts.FindAsync(src.Id);
            if (target == null)
                home.ParentAccounts.Add(ToHome(src, deptName));
            else
                home.Entry(target).CurrentValues.SetValues(ToHome(src, deptName));
            await home.SaveChangesAsync();
        }

        /// <summary>
        /// [전체 재동기화] — home.db 를 비우고 넘겨받은 것만으로 다시 채운다.
        /// home.db 는 투영본이라 깨지거나 어긋나면 이것 한 번으로 복구된다.
        /// </summary>
        public async Task ReplaceAllAsync(
            IEnumerable<ParentNotice> published,
            IEnumerable<ParentPortalProfile> profiles,
            IEnumerable<ParentPortalAccount> accounts,
            IReadOnlyDictionary<int, string> deptNames)
        {
            using var home = _homeFactory.CreateDbContext();
            await using var tx = await home.Database.BeginTransactionAsync();

            await home.Notices.ExecuteDeleteAsync();          // 자식 Cascade
            await home.PortalProfiles.ExecuteDeleteAsync();
            await home.ParentAccounts.ExecuteDeleteAsync();

            string NameOf(int deptId) => deptNames.TryGetValue(deptId, out var n) ? n : "";

            home.Notices.AddRange(published.Select(ToHome));
            home.PortalProfiles.AddRange(profiles.Select(p => ToHome(p, NameOf(p.DepartmentId))));
            home.ParentAccounts.AddRange(accounts.Select(a => ToHome(a, NameOf(a.DepartmentId))));

            await home.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // ── 옮겨 적기: 여기 적힌 칸만 학부모 쪽으로 넘어간다 ──────────────

        internal static Home.Notice ToHome(ParentNotice src) => new()
        {
            Id = src.Id,
            DepartmentId = src.DepartmentId,
            Year = src.Year,
            Month = src.Month,
            Greeting = Clean(src.Greeting),
            ScheduleNote = Clean(src.ScheduleNote),
            PublishedAt = src.PublishedAt ?? DateTime.Now,
            Weeks = src.Weeks.OrderBy(w => w.SortOrder).Select((w, i) => new NoticeWeek
            {
                Date = w.Date.Date,
                Scripture = Clean(w.Scripture),
                SermonTitle = Clean(w.SermonTitle),
                Activity = Clean(w.Activity),
                Offering = Clean(w.Offering),
                Prayer = Clean(w.Prayer),
                SortOrder = i
            }).ToList(),
            Sections = src.Sections.OrderBy(s => s.SortOrder).Select((s, i) => new NoticeSection
            {
                Title = s.Title.Trim(),
                Body = Clean(s.Body),
                SortOrder = i
            }).ToList(),
            // 저장할 때 이미 걸렀지만, 내보내는 문 앞에서 한 번 더 거른다.
            Links = src.Links.OrderBy(l => l.SortOrder)
                .Where(l => SafeUrl.IsHttp(l.Url))
                .Select((l, i) => new NoticeLink
                {
                    Title = l.Title.Trim(),
                    Url = l.Url.Trim(),
                    SortOrder = i
                }).ToList()
        };

        internal static PortalProfile ToHome(ParentPortalProfile src, string deptName) => new()
        {
            DepartmentId = src.DepartmentId,
            DeptName = deptName,
            DisplayName = Clean(src.DisplayName),
            Motto = Clean(src.Motto),
            MottoVerse = Clean(src.MottoVerse),
            OpenChatUrl = SafeUrl.OrNull(src.OpenChatUrl),
            OpenChatCode = Clean(src.OpenChatCode),
            YouTubeUrl = SafeUrl.OrNull(src.YouTubeUrl),
            PhotoAlbumUrl = SafeUrl.OrNull(src.PhotoAlbumUrl),
            MinisterTitle = Clean(src.MinisterTitle) ?? "전도사",
            MinisterName = Clean(src.MinisterName),
            MinisterPhone = Clean(src.MinisterPhone),
            HeadName = Clean(src.HeadName),
            HeadPhone = Clean(src.HeadPhone)
        };

        internal static Home.ParentAccount ToHome(ParentPortalAccount src, string deptName) => new()
        {
            Id = src.Id,
            DepartmentId = src.DepartmentId,
            DeptName = deptName,
            Username = src.Username,
            // 해시가 아니면 넘기지 않는다. 빈 값은 학부모앱의 VerifyHashed 를 절대 통과하지 못한다.
            PasswordHash = PasswordHasher.IsHashed(src.PasswordHash) ? src.PasswordHash : "",
            IsActive = src.IsActive
        };

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
