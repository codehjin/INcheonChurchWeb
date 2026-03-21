using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // AppModel.cs와 자동으로 합쳐집니다.
    public partial class LedgerEntry
    {
        // 🚀 기존 코드와의 호환성을 위한 '가짜' 속성 추가
        // DB 테이블에는 만들어지지 않지만, 코드상에서 .Department로 접근할 때 부서 이름을 돌려줍니다.
        [NotMapped]
        public string Department
        {
            get => DepartmentInfo?.Name ?? "";
            set { /* 읽기 전용으로 두거나 필요시 로직 추가 */ }
        }

        // 실제 DB 관계 (AppModel.cs에 이미 있다면 생략 가능하지만, 
        // 에러 방지를 위해 명시적으로 네비게이션 속성을 연결합니다)
        [ForeignKey("DepartmentId")]
        public virtual Department? DepartmentInfo { get; set; }
        public string? ReceiptUrl { get; set; }
    }
}