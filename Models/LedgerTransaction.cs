using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 재정 거래 유형
    public enum LedgerTransactionType
    {
        Income = 0,   // 수입
        Expense = 1   // 지출
    }

    // 🚀 [최종 마감] 통합 재정 장부 내역 (확정 사실).
    // 기존 LedgerEntry(레거시 장부)는 호환을 위해 유지하며, 본 엔티티는
    // 결의서 → 영수증으로 이어지는 신규 생명주기의 '확정 기록' 역할을 담당한다.
    public class LedgerTransaction : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public DateTime TransactionDate { get; set; }            // 거래일
        public LedgerTransactionType Type { get; set; }          // 수입/지출

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }                      // 확정 금액

        public string? Description { get; set; }                 // 적요

        // 예산 항목 FK (기존 BudgetPlan 재사용, PK: int Id).
        // 레거시 장부 이관분 등 예산 라인이 특정되지 않을 수 있어 nullable 로 둔다.
        public int? BudgetPlanId { get; set; }

        [ForeignKey(nameof(BudgetPlanId))]
        public virtual BudgetPlan? BudgetPlan { get; set; }

        // 귀속 행사 (NULL 허용: 행사와 무관한 일반 수입/지출)
        public int? AnnualPlanId { get; set; }

        [ForeignKey(nameof(AnnualPlanId))]
        public virtual AnnualPlan? AnnualPlan { get; set; }

        // 근거 지출 결의서 (1:1, 지출 시에만 존재 → NULL 허용)
        public int? ExpenseResolutionId { get; set; }

        [ForeignKey(nameof(ExpenseResolutionId))]
        public virtual ExpenseResolution? ExpenseResolution { get; set; }
    }
}
