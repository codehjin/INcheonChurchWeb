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

            /// <summary>대분류. 비어 있으면 묶이지 않은 행사.</summary>
            public string? GroupName { get; set; }

            /// <summary>
            /// 회차별 재정. 보고서가 없으면 빈 목록이고, 그때는 위의 연간 합계를 쓴다.
            /// 회차가 있으면 지출은 각 회차의 기간으로 갈라 집계한다.
            /// </summary>
            public List<EventRoundFinance> Rounds { get; set; } = new();
        }

        /// <summary>한 회차의 재정. 기간이 정해지기 전에는 지출을 집계하지 않는다.</summary>
        public class EventRoundFinance
        {
            public int ReportId { get; set; }
            public int Round { get; set; }
            public string? RoundTitle { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }

            /// <summary>회차 편성예산. 보고서에 적힌 값이 없으면 분류의 연간 예산.</summary>
            public decimal Budget { get; set; }

            /// <summary>
            /// 기간 안의 실제 지출. 기간을 정하기 전에는 null —
            /// 어느 회차 몫인지 가를 수 없으므로 집계하지 않는다.
            /// </summary>
            public decimal? Spent { get; set; }

            public int TxCount { get; set; }

            public bool HasPeriod => StartDate.HasValue;
            public bool IsOver => Spent.HasValue && Spent.Value > Budget;
        }

        /// <summary>
        /// 부서·회계연도의 행사별 재정을 한 번에 집계한다.
        /// 지출: Transactions.Category = 행사명 / 수입: Transactions.SubCategory = 행사명
        /// </summary>
        public async Task<List<EventFinance>> GetEventFinancesAsync(int deptId, int fiscalYear, bool includeExcluded = false)
        {
            using var db = _dbFactory.CreateDbContext();

            var budgets = await db.BudgetPlans.AsNoTracking()
                .Where(b => b.DepartmentId == deptId && b.Year == fiscalYear
                            && (b.Type == "Expense" || b.Type == "지출"))
                .ToListAsync();

            var txs = await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId && t.FiscalYear == fiscalYear)
                .ToListAsync();

            var settings = await db.EventCategorySettings.AsNoTracking()
                .Where(s => s.DepartmentId == deptId)
                .ToListAsync();
            var settingByName = settings.ToDictionary(s => s.Name.Trim(), s => s);

            var reports = await db.EventReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear)
                .ToListAsync();

            // 행사 후보 = 예산 카테고리 ∪ 지출이 잡힌 카테고리
            var names = budgets.Select(b => b.Category)
                .Concat(txs.Where(t => t.Expense > 0).Select(t => t.Category))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim()).Distinct().ToList();

            var result = new List<EventFinance>();
            foreach (var name in names)
            {
                settingByName.TryGetValue(name, out var setting);

                // 운영비·환수금처럼 행사가 아니라고 표시해 둔 분류는 뺀다.
                if (setting?.IsExcluded == true && !includeExcluded) continue;

                var exp = txs.Where(t => t.Expense > 0 && (t.Category ?? "").Trim() == name).ToList();
                var inc = txs.Where(t => t.Income > 0 && (t.SubCategory ?? "").Trim() == name).ToList();
                decimal categoryBudget = budgets.Where(b => (b.Category ?? "").Trim() == name).Sum(b => b.Amount);

                var fin = new EventFinance
                {
                    EventName = name,
                    GroupName = string.IsNullOrWhiteSpace(setting?.GroupName) ? null : setting!.GroupName!.Trim(),
                    Budget = categoryBudget,
                    Spent = exp.Sum(t => t.Expense),
                    Income = inc.Sum(t => t.Income),
                    TxCount = exp.Count + inc.Count,
                    SpentBySub = exp.Where(t => !string.IsNullOrWhiteSpace(t.SubCategory))
                                    .GroupBy(t => t.SubCategory!.Trim())
                                    .Select(g => (g.Key, g.Sum(x => x.Expense)))
                                    .OrderByDescending(x => x.Item2).ToList()
                };

                // 회차별 재정 — 지출은 각 회차의 기간으로 갈라 집계한다.
                fin.Rounds = reports
                    .Where(r => r.EventName.Trim() == name)
                    .OrderBy(r => r.Round)
                    .Select(r => BuildRoundFinance(r, exp, categoryBudget))
                    .ToList();

                result.Add(fin);
            }

            return result.OrderByDescending(r => r.Spent).ToList();
        }

        /// <summary>
        /// 한 회차의 재정을 만든다.
        /// 기간을 정하기 전에는 어느 회차 몫인지 가를 수 없으므로 지출을 집계하지 않는다(null).
        /// </summary>
        private static EventRoundFinance BuildRoundFinance(EventReport r, List<LedgerEntry> expenses, decimal categoryBudget)
        {
            var rf = new EventRoundFinance
            {
                ReportId = r.Id,
                Round = r.Round,
                RoundTitle = r.RoundTitle,
                StartDate = r.StartDate,
                EndDate = r.EndDate,
                Budget = r.Budget ?? categoryBudget
            };

            if (!r.StartDate.HasValue) return rf;

            DateTime from = r.StartDate.Value.Date;
            DateTime to = (r.EndDate ?? r.StartDate.Value).Date;
            if (to < from) to = from;

            var inPeriod = expenses.Where(t => t.Date.Date >= from && t.Date.Date <= to).ToList();

            rf.Spent = inPeriod.Sum(t => t.Expense);
            rf.TxCount = inPeriod.Count;
            return rf;
        }

        // ── 행사 분류 설정 (대분류 묶기 / 행사 아님 제외) ──────────────

        public async Task<List<EventCategorySetting>> GetEventCategorySettingsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.EventCategorySettings.AsNoTracking()
                .Where(s => s.DepartmentId == deptId)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        /// <summary>분류 설정을 저장한다(없으면 만든다). 대분류를 비우면 묶임이 풀린다.</summary>
        public async Task SaveEventCategorySettingAsync(int deptId, string name, string? groupName, bool isExcluded)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return;

            using var db = _dbFactory.CreateDbContext();

            var row = await db.EventCategorySettings
                .FirstOrDefaultAsync(s => s.DepartmentId == deptId && s.Name == n);

            string? g = string.IsNullOrWhiteSpace(groupName) ? null : groupName!.Trim();

            if (row == null)
            {
                // 아무 설정도 아닌 상태면 굳이 행을 만들지 않는다.
                if (g == null && !isExcluded) return;

                db.EventCategorySettings.Add(new EventCategorySetting
                {
                    DepartmentId = deptId, Name = n, GroupName = g, IsExcluded = isExcluded
                });
            }
            else
            {
                row.GroupName = g;
                row.IsExcluded = isExcluded;

                // 기본값으로 돌아왔으면 행을 남기지 않는다.
                if (g == null && !isExcluded) db.EventCategorySettings.Remove(row);
            }

            await db.SaveChangesAsync();
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

        /// <summary>행사명·회차로 보고서 조회 (미작성이면 null).</summary>
        public async Task<EventReport?> GetEventReportByNameAsync(int deptId, int fiscalYear, string eventName, int round = 1)
        {
            using var db = _dbFactory.CreateDbContext();
            var n = (eventName ?? "").Trim();
            return await db.EventReports.AsNoTracking()
                .FirstOrDefaultAsync(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear
                                          && r.EventName == n && r.Round == round);
        }

        /// <summary>한 행사의 회차 목록 (1차부터 순서대로). 미작성이면 빈 목록.</summary>
        public async Task<List<EventReport>> GetEventReportRoundsAsync(int deptId, int fiscalYear, string eventName)
        {
            using var db = _dbFactory.CreateDbContext();
            var n = (eventName ?? "").Trim();
            return await db.EventReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear && r.EventName == n)
                .OrderBy(r => r.Round)
                .ToListAsync();
        }

        /// <summary>
        /// 다음 회차 보고서의 초안을 만든다(저장하지는 않는다).
        /// 편성예산은 앞 회차 값을 그대로 복사하고, 앞 회차가 없으면 분류의 연간 예산을 쓴다.
        /// 기간은 비워 둔다 — 회차마다 다르고, 정하기 전에는 지출을 집계하지 않는다.
        /// </summary>
        public async Task<EventReport> CreateNextRoundDraftAsync(int deptId, int fiscalYear, string eventName)
        {
            var n = (eventName ?? "").Trim();

            var rounds = await GetEventReportRoundsAsync(deptId, fiscalYear, n);
            var last = rounds.OrderBy(r => r.Round).LastOrDefault();

            decimal? budget = last?.Budget;
            if (budget == null)
            {
                using var db = _dbFactory.CreateDbContext();
                var amounts = await db.BudgetPlans.AsNoTracking()
                    .Where(b => b.DepartmentId == deptId && b.Year == fiscalYear
                                && (b.Type == "Expense" || b.Type == "지출")
                                && b.Category == n)
                    .Select(b => b.Amount)
                    .ToListAsync();
                if (amounts.Count > 0) budget = amounts.Sum();
            }

            return new EventReport
            {
                DepartmentId = deptId,
                FiscalYear = fiscalYear,
                EventName = n,
                Round = rounds.Count == 0 ? 1 : rounds.Max(r => r.Round) + 1,
                Budget = budget
            };
        }

        /// <summary>이 행사에서 다음에 쓸 회차 번호. 보고서가 없으면 1.</summary>
        public async Task<int> GetNextEventReportRoundAsync(int deptId, int fiscalYear, string eventName)
        {
            using var db = _dbFactory.CreateDbContext();
            var n = (eventName ?? "").Trim();
            var rounds = await db.EventReports.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.FiscalYear == fiscalYear && r.EventName == n)
                .Select(r => r.Round)
                .ToListAsync();
            return rounds.Count == 0 ? 1 : rounds.Max() + 1;
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
            ex.Round = report.Round;
            ex.RoundTitle = report.RoundTitle;
            ex.Budget = report.Budget;
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
