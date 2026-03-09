using System.Collections.Generic;

namespace INcheonChurchWeb.Models
{
    public static class GlobalConstants
    {
        // 화면에 보여질 부서 목록 (순서 고정)
        public static readonly List<string> Departments = new List<string>
        {
            "영유아부",
            "유치부",
            "유년부", // 현재 시범 운영 중
            "초등부",
            "중고등부",
            "교회학교운영팀"
        };
    }
}