using System.Security.Cryptography;
using System.Text;
using Church.Home.Data;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Home.Services
{
    /// <summary>
    /// 로그인한 학부모의 세션. 로그인 쿠키에는 AccountId·Stamp 만 (암호화되어) 들어간다 (ParentClaims).
    /// 부서는 여기 값을 믿지 않고 매번 DB 의 계정에서 다시 읽는다.
    /// </summary>
    public sealed record ParentSession(int AccountId, string Stamp);

    /// <summary>계정이 정지되었거나 비밀번호가 바뀌어 세션이 끝났다.</summary>
    public sealed class ParentSessionEndedException : Exception
    {
        public ParentSessionEndedException() : base("다시 로그인해 주세요.") { }
    }

    public sealed record HomeView(string DeptName, PortalProfile? Profile, List<Notice> Notices);

    public sealed record NoticeView(string DeptName, PortalProfile? Profile, Notice Notice);

    /// <summary>
    /// 학부모앱이 home.db 에서 읽는 모든 것. 쓰는 메서드는 없다 (연결부터 읽기 전용).
    ///
    /// 🔒 조회 메서드는 부서·범위를 넓힐 인자를 받지 않는다. 세션의 계정을 DB 에서 다시 읽어
    ///    그 계정의 부서로만 조회한다 — 주소창이나 저장소를 고쳐 다른 부서를 볼 길이 없다.
    ///    매 조회마다 계정을 다시 확인하므로, 정지·비번 재발급은 열려 있던 화면에도 다음 조회부터 듣는다.
    /// </summary>
    public class HomeReader
    {
        /// <summary>학부모가 볼 수 있는 통신문 수 — 가장 최근 것부터.</summary>
        public const int VisibleNotices = 3;

        // 없는 아이디에도 같은 시간 동안 해시를 돌린다 — 응답 시간으로 아이디가 있는지 알아낼 수 없게.
        private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());

        private readonly IDbContextFactory<HomeDbContext> _dbFactory;

        public HomeReader(IDbContextFactory<HomeDbContext> dbFactory) => _dbFactory = dbFactory;

        // ── 로그인 · 세션 ─────────────────────────────────

        public async Task<ParentSession?> LoginAsync(string? username, string? password)
        {
            var name = (username ?? "").Trim().ToLowerInvariant();
            using var db = _dbFactory.CreateDbContext();

            var account = name.Length == 0
                ? null
                : await db.ParentAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Username == name);

            // 해시로 저장된 값만 받는다 (평문 저장값은 무조건 실패)
            bool passwordOk = PasswordHasher.VerifyHashed(password, account?.PasswordHash ?? DummyHash);

            if (account == null || !passwordOk || !account.IsActive) return null;
            return new ParentSession(account.Id, StampOf(account.PasswordHash));
        }

        /// <summary>로그인 쿠키의 세션이 아직 유효한가. 정지됐거나 비번이 바뀌었으면 null (요청마다 부른다).</summary>
        public async Task<ParentSession?> RestoreAsync(ParentSession? stored)
        {
            if (stored == null) return null;
            using var db = _dbFactory.CreateDbContext();
            return await ValidAccountAsync(db, stored) == null ? null : stored;
        }

        /// <summary>
        /// 비밀번호 해시에서 뽑은 짧은 지문. 비번을 재발급하면 해시(소금 포함)가 바뀌어
        /// 예전에 로그인해 둔 브라우저가 모두 끊긴다 — 비번이 퍼졌을 때 재발급이 실제로 듣게 한다.
        /// </summary>
        internal static string StampOf(string passwordHash)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

        private static async Task<ParentAccount?> ValidAccountAsync(HomeDbContext db, ParentSession session)
        {
            var account = await db.ParentAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == session.AccountId);
            if (account == null || !account.IsActive || !PasswordHasher.IsHashed(account.PasswordHash)) return null;

            var expected = Encoding.ASCII.GetBytes(StampOf(account.PasswordHash));
            var actual = Encoding.ASCII.GetBytes(session.Stamp ?? "");
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? account : null;
        }

        private static async Task<ParentAccount> RequireAccountAsync(HomeDbContext db, ParentSession session)
            => await ValidAccountAsync(db, session) ?? throw new ParentSessionEndedException();

        // ── 조회 ─────────────────────────────────────────

        /// <summary>첫 화면 — 내 부서의 최근 통신문(최대 3개)과 부서 소개.</summary>
        public async Task<HomeView> GetHomeAsync(ParentSession session)
        {
            using var db = _dbFactory.CreateDbContext();
            var account = await RequireAccountAsync(db, session);

            var notices = await VisibleQuery(db, account.DepartmentId).ToListAsync();
            var profile = await db.PortalProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.DepartmentId == account.DepartmentId);
            return new HomeView(account.DeptName, profile, notices);
        }

        /// <summary>
        /// 통신문 한 건. 내 부서의, 지금 목록에 보이는 것(최근 3개)만 열린다.
        /// 다른 부서 것이거나 오래된 것이면 null — 주소의 번호를 바꿔도 마찬가지다.
        /// </summary>
        public async Task<NoticeView?> GetNoticeAsync(ParentSession session, int noticeId)
        {
            using var db = _dbFactory.CreateDbContext();
            var account = await RequireAccountAsync(db, session);

            var visibleIds = await VisibleQuery(db, account.DepartmentId).Select(n => n.Id).ToListAsync();
            if (!visibleIds.Contains(noticeId)) return null;

            var notice = await db.Notices.AsNoTracking()
                .Include(n => n.Weeks.OrderBy(w => w.SortOrder))
                .Include(n => n.Sections.OrderBy(s => s.SortOrder))
                .Include(n => n.Links.OrderBy(l => l.SortOrder))
                .AsSplitQuery()
                .FirstOrDefaultAsync(n => n.Id == noticeId && n.DepartmentId == account.DepartmentId);
            if (notice == null) return null;

            var profile = await db.PortalProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.DepartmentId == account.DepartmentId);
            return new NoticeView(account.DeptName, profile, notice);
        }

        // home.db 에는 공개된 통신문만 들어 있어 공개 여부 조건이 없다.
        private static IQueryable<Notice> VisibleQuery(HomeDbContext db, int deptId)
            => db.Notices.AsNoTracking()
                .Where(n => n.DepartmentId == deptId)
                .OrderByDescending(n => n.Year).ThenByDescending(n => n.Month)
                .Take(VisibleNotices);
    }
}
