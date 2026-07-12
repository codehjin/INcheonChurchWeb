using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 연간 계획 진행 상태
    public enum PlanStatus
    {
        Planned = 0,    // 계획
        InProgress = 1, // 진행
        Done = 2,       // 완료
        Closed = 3      // 마감
    }

    // 🚀 [생명주기 허브] 연간 주차별 행사 계획 및 목표 예산.
    // 회의록·지출결의·재정장부가 모두 이 엔티티를 참조한다.
    public class AnnualPlan : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        // 소속 부서. 부서별로 계획을 분리 관리한다.
        public int DepartmentId { get; set; }

        // 행사 날짜. 이 날짜로부터 회계연도·분기·주차를 자동 계산한다.
        public DateTime EventDate { get; set; }

        public int FiscalYear { get; set; }   // 회계연도
        public int WeekNo { get; set; }       // 주차 (1~52)
        public byte Quarter { get; set; }     // 분기 (1~4) - 분기 보고서 집계 기준

        public string Title { get; set; } = "";  // 행사명
        public string? Description { get; set; }  // 추진 내용

        [Column(TypeName = "decimal(18,2)")]
        public decimal PlannedIncome { get; set; }   // 목표 수입 한도

        [Column(TypeName = "decimal(18,2)")]
        public decimal PlannedExpense { get; set; }  // 편성 지출 한도

        public PlanStatus Status { get; set; } = PlanStatus.Planned;

        // ── 네비게이션 (1:N) ──────────────────────────────
        public virtual ICollection<WeeklyMeeting> Meetings { get; set; } = new List<WeeklyMeeting>();
        public virtual ICollection<ExpenseResolution> Resolutions { get; set; } = new List<ExpenseResolution>();
        public virtual ICollection<LedgerTransaction> Transactions { get; set; } = new List<LedgerTransaction>();
    }
}
