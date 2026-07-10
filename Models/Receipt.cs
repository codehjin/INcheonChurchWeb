using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 🚀 영수증 증빙 자료.
    // 하나의 지출 결의서에 여러 장이 첨부될 수 있다 (N:1 → Resolution).
    public class Receipt : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public string ImagePath { get; set; } = "";   // 스캔/사진 경로
        public string? VendorName { get; set; }        // 거래처

        [Column(TypeName = "decimal(18,2)")]
        public decimal ActualAmount { get; set; }      // 실제 영수 금액

        public DateTime PaidDate { get; set; }         // 결제일

        // 매핑된 지출 결의서 (필수)
        public int ExpenseResolutionId { get; set; }

        [ForeignKey(nameof(ExpenseResolutionId))]
        public virtual ExpenseResolution? ExpenseResolution { get; set; }
    }
}
