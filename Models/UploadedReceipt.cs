using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    public class UploadedReceipt
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Now.Date; // 영수증 날짜
        public string Description { get; set; } = string.Empty;        // 적요 (사용처)
        public decimal Amount { get; set; }                            // 결제 금액 추가
        public string ImagePath { get; set; } = string.Empty;          // 사진 저장 경로
        public bool IsMatched { get; set; } = false;                   // 회계담당자가 장부와 매치했는지 여부
        public DateTime UploadedAt { get; set; } = DateTime.Now;       // 업로드 한 시간

        // 🚀 연결된 장부 내역 (N:1).
        //    카드 결제는 3만원 한 건인데 영수증은 1만원짜리 3장으로 나오는 경우가 있어,
        //    한 거래에 영수증 여러 장이 붙을 수 있어야 한다.
        //    장부 행이 지워져도 영수증은 남기고 미연결로 되돌린다(SetNull).
        //    IsMatched 는 이 값과 짝을 이룬다(연결되면 true).
        public int? LedgerEntryId { get; set; }

        [ForeignKey(nameof(LedgerEntryId))]
        public virtual LedgerEntry? LedgerEntry { get; set; }

        // 한 거래에 여러 장일 때의 순서 (인쇄 제목의 3/1, 3/2, 3/3 에 쓰인다).
        public int SortOrder { get; set; } = 0;
    }
}