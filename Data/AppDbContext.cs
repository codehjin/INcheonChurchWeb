using Microsoft.EntityFrameworkCore;
using INcheonChurchWeb.Models;

namespace INcheonChurchWeb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<LedgerEntry> Transactions { get; set; }
        public DbSet<BudgetPlan> BudgetPlans { get; set; }
        public DbSet<CategoryMapping> CategoryMappings { get; set; }
        public DbSet<ExpenseReport> ExpenseReports { get; set; }

        // ★ 에러 해결: 아래 두 줄이 반드시 있어야 합니다.
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<DataBackup> DataBackups { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) { }
    }
}