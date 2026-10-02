using Church.Home.Data;
using INcheonChurchWeb.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 학부모 포털 DB(home.db)의 모양을 고정한다.
///
/// 이 스키마가 '재정 데이터가 학부모 쪽으로 새지 않는다'는 보증의 바닥이다.
/// 장부·금액·교사 연락처를 담을 그릇이 애초에 없어야 한다.
/// 나중에 누가 기능을 넣다가 재정 테이블을 끌어오면 여기가 빨개진다.
/// </summary>
public class HomeSchemaTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<HomeDbContext> _options;

    public HomeSchemaTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(_conn).Options;

        using var db = Db();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private HomeDbContext Db() => new(_options);

    private static IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IProperty> 모든_속성(HomeDbContext db)
        => db.Model.GetEntityTypes().SelectMany(e => e.GetProperties());

    // ── 스키마 모양 ─────────────────────────────────────

    [Fact]
    public void home_스키마는_정해진_6개_테이블뿐이다()
    {
        using var db = Db();

        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[] { "NoticeLinks", "NoticeSections", "NoticeWeeks", "Notices", "ParentAccounts", "PortalProfiles" },
            tables);
    }

    [Fact]
    public void home_스키마에는_재정_테이블이_없다()
    {
        using var db = Db();
        var financeAssembly = typeof(AppDbContext).Assembly;

        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToList();

        Assert.DoesNotContain("Transactions", tables);
        Assert.DoesNotContain("BudgetPlans", tables);
        Assert.DoesNotContain("UploadedReceipts", tables);
        Assert.DoesNotContain("AnnualPlans", tables);
        Assert.DoesNotContain("Users", tables);
        Assert.All(db.Model.GetEntityTypes(), e => Assert.NotEqual(financeAssembly, e.ClrType.Assembly));
    }

    [Fact]
    public void home_스키마에는_금액_필드가_없다()
    {
        using var db = Db();
        var 돈_이름 = new[] { "Amount", "Income", "Expense", "Budget", "Price", "Cost" };

        foreach (var p in 모든_속성(db))
        {
            var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
            Assert.False(t == typeof(decimal) || t == typeof(double) || t == typeof(float),
                $"{p.DeclaringType.ClrType.Name}.{p.Name} 은 금액처럼 보이는 숫자형이다");
            Assert.DoesNotContain(돈_이름, w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void 연락처는_전도사와_부장_두_자리뿐이다()
    {
        using var db = Db();

        var phones = 모든_속성(db)
            .Where(p => p.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Tel", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "PortalProfile.HeadPhone", "PortalProfile.MinisterPhone" }, phones);
    }

    [Fact]
    public void home_스키마에는_공개여부_컬럼이_없다()
    {
        // 공개된 것만 내보낸다. 임시저장 글을 담을 칸이 있으면 언젠가 담긴다.
        using var db = Db();

        var names = 모든_속성(db).Select(p => p.Name).ToList();

        Assert.DoesNotContain("IsPublished", names);
        Assert.DoesNotContain("IsDeleted", names);
        Assert.DoesNotContain("IsDraft", names);
    }

    [Fact]
    public void 공유_라이브러리는_재정앱을_참조하지_않는다()
    {
        var refs = typeof(HomeDbContext).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(typeof(AppDbContext).Assembly.GetName().Name, refs);
    }

    // ── 동작 ───────────────────────────────────────────

    [Fact]
    public async Task 원본_Id를_그대로_쓴다()
    {
        using (var db = Db())
        {
            db.Notices.Add(new Notice { Id = 42, DepartmentId = 3, Year = 2026, Month = 4 });
            db.ParentAccounts.Add(new ParentAccount { Id = 7, DepartmentId = 3, Username = "youth" });
            db.PortalProfiles.Add(new PortalProfile { DepartmentId = 3, DeptName = "유년부" });
            await db.SaveChangesAsync();
        }

        using var check = Db();
        Assert.NotNull(await check.Notices.FindAsync(42));
        Assert.NotNull(await check.ParentAccounts.FindAsync(7));
        Assert.NotNull(await check.PortalProfiles.FindAsync(3));
    }

    [Fact]
    public async Task 통신문을_지우면_일정_안내_사진링크가_함께_지워진다()
    {
        using (var db = Db())
        {
            db.Notices.Add(new Notice
            {
                Id = 1, DepartmentId = 3, Year = 2026, Month = 4,
                Weeks = { new NoticeWeek { Date = DateTime.Parse("2026-04-05") } },
                Sections = { new NoticeSection { Title = "봄맞이 야외예배 안내" } },
                Links = { new NoticeLink { Title = "부활절", Url = "https://photos.app.goo.gl/x" } }
            });
            await db.SaveChangesAsync();
        }

        // 비공개로 돌릴 때처럼 자식을 불러오지 않고 통신문만 지운다 → DB 가 자식을 지워야 한다.
        using (var db = Db())
            await db.Notices.Where(n => n.Id == 1).ExecuteDeleteAsync();

        using var check = Db();
        Assert.Equal(0, await check.NoticeWeeks.CountAsync());
        Assert.Equal(0, await check.NoticeSections.CountAsync());
        Assert.Equal(0, await check.NoticeLinks.CountAsync());
    }

    [Fact]
    public async Task 같은_부서_같은_달_통신문은_한_건뿐이다()
    {
        using var db = Db();
        db.Notices.Add(new Notice { Id = 1, DepartmentId = 3, Year = 2026, Month = 4 });
        db.Notices.Add(new Notice { Id = 2, DepartmentId = 3, Year = 2026, Month = 4 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 학부모_아이디는_겹칠_수_없다()
    {
        using var db = Db();
        db.ParentAccounts.Add(new ParentAccount { Id = 1, DepartmentId = 3, Username = "youth" });
        db.ParentAccounts.Add(new ParentAccount { Id = 2, DepartmentId = 4, Username = "youth" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── 초기화 (재정앱이 기동 시 실행) ───────────────────

    [Fact]
    public void 초기화하면_마이그레이션이_적용되고_WAL이_아니다()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<HomeDbContext>().UseSqlite($"Data Source={path}").Options;

            using (var db = new HomeDbContext(options))
            {
                HomeDbInitializer.Initialize(db);
                HomeDbInitializer.Initialize(db);   // 두 번 불러도 안전해야 한다 (매 기동마다 부른다)

                Assert.Empty(db.Database.GetPendingMigrations());
            }

            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("delete", (string?)cmd.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
