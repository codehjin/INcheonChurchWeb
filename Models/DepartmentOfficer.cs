using System;

namespace INcheonChurchWeb.Models
{
    public class DepartmentOfficer
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }
        public int Year { get; set; }          // 해당 연도
        public string Role { get; set; } = ""; // 직분 (교역자, 부장(팀장), 총무, 회계)
        public string Name { get; set; } = ""; // 이름
        public DateTime StartDate { get; set; } // 임기 시작일
        public DateTime EndDate { get; set; }   // 임기 종료일
    }
}