using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 🚀 주간 교사 회의록.
    // 자유형 메모만 작성할 수도, 특정 행사(AnnualPlan)에 연결된 개요를 담을 수도 있다.
    public class WeeklyMeeting : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        // 소속 부서. 행사와 무관한 일반 회의도 부서별로 분리 관리해야 하므로 필수.
        public int DepartmentId { get; set; }

        public DateTime MeetingDate { get; set; }  // 회의 일자
        public string? FreeMemo { get; set; }      // 자유형 메모

        // ── 행사 개요 (행사와 무관한 일반 회의면 NULL) ──
        public DateTime? EventDateTime { get; set; }    // 행사 일시
        public string? EventLocation { get; set; }      // 행사 장소
        public int? ExpectedAttendees { get; set; }     // 참석(예상) 인원

        // 연결된 연간 계획 (NULL 허용: 행사와 무관한 회의)
        public int? AnnualPlanId { get; set; }

        [ForeignKey(nameof(AnnualPlanId))]
        public virtual AnnualPlan? AnnualPlan { get; set; }
    }
}
