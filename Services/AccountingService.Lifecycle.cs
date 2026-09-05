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
    // 연간 계획 · 행사 결산/사후보고 · 주간 회의록
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // ══════════════════════════════════════════════════════════════
        // 🚀 연간 계획(AnnualPlan) — 조회 / 저장 / 삭제 + 날짜 자동계산
        //   회계연도·분기는 부서별 분기 설정을 따르는 기존 메서드
        //   (CalculateFiscalYearAsync / GetQuarterNumberAsync)를 그대로 재사용한다.
        // ══════════════════════════════════════════════════════════════

        // [DTO] 연간 계획 화면에 필요한 계산값 묶음 (날짜 → 회계연도/분기/주차)
        public class PlanDateInfo
        {
            public int FiscalYear { get; set; }
            public byte Quarter { get; set; }
            public int WeekNo { get; set; }       // 연중 주차 (1~53, 일요일 시작)
            public int MonthWeek { get; set; }    // 그 달의 몇째 주 (화면 표시용)
            public int Month { get; set; }        // 화면 표시용 월
        }

        // 행사 날짜 하나로 회계연도·분기·주차를 한 번에 계산.
        public async Task<PlanDateInfo> CalcPlanDateInfoAsync(int deptId, DateTime date)
        {
            int fy = await CalculateFiscalYearAsync(deptId, date);
            int q = await GetQuarterNumberAsync(deptId, fy, date);
            return new PlanDateInfo
            {
                FiscalYear = fy,
                Quarter = (byte)q,
                WeekNo = GetWeekOfYear(date),
                MonthWeek = GetWeekOfMonth(date),
                Month = date.Month
            };
        }

        // 연중 주차 (1월 1일부터, 일요일을 한 주의 시작으로).
        public static int GetWeekOfYear(DateTime date)
        {
            var first = new DateTime(date.Year, 1, 1);
            int firstSunOffset = (int)first.DayOfWeek; // 일=0 … 토=6
            int dayOfYear = date.DayOfYear;            // 1~366
            return (dayOfYear + firstSunOffset - 1) / 7 + 1;
        }

        // 그 달의 몇째 주 (그 달 1일이 속한 주 = 1주차, 일요일 시작).
        public static int GetWeekOfMonth(DateTime date)
        {
            var first = new DateTime(date.Year, date.Month, 1);
            int firstSunOffset = (int)first.DayOfWeek;
            return (date.Day + firstSunOffset - 1) / 7 + 1;
        }

        // 특정 부서·회계연도의 연간 계획 목록 (분기·주차 순 정렬).
        public async Task<List<AnnualPlan>> GetAnnualPlansAsync(int deptId, int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .Where(p => p.DepartmentId == deptId && p.FiscalYear == fiscalYear)
                .OrderBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 전체 부서(관리자/감사 조회용) 연간 계획 목록.
        public async Task<List<AnnualPlan>> GetAllAnnualPlansAsync(int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .Where(p => p.FiscalYear == fiscalYear)
                .OrderBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 검색용 — 특정 부서의 연간 계획 전체 연도 (최근 연도부터).
        public async Task<List<AnnualPlan>> GetAnnualPlansAllYearsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .Where(p => p.DepartmentId == deptId)
                .OrderByDescending(p => p.FiscalYear).ThenBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 검색용 — 전체 부서·전체 연도 연간 계획 (관리자 조회용).
        public async Task<List<AnnualPlan>> GetAllAnnualPlansAllYearsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.AnnualPlans
                .AsNoTracking()
                .OrderByDescending(p => p.FiscalYear).ThenBy(p => p.Quarter).ThenBy(p => p.WeekNo)
                .ToListAsync();
        }

        // 연간 계획 저장 (Id==0 신규 추가 / 그 외 수정). 감사 필드는 DbContext가 자동 처리.
        public async Task SaveAnnualPlanAsync(AnnualPlan plan, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            if (plan.Id == 0)
            {
                plan.CreatedBy = actorUsername;
                db.AnnualPlans.Add(plan);
            }
            else
            {
                var existing = await db.AnnualPlans.FirstOrDefaultAsync(p => p.Id == plan.Id);
                if (existing == null) return;
                existing.DepartmentId = plan.DepartmentId;
                existing.EventDate = plan.EventDate;
                existing.EndDate = plan.EndDate;
                existing.FiscalYear = plan.FiscalYear;
                existing.WeekNo = plan.WeekNo;
                existing.Quarter = plan.Quarter;
                existing.Title = plan.Title;
                existing.Description = plan.Description;
                existing.PlannedIncome = plan.PlannedIncome;
                existing.PlannedExpense = plan.PlannedExpense;
                existing.Status = plan.Status;
                existing.UpdatedBy = actorUsername;
            }
            await db.SaveChangesAsync();
        }

        // 연간 계획 일괄 저장 (엑셀/CSV 가져오기용). 저장된 건수를 반환한다.
        public async Task<int> AddAnnualPlansBulkAsync(List<AnnualPlan> plans, string? actorUsername)
        {
            if (plans == null || plans.Count == 0) return 0;

            using var db = _dbFactory.CreateDbContext();
            foreach (var p in plans)
            {
                p.Id = 0;                     // 신규 삽입 강제
                p.CreatedBy = actorUsername;
            }
            db.AnnualPlans.AddRange(plans);
            await db.SaveChangesAsync();
            return plans.Count;
        }

        // ══════════════════════════════════════════════════════════════
        // 🚀 행사 사후 보고서(EventReport)
        //   재정 수치는 저장하지 않고 예산·장부에서 EventName으로 매칭해 집계한다.
        // ══════════════════════════════════════════════════════════════

        /// <summary>행사 하나의 재정 집계 결과.</summary>
        public class EventFinance
        {
            public string EventName { get; set; } = "";
            public decimal Budget { get; set; }        // 편성 예산
            public decimal Spent { get; set; }         // 실제 지출
            public decimal Income { get; set; }        // 수입(회비·찬조)
            public decimal Net => Spent - Income;      // 순비용
            public decimal Diff => Budget - Spent;     // 예산 잔여(음수면 초과)
            public bool IsOver => Spent > Budget;
            public int TxCount { get; set; }           // 관련 거래 건수
            /// <summary>세부(SubCategory)별 지출 내역</summary>
            public List<(string Sub, decimal Amount)> SpentBySub { get; set; } = new();
        }

        /// <summary>
        /// 부서·회계연도의 행사별 재정을 한 번에 집계한다.
        /// 지출: Transactions.Category = 행사명 / 수입: Transactions.SubCategory = 행사명
        /// </summary>
        public async Task<List<EventFinance>> GetEventFinancesAsync(int deptId, int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();

            var budgets = await db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Year == fiscalYear
                            && (b.Type == "Expense" || b.Type == "지출"))
                .ToListAsync();

            var txs = await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId && t.FiscalYear == fiscalYear)
                .ToListAsync();

            // 행사 후보 = 예산 카테고리 ∪ 지출이 잡힌 카테고리
            var names = budgets.Select(b => b.Category)
                .Concat(txs.Where(t => t.Expense > 0).Select(t => t.Category))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim()).Distinct().ToList();

            var result = new List<EventFinance>();
            foreach (var name in names)
            {
                var exp = txs.Where(t => t.Expense > 0 && (t.Category ?? "").Trim() == name).ToList();
                var inc = txs.Where(t => t.Income > 0 && (t.SubCategory ?? "").Trim() == name).ToList();

                result.Add(new EventFinance
                {
                    EventName = name,
                    Budget = budgets.Where(b => (b.Category ?? "").Trim() == name).Sum(b => b.Amount),
                    Spent = exp.Sum(t => t.Expense),
                    Income = inc.Sum(t => t.Income),
                    TxCount = exp.Count + inc.Count,
                    SpentBySub = exp.Where(t => !string.IsNullOrWhiteSpace(t.SubCategory))
                                    .GroupBy(t => t.SubCategory!.Trim())
                                    .Select(g => (g.Key, g.Sum(x => x.Expense)))
                                    .OrderByDescending(x => x.Item2).ToList()
                });
            }

            return result.OrderByDescending(r => r.Spent).ToList();
        }

        /// <summary>부서·회계연도의 보고서 목록.</summary>
        public async Task<List<EventReport>> GetEventReportsAsync(int deptId, int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.EventReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear)
                .ToListAsync();
        }

        /// <summary>보고서 1건 조회 (없으면 null).</summary>
        public async Task<EventReport?> GetEventReportAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.EventReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        }

        /// <summary>행사명으로 보고서 조회 (미작성이면 null).</summary>
        public async Task<EventReport?> GetEventReportByNameAsync(int deptId, int fiscalYear, string eventName)
        {
            using var db = _dbFactory.CreateDbContext();
            var n = (eventName ?? "").Trim();
            return await db.EventReports.AsNoTracking()
                .FirstOrDefaultAsync(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear
                                          && r.EventName == n);
        }

        /// <summary>보고서 저장 (Id==0 신규 / 그 외 수정). 저장된 Id를 반환.</summary>
        public async Task<int> SaveEventReportAsync(EventReport report, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            if (report.Id == 0)
            {
                report.CreatedBy = actorUsername;
                db.EventReports.Add(report);
                await db.SaveChangesAsync();
                return report.Id;
            }

            var ex = await db.EventReports.FirstOrDefaultAsync(r => r.Id == report.Id);
            if (ex == null) return 0;
            ex.EventName = report.EventName;
            ex.AnnualPlanId = report.AnnualPlanId;
            ex.StartDate = report.StartDate;
            ex.EndDate = report.EndDate;
            ex.Location = report.Location;
            ex.Attendees = report.Attendees;
            ex.Organizer = report.Organizer;
            ex.Content = report.Content;
            ex.WhatWentWell = report.WhatWentWell;
            ex.WhatToImprove = report.WhatToImprove;
            ex.UpdatedBy = actorUsername;
            await db.SaveChangesAsync();
            return ex.Id;
        }

        /// <summary>보고서 소프트 삭제.</summary>
        public async Task DeleteEventReportAsync(int id, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            var r = await db.EventReports.FirstOrDefaultAsync(x => x.Id == id);
            if (r == null) return;
            r.IsDeleted = true;
            r.UpdatedBy = actorUsername;
            await db.SaveChangesAsync();
        }

        // 연간 계획 소프트 삭제 (IsDeleted = true → 글로벌 쿼리 필터로 자동 제외).
        public async Task DeleteAnnualPlanAsync(int planId, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            var plan = await db.AnnualPlans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null) return;
            plan.IsDeleted = true;
            plan.UpdatedBy = actorUsername;
            await db.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════════════
        // 🚀 주간 회의록(WeeklyMeeting) — 조회 / 저장 / 삭제
        // ══════════════════════════════════════════════════════════════

        public async Task<List<WeeklyMeeting>> GetWeeklyMeetingsAsync(int deptId, int fiscalYear)
        {
            using var db = _dbFactory.CreateDbContext();
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q4 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 4);

            return await db.WeeklyMeetings
                .AsNoTracking()
                .Include(m => m.AnnualPlan)
                .Where(m => m.DepartmentId == deptId
                            && m.MeetingDate >= q1.Start
                            && m.MeetingDate <= q4.End)
                .OrderByDescending(m => m.MeetingDate)
                .ThenBy(m => m.Id)
                .ToListAsync();
        }

        // 검색용 — 특정 부서의 회의록 전체 기간 (최근 회의부터).
        public async Task<List<WeeklyMeeting>> GetWeeklyMeetingsAllYearsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.WeeklyMeetings
                .AsNoTracking()
                .Include(m => m.AnnualPlan)
                .Where(m => m.DepartmentId == deptId)
                .OrderByDescending(m => m.MeetingDate)
                .ThenBy(m => m.Id)
                .ToListAsync();
        }

        public async Task SaveWeeklyMeetingAsync(WeeklyMeeting meeting, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            if (meeting.Id == 0)
            {
                meeting.CreatedBy = actorUsername;
                db.WeeklyMeetings.Add(meeting);
            }
            else
            {
                var existing = await db.WeeklyMeetings.FirstOrDefaultAsync(m => m.Id == meeting.Id);
                if (existing == null) return;
                existing.DepartmentId = meeting.DepartmentId;
                existing.MeetingDate = meeting.MeetingDate;
                existing.FreeMemo = meeting.FreeMemo;
                existing.EventDateTime = meeting.EventDateTime;
                existing.EventLocation = meeting.EventLocation;
                existing.ExpectedAttendees = meeting.ExpectedAttendees;
                existing.AnnualPlanId = meeting.AnnualPlanId;
                existing.UpdatedBy = actorUsername;
            }
            await db.SaveChangesAsync();
        }

        public async Task DeleteWeeklyMeetingAsync(int meetingId, string? actorUsername)
        {
            using var db = _dbFactory.CreateDbContext();
            var m = await db.WeeklyMeetings.FirstOrDefaultAsync(x => x.Id == meetingId);
            if (m == null) return;
            m.IsDeleted = true;
            m.UpdatedBy = actorUsername;
            await db.SaveChangesAsync();
        }
    }
}
