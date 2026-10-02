using System.ComponentModel.DataAnnotations;

namespace INcheonChurchWeb.Models
{
    /// <summary>
    /// 🚀 행사보고서 목록에서 분류(=행사명)를 어떻게 다룰지에 대한 부서별 설정.
    ///
    /// 행사 목록은 예산·장부의 분류명에서 자동으로 만들어진다. 그런데 그중에는
    /// 운영비·환수금처럼 행사가 아닌 것도 섞이고, 여름성경학교·겨울성경학교처럼
    /// 묶어서 보면 좋은 것도 있다. 둘 다 "이 분류를 어떻게 볼 것인가"라는 한 가지
    /// 문제라 한 테이블에서 관리한다.
    ///
    /// 연도를 두지 않는다. 한 번 "운영비는 행사가 아니다"라고 정하면 해마다
    /// 다시 정할 일이 없기 때문이다.
    /// </summary>
    public class EventCategorySetting
    {
        [Key]
        public int Id { get; set; }

        public int DepartmentId { get; set; }

        /// <summary>분류명. 예산·장부의 Category 와 같은 문자열.</summary>
        public string Name { get; set; } = "";

        /// <summary>대분류. 비우면 묶지 않는다. (예: 여름성경학교·겨울성경학교 → "성경학교")</summary>
        public string? GroupName { get; set; }

        /// <summary>행사가 아니라서 목록에서 뺀 것. (예: 운영비, 환수금)</summary>
        public bool IsExcluded { get; set; }
    }
}
