using System.Collections.Generic;

namespace INcheonChurchWeb.Models
{
    public static class GlobalConstants
    {
        public const long MaxSingleFileSize = 25 * 1024 * 1024;
        public const long MaxMultiFileSize = 10 * 1024 * 1024;
        public const long MaxBankImportFileSize = 10 * 1024 * 1024;
        public const long MaxLedgerImportFileSize = 20 * 1024 * 1024;

        // AI 영수증 판독 월 무료 제공량
        public const int OcrMonthlyFreeLimit = 1000;

        public const string TransactionTypeIncome = "수입";
        public const string TransactionTypeExpense = "지출";
        public const string CategoryUnclassified = "미분류";

        // 교회가 부서에 내려주는 보조금. 지출결의서의 신청 한도이자
        // 대시보드 '수입 예산 달성률'의 기준 항목이라 한 곳에서 관리한다.
        public const string CategoryChurchSubsidy = "교회보조금";

        // 화면에 보여질 부서 목록 (순서 고정)
        public static readonly List<string> Departments = new List<string>
        {
            "영유아부",
            "유치부",
            "유년부",
            "초등부",
            "중고등부",
            "교회학교운영팀"
        };
    }
}