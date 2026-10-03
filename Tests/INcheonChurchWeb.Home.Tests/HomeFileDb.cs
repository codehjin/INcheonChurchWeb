using Church.Home.Data;
using INcheonChurchWeb.Home.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Home.Tests;

/// <summary>
/// 임시 파일로 만든 home.db. 쓰기 연결은 재정앱 역할(데이터 심기), 읽기 연결은 학부모앱과 똑같이
/// HomeConnection.ReadOnly 를 거친다. 읽기 전용은 :memory: 로는 시험할 수 없어 파일을 쓴다.
/// </summary>
public sealed class HomeFileDb : IDisposable
{
    private readonly string _dir;

    public string Path { get; }

    public HomeFileDb()
    {
        _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"homeapp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        Path = System.IO.Path.Combine(_dir, "home.db");

        using var db = Writer();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;");   // 서버와 같게 (HomeDbInitializer)
    }

    public string KeysDir => System.IO.Path.Combine(_dir, "keys");

    public HomeDbContext Writer()
        => new(new DbContextOptionsBuilder<HomeDbContext>().UseSqlite($"Data Source={Path};Pooling=False").Options);

    public IDbContextFactory<HomeDbContext> ReadOnly() => new Factory(HomeConnection.ReadOnly($"Data Source={Path}"));

    public HomeReader Reader() => new(ReadOnly());

    // ── 심기 ──────────────────────────────────────────

    public void Account(int id, int dept, string username, string password, bool active = true, string deptName = "유년부")
    {
        using var db = Writer();
        db.ParentAccounts.Add(new ParentAccount
        {
            Id = id, DepartmentId = dept, DeptName = deptName, Username = username,
            PasswordHash = PasswordHasher.Hash(password), IsActive = active
        });
        db.SaveChanges();
    }

    public void Notice(int id, int dept, int year, int month, string? greeting = null, Action<Notice>? fill = null)
    {
        using var db = Writer();
        var n = new Notice { Id = id, DepartmentId = dept, Year = year, Month = month, Greeting = greeting, PublishedAt = new DateTime(year, month, 1) };
        fill?.Invoke(n);
        db.Notices.Add(n);
        db.SaveChanges();
    }

    public void Change(Action<HomeDbContext> change)
    {
        using var db = Writer();
        change(db);
        db.SaveChanges();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* 임시 폴더 — 못 지워도 그만 */ }
    }

    private sealed class Factory(string connectionString) : IDbContextFactory<HomeDbContext>
    {
        public HomeDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(connectionString).Options);
    }
}
