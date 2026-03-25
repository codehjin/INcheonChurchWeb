namespace INcheonChurchWeb.Models
{
    public class OcrUsage
    {
        public int Id { get; set; }
        public string YearMonth { get; set; } = ""; // 예: "2026-03"
        public int UsageCount { get; set; }
    }
}