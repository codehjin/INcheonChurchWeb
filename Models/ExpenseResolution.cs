using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 지출 결의서 결재 상태
    public enum ResolutionStatus
    {
        Pending = 0,   // 대기
        Approved = 1,  // 승인
        Rejected = 2   // 반려
    }

    // 🚀 지출 결의서 (사전 기안 문서).
    // 돈을 쓰기 전 예산 한도 내에서 승인을 받는 단계. 사후 영수증과 매핑된다.
    public class ExpenseResolution : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public string ResolutionNo { get; set; } = "";  // 결의 번호 (예: 2026-Q2-001)
        public string Title { get; set; } = "";         // 결의 제목

        [Column(TypeName = "decimal(18,2)")]
        public decimal RequestedAmount { get; set; }    // 신청(예정) 금액

        public ResolutionStatus Status { get; set; } = ResolutionStatus.Pending;
        public DateTime RequestDate { get; set; }       // 기안일
        public DateTime? ApprovedDate { get; set; }     // 승인일

        // ── 허브(연간 계획) FK ───────────────────────────
        // 어떤 행사 예산에서 집행하는가 (필수)
        public int AnnualPlanId { get; set; }

        [ForeignKey(nameof(AnnualPlanId))]
        public virtual AnnualPlan? AnnualPlan { get; set; }

        // ── 예산 항목 FK (기존 BudgetPlan 재사용) ─────────
        // 기존 예산 항목 모델 BudgetPlan(PK: int Id)을 그대로 참조한다.
        // 기안 시점에 정확한 예산 라인이 미정일 수 있어 nullable 로 둔다.
        public int? BudgetPlanId { get; set; }

        [ForeignKey(nameof(BudgetPlanId))]
        public virtual BudgetPlan? BudgetPlan { get; set; }

        // ── 작성자 / 승인자 (User.Username = string PK 참조) ──
        // 기존 User 모델의 PK 는 string Username 이므로 FK 도 string? 으로 맞춘다.
        public string? DrafterId { get; set; }   // 기안자 (User.Username)

        [ForeignKey(nameof(DrafterId))]
        public virtual User? Drafter { get; set; }

        public string? ApproverId { get; set; }  // 승인자 (User.Username)

        [ForeignKey(nameof(ApproverId))]
        public virtual User? Approver { get; set; }

        // ── 하위 네비게이션 ─────────────────────────────
        public virtual ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();   // 1:N 영수증
        public virtual LedgerTransaction? LedgerTransaction { get; set; }                   // 1:1 확정 장부
    }
}
