using Church.Home.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>인메모리 SQLite 로 띄운 home.db. 연결을 열어 둔 동안만 살아 있다.</summary>
public sealed class HomeTestDb : IDbContextFactory<HomeDbContext>, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<HomeDbContext> _options;

    public HomeTestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(_conn).Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public HomeDbContext CreateDbContext() => new(_options);

    public void Dispose() => _conn.Dispose();
}

/// <summary>쓰기가 늘 실패하는 home.db — 서버 폴더 권한이 틀렸거나 디스크가 찬 상황.</summary>
public sealed class BrokenHomeDb : IDbContextFactory<HomeDbContext>
{
    public HomeDbContext CreateDbContext() => throw new IOException("home.db 를 열 수 없음 (테스트)");
}
