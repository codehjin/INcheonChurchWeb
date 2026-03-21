using Microsoft.EntityFrameworkCore;
using INcheonChurchWeb.Models;

namespace INcheonChurchWeb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // 🚀 신규 부서 테이블
        public DbSet<Department> Departments { get; set; }
        public DbSet<UploadedReceipt> UploadedReceipts { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<LedgerEntry> Transactions { get; set; }
        public DbSet<BudgetPlan> BudgetPlans { get; set; }
        public DbSet<CategoryMapping> CategoryMappings { get; set; }
        public DbSet<QuarterClose> QuarterCloses { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<DataBackup> DataBackups { get; set; }
        public DbSet<ExpenseReport> ExpenseReports { get; set; }
    }
}