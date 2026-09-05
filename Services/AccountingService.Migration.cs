using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using ClosedXML.Excel;
using ExcelDataReader;
using System.Data;
using System.Globalization;

namespace INcheonChurchWeb.Services
{
    // 1회성 레거시 장부 이관
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // 🚀 [1회성 마이그레이션] 레거시 LedgerEntry → 신규 LedgerTransaction 이관
        //   - 기존 db.Transactions(LedgerEntry) 데이터를 읽어 신규 장부 테이블로 복사/매핑한다.
        //   - ⚠️ 신규 테이블이 없으면(기존 DB는 EnsureCreated 로 생성되지 않음) 예외 대신
        //         SchemaReady=false 로 안전하게 종료한다. (스키마 적용 안내는 운영자에게 위임)
        //   - 멱등성: 소프트 삭제분까지 포함해 한 건이라도 이관됐으면 중복 생성하지 않는다.
        //   - 금액 이상치(양쪽 0 / 양쪽 입력=대체거래 / 음수)는 격리(Quarantine)하여 건너뛴다.
        //   - 구분/금액: '금액이 들어있는 컬럼'을 거래 유형의 1차 근거로 신뢰한다(Type 문자열은 보조).
        //   - (부서·연도·항목명·구분)이 일치하는 BudgetPlan 이 있으면 FK 연결(Trim+대소문자 무시), 없으면 null.
        // =========================================================
        public async Task<LedgerMigrationResult> MigrateLedgerDataAsync()
        {
            using var db = _dbFactory.CreateDbContext();

            // 0) 스키마 가드: 신규 테이블 미존재 시(기존 church.db) 즉시 안전 종료
            if (!await TableExistsAsync(db, "LedgerTransactions"))
            {
                return new LedgerMigrationResult
                {
                    SchemaReady = false,
                    Message = "LedgerTransactions 테이블이 없습니다. 신규 생명주기 테이블 스키마를 먼저 적용한 뒤 다시 실행하세요."
                };
            }

            // 1) 멱등성 가드: 소프트 삭제분까지 포함해 이미 이관된 행이 있으면 중복 방지
            int already = await db.LedgerTransactions.IgnoreQueryFilters().CountAsync();
            if (already > 0)
            {
                return new LedgerMigrationResult
                {
                    SchemaReady = true,
                    AlreadyMigrated = already,
                    Message = $"이미 {already}건이 이관되어 있어 건너뜁니다(중복 방지)."
                };
            }

            var legacy = await db.Transactions.AsNoTracking().ToListAsync();
            if (legacy.Count == 0)
                return new LedgerMigrationResult { SchemaReady = true, Message = "이관할 레거시 장부 데이터가 없습니다." };

            // BudgetPlan 매칭용 캐시 (반복 DB 조회 방지)
            var budgetPlans = await db.BudgetPlans.AsNoTracking().ToListAsync();

            int linked = 0, quarantined = 0;
            var newRows = new List<LedgerTransaction>(legacy.Count);

            foreach (var e in legacy)
            {
                // ── 1) 이상치 격리: 양쪽 0(빈 거래) / 양쪽 입력(대체·이중계상) / 음수 금액은 건너뜀 ──
                bool bothZero = e.Income == 0 && e.Expense == 0;
                bool bothSet = e.Income != 0 && e.Expense != 0;
                bool negative = e.Income < 0 || e.Expense < 0;
                if (bothZero || bothSet || negative) { quarantined++; continue; }

                // ── 2) 구분/금액 결정: 금액이 들어있는 컬럼이 곧 거래 유형(데이터 신뢰도 우선) ──
                LedgerTransactionType type;
                decimal amount;
                if (e.Income > 0) { type = LedgerTransactionType.Income; amount = e.Income; }
                else { type = LedgerTransactionType.Expense; amount = e.Expense; }

                // ── 3) BudgetPlan(예산 항목) 매칭: 부서·연도·항목명·구분 (Trim + 대소문자 무시) ──
                bool isIncome = type == LedgerTransactionType.Income;
                string cat = (e.Category ?? "").Trim();
                var plan = budgetPlans.FirstOrDefault(b =>
                    b.DepartmentId == e.DepartmentId &&
                    b.Year == e.FiscalYear &&
                    string.Equals((b.Category ?? "").Trim(), cat, StringComparison.OrdinalIgnoreCase) &&
                    (isIncome ? IsIncomeTypeString(b.Type) : IsExpenseTypeString(b.Type)));
                if (plan != null) linked++;

                // ── 4) 신규 장부 행 구성 (적요는 내역 우선, 없으면 비고 사용) ──
                string desc = !string.IsNullOrWhiteSpace(e.Description) ? e.Description : (e.Note ?? "");

                newRows.Add(new LedgerTransaction
                {
                    TransactionDate = e.Date,
                    Type = type,
                    Amount = amount,
                    Description = desc,
                    BudgetPlanId = plan?.Id,      // 매칭 실패 시 null → FK 위반 없음
                    AnnualPlanId = null,           // 레거시 데이터는 연간계획 미연결
                    ExpenseResolutionId = null,    // 결의서 미연결
                    CreatedBy = "LedgerMigration"  // CreatedAt 은 SaveChanges 오버라이드가 자동 기록
                });
            }

            db.LedgerTransactions.AddRange(newRows);
            await db.SaveChangesAsync();

            return new LedgerMigrationResult
            {
                SchemaReady = true,
                Migrated = newRows.Count,
                LinkedToBudget = linked,
                Quarantined = quarantined,
                Message = $"{newRows.Count}건 이관 완료 (예산항목 연결 {linked}건, 이상치 격리 {quarantined}건)."
            };
        }

        // 예산 항목 구분 문자열 정규화 비교 (수입/Income, 지출/Expense — 대소문자·공백 무시)
        private static bool IsIncomeTypeString(string? t)
        {
            string v = (t ?? "").Trim();
            return string.Equals(v, "수입", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Income", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsExpenseTypeString(string? t)
        {
            string v = (t ?? "").Trim();
            return string.Equals(v, "지출", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Expense", StringComparison.OrdinalIgnoreCase);
        }

        // SQLite sqlite_master 를 조회해 특정 테이블 존재 여부를 확인한다.
        // (EnsureCreated 기반 프로젝트라 신규 테이블이 기존 DB에 없을 수 있어 사전 점검에 사용)
        private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName)
        {
            var conn = db.Database.GetDbConnection();
            bool opened = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                opened = true;
            }
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                var p = cmd.CreateParameter();
                p.ParameterName = "$name";
                p.Value = tableName;
                cmd.Parameters.Add(p);
                var result = await cmd.ExecuteScalarAsync();
                return result != null && Convert.ToInt64(result) > 0;
            }
            finally
            {
                if (opened) await conn.CloseAsync();
            }
        }

        // =========================================================
    }
}
