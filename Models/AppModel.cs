using System.ComponentModel.DataAnnotations;

namespace INcheonChurchWeb.Models
{
    // 사용자 (기존 유지)
    public partial class User
    {
        [Key]
        public string Username { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
        public string Department { get; set; }
    }

    // 회계 장부 (속성 중복 해결 및 partial 추가)
    public partial class LedgerEntry
    {
        public int Id { get; set; }
        public string Department { get; set; } = "";
        public DateTime Date { get; set; }
        public int FiscalYear { get; set; }
        public int Quarter { get; set; }
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public bool IsAudited { get; set; }

        // SQLite 'NOT NULL' 제약 조건 오류 방지
        public string Note { get; set; } = "";
    }

    // 예산 계획표 (partial 추가)
    public partial class BudgetPlan
    {
        public int Id { get; set; }
        public string Department { get; set; } // 부서
        public int Year { get; set; }          // 연도
        public string Type { get; set; }       // 구분 (수입/지출)
        public string Category { get; set; }   // 항 (예: 행사비)
        public string? SubCategory { get; set; } // 목 (예: 부활절 특별활동비)
        public string? CalcDetail { get; set; }  // 산출내역
        public decimal Amount { get; set; }      // 금액
    }

    // ★ 신규 추가: 자동 분류 매핑 규칙
    public partial class CategoryMapping
    {
        public int Id { get; set; }
        public string Department { get; set; } // 부서
        public string Keyword { get; set; }    // 은행 적요에 포함된 단어 (예: 다이소)
        public string Category { get; set; }   // 매핑할 항목 (예: 행사비)
    }
    public class QuarterClose
    {
        public int Id { get; set; }
        public string Department { get; set; }
        public int Year { get; set; }
        public int Quarter { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsClosed { get; set; }
    }
        public partial class LedgerEntry
    {
        // 기존 필드들...
        public string ReceiptPath { get; set; } = ""; // 영수증 파일 경로 (예: /uploads/2026-01-01_간식_빵.jpg)
    }

    // [신규] 활동 로그 (로그인, 데이터 변경 등)
    public class ActivityLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Username { get; set; } = "";
        public string Action { get; set; } = ""; // Login, Import, Delete 등
        public string Details { get; set; } = "";
    }

    // [신규] 데이터 백업 스냅샷
    public class DataBackup
    {
        public int Id { get; set; }
        public DateTime BackupDate { get; set; } = DateTime.Now;
        public string Department { get; set; } = "";
        public string Memo { get; set; } = "";
        public string DataType { get; set; } = ""; // Ledger, Budget, Mapping 등
        public string JsonData { get; set; } = ""; // 실제 데이터 직렬화본
    }
}