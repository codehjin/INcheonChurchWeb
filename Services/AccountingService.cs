using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms; // 파일 업로드용
using Microsoft.AspNetCore.Hosting; // 경로 확인용
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing; // 리사이징용
using SixLabors.ImageSharp.Formats.Jpeg; // 압축용
using ClosedXML.Excel; // 환경설정 엑셀 내보내기
using ExcelDataReader; // 환경설정 엑셀 읽기
using System.Data; // DataTable
using System.Globalization; // 금액 문자열 고정

namespace INcheonChurchWeb.Services
{
    // [DTO] 자동분류 규칙 분석(추천 시스템)용 — 키워드별 분류 빈도
    public class CategoryFrequency
    {
        public string Category { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; } // 0~100
    }

    // [DTO] 자동분류 규칙 분석(추천 시스템)용 — 키워드 한 건의 분석 결과
    public class KeywordSuggestion
    {
        public string Keyword { get; set; } = "";          // 적요/내역 문구 (예: "파리바게트미추")
        public int TotalCount { get; set; }                 // 해당 키워드의 총 분류 건수
        public string RecommendedCategory { get; set; } = ""; // 비율이 가장 높은 추천 분류
        public double RecommendedPercentage { get; set; }   // 추천 분류의 비율(%)
        public List<CategoryFrequency> Distribution { get; set; } = new(); // 분류별 빈도(내림차순)
    }

    // [DTO] 부서 단위 장부 백업/복구용
    public class DepartmentBackupDto
    {
        public DateTime ExportDate { get; set; }
        public int DepartmentId { get; set; }
        public List<LedgerEntry> Transactions { get; set; } = new();
        public List<BudgetPlan> BudgetPlans { get; set; } = new();
        public List<CategoryMapping> CategoryMappings { get; set; } = new();
        public List<ExpenseReport> ExpenseReports { get; set; } = new();
    }

    // [DTO] 레거시 장부 → 신규 LedgerTransaction 이관 결과 리포트
    public class LedgerMigrationResult
    {
        public bool SchemaReady { get; set; }       // 신규 테이블 존재 여부(미존재 시 이관 불가)
        public int Migrated { get; set; }           // 새로 이관된 건수
        public int LinkedToBudget { get; set; }     // BudgetPlan FK 연결 성공 건수
        public int Quarantined { get; set; }        // 금액 이상치로 건너뛴 건수(양쪽 0 / 양쪽 입력 / 음수)
        public int AlreadyMigrated { get; set; }    // 멱등 가드로 스킵된 기존 이관 건수
        public string Message { get; set; } = "";   // 운영자 표시용 요약 메시지
    }

    // 🚀 1. 기존 public class 대신 public partial class 하나만 남깁니다.
    public partial class AccountingService
    {
        // 🚀 2. 정규식 생성기는 반드시 클래스의 '{' 안쪽에 위치해야 합니다.
        [GeneratedRegex(@"[\\/:*?""<>|]")]
        private static partial Regex InvalidFileNameChars();

        // 영수증 파일명에 넣을 적요 최대 길이
        private const int MaxDescLengthInFileName = 30;

        // 🚀 [동시성 안전화] Blazor Server에서 Scoped DbContext는 회로(circuit) 수명 동안
        // 살아남아 여러 비동기 작업이 같은 인스턴스를 공유 → 스레드 충돌이 발생합니다.
        // 따라서 전역 _db 주입 대신 IDbContextFactory로 각 메서드마다 짧은 수명의 컨텍스트를 생성합니다.
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IWebHostEnvironment _env;

        public AccountingService(IDbContextFactory<AppDbContext> dbFactory, IWebHostEnvironment env)
        {
            _dbFactory = dbFactory;
            _env = env;
        }
        // =========================================================
        // [1-1] 보조금 현황 계산 로직
        // =========================================================
        // 🚀 회계 달력의 기본 규칙은 Models/DateHelper.cs 한 곳에만 둔다.
        //    여기 있는 static 메서드들은 기존 호출부를 위한 위임 래퍼다.
        public static DateTime GetFourthSundayOfMonth(int year, int month)
            => DateHelper.GetFourthSundayOfMonth(year, month);

        public static DateTime GetFourthSundayOfNovember(int year)
            => DateHelper.GetFiscalYearEndDate(year);

        // 기본 분기 날짜 계산 (DB 설정이 없을 때 사용되는 규칙)
        // 규칙: 3개월마다 4주차 주일이 분기 마감일, 다음날 월요일이 새 분기 시작
        //   Q1: 전년 11월 4주차 주일 다음 월요일 ~ 2월 4주차 주일
        //   Q2: 2월 4주차 주일 다음 월요일 ~ 5월 4주차 주일
        //   Q3: 5월 4주차 주일 다음 월요일 ~ 8월 4주차 주일
        //   Q4: 8월 4주차 주일 다음 월요일 ~ 11월 4주차 주일
        public static (DateTime Start, DateTime End) GetDefaultQuarterRange(int year, int quarter)
            => DateHelper.GetDefaultQuarterRange(year, quarter);

        /// <summary>
        /// 부서의 분기 경계를 한 번만 읽어, 날짜 → (회계연도, 분기)를 계산하는 함수를 돌려준다.
        ///
        /// 은행 엑셀·장부 임포트처럼 수백 행을 한꺼번에 처리할 때 쓴다.
        /// 행마다 <see cref="GetQuarterNumberAsync"/>를 부르면 행당 DB 조회가 3~4번 나가므로,
        /// 여기서 필요한 연도의 범위를 미리 다 읽어 두고 이후는 메모리에서만 판정한다.
        ///
        /// 범위 밖 날짜(아주 오래된 데이터 등)는 기본 규칙(<see cref="DateHelper"/>)으로 떨어진다.
        /// </summary>
        public async Task<Func<DateTime, (int FiscalYear, byte Quarter)>> GetFiscalPeriodResolverAsync(
            int deptId, int fromYear, int toYear)
        {
            var windows = new List<(int FiscalYear, byte Quarter, DateTime Start, DateTime End)>();

            for (int year = fromYear; year <= toYear; year++)
            {
                for (byte quarter = 1; quarter <= 4; quarter++)
                {
                    var (start, end) = await GetQuarterDateRangeAsync(deptId, year, quarter);
                    windows.Add((year, quarter, start.Date, end.Date));
                }
            }

            return date =>
            {
                var d = date.Date;
                foreach (var w in windows)
                {
                    if (d >= w.Start && d <= w.End) return (w.FiscalYear, w.Quarter);
                }

                // 미리 읽어둔 연도 밖 — 기본 규칙으로 계산한다
                return (DateHelper.CalculateFiscalYear(d), (byte)DateHelper.GetQuarter(d));
            };
        }

        // 분기 날짜 조회: DB 수동 설정 우선, 없으면 기본 규칙 적용
        public async Task<(DateTime Start, DateTime End)> GetQuarterDateRangeAsync(int deptId, int year, int quarter)
        {
            using var db = _dbFactory.CreateDbContext();

            string key = $"Quarter_{year}_Q{quarter}";
            var setting = await db.CategoryMappings.AsNoTracking().FirstOrDefaultAsync(m => m.DepartmentId == deptId && m.Keyword == key);

            // 1. DB에 설정된 분기값이 있으면 우선 적용
            if (setting != null && setting.Category.Contains('~'))
            {
                var parts = setting.Category.Split('~');
                if (DateTime.TryParse(parts[0], out DateTime s) && DateTime.TryParse(parts[1], out DateTime e))
                {
                    return (s, e);
                }
            }

            // 2. 설정이 없을 경우 기본 규칙 적용
            return GetDefaultQuarterRange(year, quarter);
        }
        // =========================================================
        // [1-2] 분기 설정: 날짜 범위 저장 및 조회
        // =========================================================

        // isSystemAdmin == true: 특정 부서가 아닌 '모든 부서(관리자/시스템 제외)'에 동일 분기 일정을 일괄 적용.
        public async Task SaveQuarterSettingAsync(int deptId, int year, int quarter, DateTime start, DateTime end, bool isSystemAdmin = false)
        {
            using var db = _dbFactory.CreateDbContext();

            string key = $"Quarter_{year}_Q{quarter}";
            string rangeStr = $"{start:yyyy-MM-dd}~{end:yyyy-MM-dd}";

            // 적용 대상 부서: 관리자면 전체, 아니면 본인 부서만
            List<int> targetDeptIds = isSystemAdmin
                ? await db.Departments.Where(d => d.Name != "관리자" && d.Name != "시스템").Select(d => d.Id).ToListAsync()
                : new List<int> { deptId };

            foreach (var tid in targetDeptIds)
            {
                var setting = await db.CategoryMappings.FirstOrDefaultAsync(m => m.DepartmentId == tid && m.Keyword == key);
                if (setting == null)
                    db.CategoryMappings.Add(new CategoryMapping { DepartmentId = tid, Keyword = key, Category = rangeStr });
                else
                    setting.Category = rangeStr;
            }
            await db.SaveChangesAsync();
        }

        public async Task BulkUpdateCategoryAsync(List<int> ids, string newCategory)
        {
            using var db = _dbFactory.CreateDbContext();

            var targets = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync();
            foreach (var item in targets) { item.Category = newCategory; }
            await db.SaveChangesAsync();
        }

    }
}
