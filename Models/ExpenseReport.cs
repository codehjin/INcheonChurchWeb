using System.ComponentModel.DataAnnotations;

namespace INcheonChurchWeb.Models
{
    public class ExpenseReport
    {
        [Key]
        public int Id { get; set; }
        public string Department { get; set; } = "";
        public DateTime Date { get; set; } // 작성일
        public int FiscalYear { get; set; } // 회계연도
        public string Title { get; set; } = ""; // 제목 (예: 1분기 교사 회식비)
        public decimal TotalAmount { get; set; }
        public string DetailsJson { get; set; } = ""; // 상세 내역(List<BudgetPlan>)을 JSON 문자열로 저장
    }
}