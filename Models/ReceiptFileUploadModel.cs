using Microsoft.AspNetCore.Components.Forms;

namespace INcheonChurchWeb.Models
{
    public enum ReceiptFileStatus
    {
        Pending,
        Processing,
        Success,
        Failed
    }

    public class ReceiptFileUploadModel
    {
        public IBrowserFile File { get; set; } = default!;
        public string FileName { get; set; } = string.Empty;
        public ReceiptFileStatus Status { get; set; } = ReceiptFileStatus.Pending;
        public DateTime? ParsedDate { get; set; }
        public decimal ParsedAmount { get; set; }
        public string ParsedDescription { get; set; } = string.Empty;
    }
}
