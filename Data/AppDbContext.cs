using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public DbSet<DepartmentOfficer> DepartmentOfficers { get; set; }
        public DbSet<OcrUsage> OcrUsages { get; set; }

        // 🚀 [통합 관리 시스템] 생명주기 엔티티 (연간계획 → 회의 → 기안 → 결산)
        public DbSet<AnnualPlan> AnnualPlans { get; set; }
        public DbSet<WeeklyMeeting> WeeklyMeetings { get; set; }
        public DbSet<ExpenseResolution> ExpenseResolutions { get; set; }
        public DbSet<Receipt> Receipts { get; set; }
        public DbSet<LedgerTransaction> LedgerTransactions { get; set; }

        // 🚀 행사 사후 보고서 (기존 ExpenseReport와는 별개)
        public DbSet<EventReport> EventReports { get; set; }

        // 🚀 행사 목록에서 분류를 어떻게 다룰지 (대분류 묶기 / 행사 아님 제외)
        public DbSet<EventCategorySetting> EventCategorySettings { get; set; }

        // 🚀 학부모 포털 원본 (공개분만 HomePublisher 가 home.db 로 내보낸다)
        public DbSet<ParentNotice> ParentNotices { get; set; }
        public DbSet<ParentNoticeWeek> ParentNoticeWeeks { get; set; }
        public DbSet<ParentNoticeSection> ParentNoticeSections { get; set; }
        public DbSet<ParentNoticeLink> ParentNoticeLinks { get; set; }
        public DbSet<ParentPortalProfile> ParentPortalProfiles { get; set; }
        public DbSet<ParentPortalAccount> ParentPortalAccounts { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ───────────────────────────────────────────────
            // 🚀 소프트 삭제 글로벌 쿼리 필터 (IsDeleted == true 행은 조회 제외)
            // ───────────────────────────────────────────────
            builder.Entity<AnnualPlan>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<WeeklyMeeting>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<ExpenseResolution>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<Receipt>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<LedgerTransaction>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<EventReport>().HasQueryFilter(e => !e.IsDeleted);
            builder.Entity<ParentNotice>().HasQueryFilter(e => !e.IsDeleted);

            // ───────────────────────────────────────────────
            // 🚀 ExpenseResolution 1 : 1 LedgerTransaction
            //    (승인·집행된 결의 1건 = 장부 확정 1건)
            // ───────────────────────────────────────────────
            builder.Entity<ExpenseResolution>()
                .HasOne(r => r.LedgerTransaction)
                .WithOne(t => t.ExpenseResolution)
                .HasForeignKey<LedgerTransaction>(t => t.ExpenseResolutionId)
                .OnDelete(DeleteBehavior.Restrict);

            // ───────────────────────────────────────────────
            // 1:N 관계 (허브 AnnualPlan 기준) + Cascade 충돌 방지
            // ───────────────────────────────────────────────
            builder.Entity<ExpenseResolution>()
                .HasOne(r => r.AnnualPlan)
                .WithMany(p => p.Resolutions)
                .HasForeignKey(r => r.AnnualPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WeeklyMeeting>()
                .HasOne(m => m.AnnualPlan)
                .WithMany(p => p.Meetings)
                .HasForeignKey(m => m.AnnualPlanId)
                .OnDelete(DeleteBehavior.SetNull);

            // 행사보고서 → 연간계획 (선택적). 연간계획이 지워져도 보고서 기록은 남긴다.
            builder.Entity<EventReport>()
                   .HasOne(r => r.AnnualPlan)
                   .WithMany()
                   .HasForeignKey(r => r.AnnualPlanId)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<LedgerTransaction>()
                .HasOne(t => t.AnnualPlan)
                .WithMany(p => p.Transactions)
                .HasForeignKey(t => t.AnnualPlanId)
                .OnDelete(DeleteBehavior.SetNull);

            // 🚀 장부 1건 : 영수증 N장.
            //    장부 행이 지워져도 영수증 파일과 기록은 남기고 미연결로 되돌린다.
            builder.Entity<UploadedReceipt>()
                .HasOne(r => r.LedgerEntry)
                .WithMany()
                .HasForeignKey(r => r.LedgerEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<UploadedReceipt>()
                .HasIndex(r => r.LedgerEntryId);

            builder.Entity<Receipt>()
                .HasOne(r => r.ExpenseResolution)
                .WithMany(e => e.Receipts)
                .HasForeignKey(r => r.ExpenseResolutionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ───────────────────────────────────────────────
            // 🚀 예산 항목(기존 BudgetPlan) 재사용 FK 연결 (고립 FK 방지)
            //    BudgetPlan 은 감사·이력 보존을 위해 참조 중이면 삭제 차단(Restrict).
            // ───────────────────────────────────────────────
            builder.Entity<ExpenseResolution>()
                .HasOne(r => r.BudgetPlan)
                .WithMany()
                .HasForeignKey(r => r.BudgetPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<LedgerTransaction>()
                .HasOne(t => t.BudgetPlan)
                .WithMany()
                .HasForeignKey(t => t.BudgetPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            // ───────────────────────────────────────────────
            // 🚀 작성자/승인자 → User(PK: string Username) 참조.
            //    한 테이블(User)을 두 FK 가 참조하므로 Cascade 충돌 방지를 위해 Restrict 필수.
            // ───────────────────────────────────────────────
            builder.Entity<ExpenseResolution>()
                .HasOne(r => r.Drafter)
                .WithMany()
                .HasForeignKey(r => r.DrafterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ExpenseResolution>()
                .HasOne(r => r.Approver)
                .WithMany()
                .HasForeignKey(r => r.ApproverId)
                .OnDelete(DeleteBehavior.Restrict);

            // 결의 번호 조회용 '비유니크' 인덱스.
            // (의도적으로 유니크 제약을 걸지 않음: 신규 행의 ResolutionNo 기본값이 ""이고
            //  소프트 삭제 행도 인덱스에 포함되므로, 유일성은 앱 계층 또는 추후 필터형
            //  유니크 인덱스[HasFilter("\"IsDeleted\" = 0")]로 보장하는 것이 안전하다.)
            builder.Entity<ExpenseResolution>()
                .HasIndex(r => r.ResolutionNo);

            // 부서 안에서 분류명은 설정 한 줄씩만 갖는다.
            builder.Entity<EventCategorySetting>()
                .HasIndex(s => new { s.DepartmentId, s.Name })
                .IsUnique();

            // ───────────────────────────────────────────────
            // 🚀 학부모 포털
            // ───────────────────────────────────────────────

            // 부서 · 연 · 월에 통신문 한 건. 지운 통신문(소프트 삭제)은 빼고 따진다 —
            // 지운 달에 새로 쓸 수 있어야 한다.
            builder.Entity<ParentNotice>()
                .HasIndex(n => new { n.DepartmentId, n.Year, n.Month })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = 0");

            builder.Entity<ParentNotice>()
                .HasMany(n => n.Weeks).WithOne()
                .HasForeignKey(w => w.ParentNoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ParentNotice>()
                .HasMany(n => n.Sections).WithOne()
                .HasForeignKey(s => s.ParentNoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ParentNotice>()
                .HasMany(n => n.Links).WithOne()
                .HasForeignKey(l => l.ParentNoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ParentPortalProfile>()
                .Property(p => p.DepartmentId).ValueGeneratedNever();

            builder.Entity<ParentPortalAccount>()
                .HasIndex(a => a.Username)
                .IsUnique();
        }

        // ───────────────────────────────────────────────
        // 🚀 감사 추적(Audit) 자동화: Added → CreatedAt, Modified → UpdatedAt
        // ───────────────────────────────────────────────
        public override int SaveChanges()
        {
            ApplyAuditInformation();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditInformation();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditInformation()
        {
            var now = DateTime.Now;

            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = now;
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = now;
                        // 생성 정보는 수정 시 보존
                        entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                        entry.Property(nameof(BaseEntity.CreatedBy)).IsModified = false;
                        break;
                }
            }
        }
    }
}
