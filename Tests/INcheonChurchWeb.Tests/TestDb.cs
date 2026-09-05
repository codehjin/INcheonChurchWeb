using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 인메모리 SQLite로 진짜 EF Core를 돌린다.
/// 연결을 열어둔 채로 유지해야 :memory: DB가 살아있다.
/// </summary>
public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        using var db = CreateDbContext();
        db.Database.EnsureCreated();

        // 장부·예산은 부서를 외래키로 참조한다. 운영 DB처럼 부서를 먼저 심는다.
        db.Departments.AddRange(
            new Department { Id = 1, Name = "영유아부" },
            new Department { Id = 2, Name = "유치부" },
            new Department { Id = 3, Name = "유년부" },
            new Department { Id = 4, Name = "초등부" },
            new Department { Id = 5, Name = "중고등부" },
            new Department { Id = 6, Name = "교회학교운영팀" },
            new Department { Id = 99, Name = "테스트부서" });
        db.SaveChanges();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _conn.Dispose();
}
