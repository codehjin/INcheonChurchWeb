using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    // 🚀 1. 신규 추가: 부서 관리 테이블
    public class Department
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        // 🚀 부서 계좌 정보
        public string BankName { get; set; } = "";      // 은행명
        public string AccountNumber { get; set; } = ""; // 계좌번호
        public string AccountHolder { get; set; } = ""; // 예금주
    }

    // 🚀 2. 사용자 (전면 개편)
    public partial class User
    {
        [Key]
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "";

        // 신규 필드: 사용자 이름 및 활성화 상태
        public string FullName { get; set; } = "";
        public bool IsActive { get; set; } = true;

        // 부서 연결 (string -> int 교체)
        public int DepartmentId { get; set; }

        [ForeignKey("DepartmentId")]
        public virtual Department? Department { get; set; }

        // 🚀 2단계 인증(2FA/OTP) 필드
        public string? TwoFactorSecret { get; set; } // OTP용 비밀키 (QR코드 생성 시 사용)
        public bool IsTwoFactorEnabled { get; set; } = false; // 2단계 인증 활성화 여부

        // ==========================================
        // 🚀 방금 추가할 부분: 계정 역할에 따른 권한 규칙 모음
        // ==========================================
        public bool IsSystemAdmin => Role == "Admin";
        public bool IsManager => Role == "Manager" || Role == "DeptAdmin" || Role == "Admin";
        public bool IsAuditor => Role == "Auditor" || Role == "Director";

        // 장부 수정/삭제 가능 여부 (최고관리자, 부서운영자만 가능)
        public bool CanEditLedger => Role == "Admin" || Role == "Manager" || Role == "DeptAdmin";
    }

    // 🚀 3. 회계 장부
    public partial class LedgerEntry
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; } // 교체됨
        public DateTime Date { get; set; }
        public int FiscalYear { get; set; }
        public int Quarter { get; set; }
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public bool IsAudited { get; set; }
        public string Note { get; set; } = "";
        public string ReceiptPath { get; set; } = "";
    }

    // 🚀 4. 예산 계획표
    public partial class BudgetPlan
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; } // 교체됨
        public int Year { get; set; }
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string? SubCategory { get; set; }
        public string? CalcDetail { get; set; }
        public decimal Amount { get; set; }
    }

    // 🚀 5. 자동 분류 매핑 규칙
    public partial class CategoryMapping
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; } // 교체됨
        public string Keyword { get; set; } = "";
        public string Category { get; set; } = "";
    }

    // 🚀 6. 분기 마감
    public class QuarterClose
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; } // 교체됨
        public int Year { get; set; }
        public int Quarter { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsClosed { get; set; }
    }

    // 🚀 7. 활동 로그 (이건 로그인 아이디 기준이므로 그대로 유지)
    public class ActivityLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
    }

    // 🚀 8. 데이터 백업 스냅샷
    public class DataBackup
    {
        public int Id { get; set; }
        public DateTime BackupDate { get; set; } = DateTime.Now;
        public int DepartmentId { get; set; } // 교체됨
        public string Memo { get; set; } = "";
        public string DataType { get; set; } = "";
        public string JsonData { get; set; } = "";
    }

    // 🚀 9. 시스템 전체 백업 데이터용 DTO (Settings.razor 에러 방지용)
    public class SystemBackupDto
    {
        public DateTime ExportDate { get; set; }
        public List<Department> Departments { get; set; } = new();
        public List<User> Users { get; set; } = new();
        public List<LedgerEntry> Transactions { get; set; } = new();
        public List<BudgetPlan> BudgetPlans { get; set; } = new();
        public List<CategoryMapping> CategoryMappings { get; set; } = new();
    }
}