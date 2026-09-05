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
    // 지출결의서
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // =========================================================
        // [7] 지출결의서
        // =========================================================
        public async Task SaveExpenseReportAsync(ExpenseReport report)
        {
            using var db = _dbFactory.CreateDbContext();

            if (report.Id == 0) db.ExpenseReports.Add(report);
            else { var existing = await db.ExpenseReports.FindAsync(report.Id); if (existing != null) db.Entry(existing).CurrentValues.SetValues(report); }
            await db.SaveChangesAsync();
        }

        public async Task<List<ExpenseReport>> GetExpenseReportsAsync(int deptId, int year)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.ExpenseReports.AsNoTracking().Where(r => r.DepartmentId == deptId && r.FiscalYear == year).OrderByDescending(r => r.Date).ToListAsync();
        }

        public async Task DeleteExpenseReportAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.ExpenseReports.FindAsync(id); if (target != null) { db.ExpenseReports.Remove(target); await db.SaveChangesAsync(); }
        }
        // =========================================================
        // [신규 추가] 지출결의서 작성용 예산 및 기 신청액 통계 조회 (SQLite 호환성 패치)
        // =========================================================
        public async Task<(decimal TotalReceived, decimal TotalUsed)> GetSubsidyStatusForReportAsync(int deptId, int year)
        {
            using var db = _dbFactory.CreateDbContext();

            // 1. 총 예산(수령액): 해당 연도 수입 예산 총합 (SQLite decimal Sum 오류 방지를 위해 메모리에서 합산)
            var budgetList = await db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Year == year && (b.Type == "Income" || b.Type == "수입"))
                .Select(b => b.Amount)
                .ToListAsync();

            decimal totalBudget = budgetList.Sum();

            // 2. 기 신청액: 해당 연도에 이미 작성된 지출결의서들의 총합
            var usedList = await db.ExpenseReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == year)
                .Select(r => r.TotalAmount)
                .ToListAsync();

            decimal totalUsed = usedList.Sum();

            return (totalBudget, totalUsed);
        }
        private static decimal ParseMoney(string s) => decimal.TryParse((s ?? "").Replace(",", "").Replace("\"", "").Trim(), out decimal r) ? r : 0;

        // 수동 설정 및 11월 4째주 주일 규칙을 모두 반영하여 회계연도를 반환하는 메서드
        // 핵심: Q1 시작일(전년 11월 말)과 Q4 종료일(당년 11월)을 모두 확인하여
        //       날짜가 어느 회계연도에 속하는지 정확히 판단
        public async Task<int> CalculateFiscalYearAsync(int deptId, DateTime date)
        {
            int calendarYear = date.Year;

            // 11월·12월 데이터는 올해 회계연도(calendarYear) 소속인지,
            // 다음 해 회계연도(calendarYear+1) 소속인지 판단해야 함
            if (date.Month == 11 || date.Month == 12)
            {
                // 다음 해 회계연도의 Q1 시작일을 확인
                var q1Next = await GetQuarterDateRangeAsync(deptId, calendarYear + 1, 1);

                // 날짜가 다음 해 Q1 시작일 이후라면 → 다음 해 회계연도 소속
                if (date.Date >= q1Next.Start.Date)
                {
                    return calendarYear + 1;
                }
            }
            return calendarYear;
        }

        // 분기 설정 변경 시, 해당 부서의 관련 장부 데이터 FiscalYear/Quarter를 일괄 재계산
        // isSystemAdmin == true: 모든 부서(관리자/시스템 제외)의 장부를 각각 재계산하고 총 변경건수를 합산.
        public async Task<int> BulkRecalcFiscalYearQuarterAsync(int deptId, int fiscalYear, bool isSystemAdmin = false)
        {
            if (isSystemAdmin)
            {
                using var dbAll = _dbFactory.CreateDbContext();
                var deptIds = await dbAll.Departments.Where(d => d.Name != "관리자" && d.Name != "시스템").Select(d => d.Id).ToListAsync();
                int total = 0;
                foreach (var tid in deptIds)
                    total += await BulkRecalcFiscalYearQuarterAsync(tid, fiscalYear, false);
                return total;
            }

            using var db = _dbFactory.CreateDbContext();

            // 해당 회계연도의 전체 날짜 범위를 구함
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q4 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 4);

            // 이전 회계연도의 범위도 구함 (경계에 있는 데이터 포착용)
            var q1Prev = await GetQuarterDateRangeAsync(deptId, fiscalYear - 1, 1);
            var q4Prev = await GetQuarterDateRangeAsync(deptId, fiscalYear - 1, 4);

            // 넓은 날짜 범위로 해당 부서의 모든 관련 데이터를 한 번에 가져옴
            DateTime rangeStart = q4Prev.Start.Date < q1.Start.Date ? q4Prev.Start.Date : q1.Start.Date;
            DateTime rangeEnd = q4.End.Date.AddDays(60); // 여유 있게

            var targets = await db.Transactions
                .Where(t => t.DepartmentId == deptId && t.Date >= rangeStart && t.Date <= rangeEnd)
                .ToListAsync();

            int updatedCount = 0;
            foreach (var t in targets)
            {
                int newFiscalYear = await CalculateFiscalYearAsync(deptId, t.Date);
                int newQuarter = await GetQuarterNumberAsync(deptId, newFiscalYear, t.Date);

                if (t.FiscalYear != newFiscalYear || t.Quarter != newQuarter)
                {
                    t.FiscalYear = newFiscalYear;
                    t.Quarter = newQuarter;
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
                await db.SaveChangesAsync();

            return updatedCount;
        }
    }
}
