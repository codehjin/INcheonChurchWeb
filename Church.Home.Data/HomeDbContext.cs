using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Church.Home.Data
{
    /// <summary>
    /// home.db — 학부모 포털이 읽는 유일한 DB.
    ///
    /// 재정앱:   읽기+쓰기 (공개할 때 내보내고, 기동 시 마이그레이션한다)
    /// 학부모앱: Mode=ReadOnly 로만 연다. Migrate() 를 호출하지 않는다.
    ///
    /// 🔒 DbSet 은 아래 6개뿐이다. 장부·예산·영수증 테이블을 여기에 더하면
    ///    HomeSchemaTests 가 빨개진다.
    /// </summary>
    public class HomeDbContext : DbContext
    {
        public HomeDbContext(DbContextOptions<HomeDbContext> options) : base(options) { }

        public DbSet<ParentAccount> ParentAccounts { get; set; }
        public DbSet<PortalProfile> PortalProfiles { get; set; }
        public DbSet<Notice> Notices { get; set; }
        public DbSet<NoticeWeek> NoticeWeeks { get; set; }
        public DbSet<NoticeSection> NoticeSections { get; set; }
        public DbSet<NoticeLink> NoticeLinks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // 원본(church.db)의 키를 그대로 쓴다. 자동 증가를 끄지 않으면 재동기화 때 짝이 어긋난다.
            builder.Entity<ParentAccount>().Property(a => a.Id).ValueGeneratedNever();
            builder.Entity<PortalProfile>().Property(p => p.DepartmentId).ValueGeneratedNever();
            builder.Entity<Notice>().Property(n => n.Id).ValueGeneratedNever();

            builder.Entity<ParentAccount>()
                .HasIndex(a => a.Username)
                .IsUnique();

            // 부서 · 연 · 월에 통신문 한 건
            builder.Entity<Notice>()
                .HasIndex(n => new { n.DepartmentId, n.Year, n.Month })
                .IsUnique();

            // 자식은 통신문과 함께 지워진다. 비공개로 돌리면 통째로 사라져야 한다.
            builder.Entity<Notice>()
                .HasMany(n => n.Weeks).WithOne()
                .HasForeignKey(w => w.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Notice>()
                .HasMany(n => n.Sections).WithOne()
                .HasForeignKey(s => s.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Notice>()
                .HasMany(n => n.Links).WithOne()
                .HasForeignKey(l => l.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>
    /// dotnet ef 전용. 실행 중인 앱은 이 팩토리를 쓰지 않는다.
    ///   dotnet ef migrations add 이름 --project Church.Home.Data --startup-project INcheonChurchWeb.csproj --context HomeDbContext
    /// </summary>
    public class HomeDbContextDesignFactory : IDesignTimeDbContextFactory<HomeDbContext>
    {
        public HomeDbContext CreateDbContext(string[] args)
            => new(new DbContextOptionsBuilder<HomeDbContext>()
                .UseSqlite("Data Source=home.db")
                .Options);
    }
}
