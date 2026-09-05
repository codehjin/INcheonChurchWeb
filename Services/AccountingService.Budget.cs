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
    // 예산 · 자동분류 규칙 · 환경설정 엑셀
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // =========================================================
        // 5. 예산 및 분류 설정
        // =========================================================
        public async Task<List<BudgetPlan>> GetBudgetPlansAsync(int deptId, int year, string type)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId && b.Year == year && b.Type == type).ToListAsync();
        }
        public async Task SaveBudgetPlanAsync(BudgetPlan plan)
        {
            if (plan == null) return;

            using var db = _dbFactory.CreateDbContext();

            if (!string.IsNullOrEmpty(plan.Type))
            {
                if (plan.Type.Equals("수입", StringComparison.OrdinalIgnoreCase)) plan.Type = "Income";
                else if (plan.Type.Equals("지출", StringComparison.OrdinalIgnoreCase)) plan.Type = "Expense";
            }

            if (plan.Id == 0) db.BudgetPlans.Add(plan);
            else { var ex = await db.BudgetPlans.FindAsync(plan.Id); if (ex != null) db.Entry(ex).CurrentValues.SetValues(plan); }
            await db.SaveChangesAsync();
        }
        public async Task DeleteBudgetPlanAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var t = await db.BudgetPlans.FindAsync(id); if (t != null) { db.BudgetPlans.Remove(t); await db.SaveChangesAsync(); }
        }

        public async Task<List<CategoryMapping>> GetMappingsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.CategoryMappings.AsNoTracking().Where(m => m.DepartmentId == deptId).ToListAsync();
        }
        public async Task SaveMappingAsync(CategoryMapping m)
        {
            using var db = _dbFactory.CreateDbContext();
            if (m.Id == 0) db.CategoryMappings.Add(m); else { var ex = await db.CategoryMappings.FindAsync(m.Id); if (ex != null) db.Entry(ex).CurrentValues.SetValues(m); } await db.SaveChangesAsync();
        }
        public async Task DeleteMappingAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var m = await db.CategoryMappings.FindAsync(id); if (m != null) { db.CategoryMappings.Remove(m); await db.SaveChangesAsync(); }
        }

        // =========================================================
        // 🚀 환경설정 엑셀 백업/복원 — 2개 시트(예산항목 / 자동분류)
        // =========================================================

        // 해당 연도 예산항목 + 자동분류 규칙을 2개 시트 엑셀로 내보냅니다.
        // isSystemAdmin == true: 전체 부서 데이터를 내보냄 / false: 해당 deptId만.
        // 모든 시트의 A열에 '부서명'을 출력합니다.
        public async Task<byte[]> ExportSettingsExcelAsync(int deptId, int year, bool isSystemAdmin)
        {
            using var db = _dbFactory.CreateDbContext();

            var budgetQuery = db.BudgetPlans.AsNoTracking().Where(b => b.Year == year);
            // 분기설정(Quarter_*) 등 시스템용 매핑은 제외하고 실제 자동분류 규칙만 내보냄
            var mappingQuery = db.CategoryMappings.AsNoTracking().Where(m => !m.Keyword.StartsWith("Quarter_"));

            if (!isSystemAdmin)
            {
                budgetQuery = budgetQuery.Where(b => b.DepartmentId == deptId);
                mappingQuery = mappingQuery.Where(m => m.DepartmentId == deptId);
            }

            var budgets = await budgetQuery.OrderBy(b => b.DepartmentId).ThenBy(b => b.Type).ThenBy(b => b.Category).ToListAsync();
            var mappings = await mappingQuery.OrderBy(m => m.DepartmentId).ThenBy(m => m.Category).ThenBy(m => m.Keyword).ToListAsync();

            var deptNames = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
            string DeptName(int id) => deptNames.TryGetValue(id, out var n) ? n : "";

            // 자동분류 구분(수입/지출)은 (부서, 항목)이 해당 부서 수입 예산 항목군에 속하는지로 추론
            var incomeKey = budgets.Where(b => b.Type == "Income" || b.Type == "수입").Select(b => (b.DepartmentId, b.Category)).ToHashSet();

            using var wb = new XLWorkbook();

            // Sheet 1: 예산항목 — 부서명 | 구분 | 항목명 | 예산금액
            var ws1 = wb.Worksheets.Add("예산항목");
            string[] h1 = { "부서명", "구분", "항목명", "예산금액" };
            for (int i = 0; i < h1.Length; i++) { ws1.Cell(1, i + 1).Value = h1[i]; ws1.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray; ws1.Cell(1, i + 1).Style.Font.Bold = true; }
            int r1 = 2;
            foreach (var b in budgets)
            {
                ws1.Cell(r1, 1).Value = DeptName(b.DepartmentId);
                ws1.Cell(r1, 2).Value = (b.Type == "Income" || b.Type == "수입") ? "수입" : "지출";
                ws1.Cell(r1, 3).Value = b.Category;
                ws1.Cell(r1, 4).Value = b.Amount;
                r1++;
            }
            ws1.Column(1).Width = 18; ws1.Column(2).Width = 10; ws1.Column(3).Width = 30; ws1.Column(4).Width = 18;

            // Sheet 2: 자동분류 — 부서명 | 구분 | 키워드 | 분류될항목
            var ws2 = wb.Worksheets.Add("자동분류");
            string[] h2 = { "부서명", "구분", "키워드", "분류될항목" };
            for (int i = 0; i < h2.Length; i++) { ws2.Cell(1, i + 1).Value = h2[i]; ws2.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray; ws2.Cell(1, i + 1).Style.Font.Bold = true; }
            int r2 = 2;
            foreach (var m in mappings)
            {
                ws2.Cell(r2, 1).Value = DeptName(m.DepartmentId);
                ws2.Cell(r2, 2).Value = incomeKey.Contains((m.DepartmentId, m.Category)) ? "수입" : "지출";
                ws2.Cell(r2, 3).Value = m.Keyword;
                ws2.Cell(r2, 4).Value = m.Category;
                r2++;
            }
            ws2.Column(1).Width = 18; ws2.Column(2).Width = 10; ws2.Column(3).Width = 40; ws2.Column(4).Width = 30;

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return stream.ToArray();
        }

        // 업로드된 엑셀(예산항목/자동분류 시트)을 파싱하여 병합/업데이트합니다.
        // A열 = 부서명. isSystemAdmin == true: A열 부서명대로 각 부서에 분배 /
        // false: 보안상 엑셀 부서명을 무시하고 무조건 currentDeptId에만 반영.
        // 반환: (예산항목 처리 건수, 자동분류 추가 건수)
        public async Task<(int budgetCount, int mappingCount)> ImportSettingsExcelAsync(int currentDeptId, int year, bool isSystemAdmin, Stream fileStream)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var db = _dbFactory.CreateDbContext();
            int budgetCount = 0, mappingCount = 0;

            // 부서명 → DepartmentId 매핑(관리자 전체 분배용)
            var depts = await db.Departments.AsNoTracking().ToListAsync();
            int ResolveDeptId(string name)
            {
                if (!isSystemAdmin) return currentDeptId; // 일반 사용자는 무조건 본인 부서로 강제(보안)
                var hit = depts.FirstOrDefault(d => d.Name.Trim() == (name ?? "").Trim());
                return hit?.Id ?? 0;
            }

            using var reader = ExcelReaderFactory.CreateReader(fileStream);
            var ds = reader.AsDataSet();

            foreach (DataTable table in ds.Tables)
            {
                if (table.TableName == "예산항목")
                {
                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        // A열=부서명, B열=구분, C열=항목명, D열=예산금액
                        string deptName = row[0]?.ToString()?.Trim() ?? "";
                        string typeStr = (table.Columns.Count > 1 ? row[1]?.ToString()?.Trim() : "") ?? "";
                        string cat = (table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : "") ?? "";
                        string amtStr = (table.Columns.Count > 3 ? row[3]?.ToString()?.Trim() : "0") ?? "0";
                        if (string.IsNullOrEmpty(cat)) continue;

                        int targetDeptId = ResolveDeptId(deptName);
                        if (targetDeptId == 0) continue; // 관리자인데 매칭되는 부서명이 없으면 스킵

                        string type = (typeStr == "수입" || typeStr == "Income") ? "Income"
                                    : (typeStr == "지출" || typeStr == "Expense") ? "Expense" : "";
                        if (type == "") continue;
                        decimal.TryParse(amtStr, out decimal amt);

                        // 동일 부서/연도/구분/항목명이면 금액 업데이트, 없으면 신규 추가(병합)
                        var existing = await db.BudgetPlans.FirstOrDefaultAsync(b => b.DepartmentId == targetDeptId && b.Year == year && b.Type == type && b.Category == cat);
                        if (existing == null) db.BudgetPlans.Add(new BudgetPlan { DepartmentId = targetDeptId, Year = year, Type = type, Category = cat, Amount = amt });
                        else existing.Amount = amt;
                        budgetCount++;
                    }
                }
                else if (table.TableName == "자동분류")
                {
                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        // A열=부서명, B열=구분(참고용), C열=키워드, D열=분류될항목
                        string deptName = row[0]?.ToString()?.Trim() ?? "";
                        string keyword = (table.Columns.Count > 2 ? row[2]?.ToString()?.Trim() : "") ?? "";
                        string cat = (table.Columns.Count > 3 ? row[3]?.ToString()?.Trim() : "") ?? "";
                        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(cat)) continue;

                        int targetDeptId = ResolveDeptId(deptName);
                        if (targetDeptId == 0) continue;

                        // 키워드 중복 방지: 동일 부서에 같은 키워드가 이미 있으면 건너뜀
                        bool exists = await db.CategoryMappings.AnyAsync(m => m.DepartmentId == targetDeptId && m.Keyword == keyword);
                        if (exists) continue;
                        db.CategoryMappings.Add(new CategoryMapping { DepartmentId = targetDeptId, Keyword = keyword, Category = cat });
                        mappingCount++;
                    }
                }
            }

            await db.SaveChangesAsync();
            return (budgetCount, mappingCount);
        }

        // =========================================================
        // 🚀 [추천 시스템] 자동분류 규칙 분석
        // 선택 기간의 '지출' 장부에서 이미 분류된 내역을 키워드(내역/비고)별로 그룹핑하여
        // 어떤 분류 항목이 가장 자주 귀속되었는지 빈도/비율을 계산하고 추천 분류를 선정합니다.
        // quarter / month 는 0이면 '전체'로 간주하여 필터를 적용하지 않습니다.
        // =========================================================
        public async Task<List<KeywordSuggestion>> AnalyzeMappingSuggestionsAsync(int deptId, int year, int quarter, int month, string type)
        {
            using var db = _dbFactory.CreateDbContext();

            // 구분(type)에 맞춰 수입/지출 장부를 선택적으로 분석 (한글/영문 표기 모두 대응)
            bool isIncome = type == "수입" || type == "Income";

            var query = db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId
                         && t.FiscalYear == year
                         && (isIncome ? (t.Type == "수입" || t.Type == "Income")
                                      : (t.Type == "지출" || t.Type == "Expense")));

            if (quarter > 0) query = query.Where(t => t.Quarter == quarter);
            if (month > 0) query = query.Where(t => t.Date.Month == month);

            var rows = await query.ToListAsync();

            // 🚀 [단어(Token) 기반 분석]
            // 텍스트는 '내역(Description)' 우선, 비어있으면 '비고(Note)'를 사용.
            // 이미 분류된 내역만 학습 대상으로 삼으므로 '미분류'와 빈 분류는 제외.
            // 각 지출 건의 텍스트를 단어로 토큰화한 뒤, (단어 → 분류) 쌍으로 1:1 평탄화(SelectMany)한다.
            // 한 건 안에서 같은 단어가 여러 번 나와도 1표만 인정하도록 건별로 Distinct 처리.
            var tokenCategoryPairs = rows
                .Select(t => new
                {
                    Text = !string.IsNullOrWhiteSpace(t.Description) ? t.Description : (t.Note ?? ""),
                    Category = (t.Category ?? "").Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "미분류")
                .SelectMany(x => Tokenize(x.Text).Distinct().Select(token => new { Token = token, x.Category }));

            var suggestions = new List<KeywordSuggestion>();

            foreach (var grp in tokenCategoryPairs.GroupBy(p => p.Token))
            {
                int total = grp.Count();

                // 🚀 [필터 완화] 1번만 등장한 단어도 모두 추천 리스트에 노출.
                // (기존 노이즈 제거: if (total < 2) continue; — 과소추출 방지를 위해 비활성화)

                var distribution = grp.GroupBy(p => p.Category)
                    .Select(cg => new CategoryFrequency
                    {
                        Category = cg.Key,
                        Count = cg.Count(),
                        Percentage = Math.Round(cg.Count() * 100.0 / total, 1)
                    })
                    .OrderByDescending(c => c.Count)
                    .ThenBy(c => c.Category)
                    .ToList();

                var top = distribution.First();

                suggestions.Add(new KeywordSuggestion
                {
                    Keyword = grp.Key,
                    TotalCount = total,
                    RecommendedCategory = top.Category,
                    RecommendedPercentage = top.Percentage,
                    Distribution = distribution
                });
            }

            // 분석 가치가 높은(자주 등장한) 단어부터 위로 정렬
            return suggestions
                .OrderByDescending(s => s.TotalCount)
                .ThenByDescending(s => s.RecommendedPercentage)
                .ToList();
        }

        // 회계 장부에서 분류 단서가 되지 못하는 불용어(Stopwords) — 토큰화 후 제외.
        private static readonly HashSet<string> _analysisStopwords = new()
        {
            "지출", "결제", "구입", "구매", "비용", "이체", "송금", "대금", "지급", "현금", "카드", "사용", "기타"
        };

        // 🚀 텍스트 정제 및 토큰화:
        // ① 한글·영문·숫자가 아닌 모든 문자(괄호·쉼표·하이픈·언더스코어 등)를 공백으로 치환
        // ② 공백 기준 분리(빈 항목 제거)
        // ③ 1글자 단어 제외 + 불용어 제외
        private static IEnumerable<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<string>();

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            return sb.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2)                       // 1글자 단어 제외
                .Where(w => !_analysisStopwords.Contains(w));     // 불용어 제외
        }
    }
}
