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
    }
}