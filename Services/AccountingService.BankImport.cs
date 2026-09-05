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
    // 은행 거래내역 CSV/XLS 파싱·자동분류
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // CSV/XLS 은행 거래내역 파싱
        // =========================================================
        public async Task<List<LedgerEntry>> ParseAndClassifyBankCsvAsync(Stream fileStream, int departmentId)
        {
            using var db = _dbFactory.CreateDbContext();

            var list = new List<LedgerEntry>();
            var dbMappings = await db.CategoryMappings.AsNoTracking()
                .Where(m => m.DepartmentId == departmentId).ToListAsync();

            Encoding encoding;
            try { encoding = Encoding.GetEncoding("euc-kr"); }
            catch { encoding = Encoding.UTF8; }

            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                rawBytes = ms.ToArray();
            }

            if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                encoding = Encoding.UTF8;
            else if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
                encoding = Encoding.Unicode;

            var content = encoding.GetString(rawBytes);
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int dataStartLine = 0;
            int colDate = -1, colDesc = -1, colMemo = -1, colPerson = -1;
            int colIncome = -1, colExpense = -1, colBalance = -1;

            for (int i = 0; i < Math.Min(lines.Length, 15); i++)
            {
                var cols = SplitCsvLine(lines[i]);
                for (int c = 0; c < cols.Count; c++)
                {
                    string h = cols[c].Trim().Trim('"');
                    if (h == "거래일시" || h == "날짜") colDate = c;
                    else if (h == "적요") colDesc = c;
                    else if (h == "추가메모") colMemo = c;
                    else if (h == "의뢰인/수취인" || h == "의뢰인" || h == "수취인") colPerson = c;
                    else if (h == "입금" || h == "입금액" || h == "입금(원)") colIncome = c;
                    else if (h == "출금" || h == "출금액" || h == "출금(원)") colExpense = c;
                    else if (h == "거래후잔액" || h == "잔액") colBalance = c;
                }
                if (colDate >= 0 && colDesc >= 0) { dataStartLine = i + 1; break; }
            }

            if (colDate < 0)
            {
                colDate = 0; colDesc = 1; colMemo = 2; colPerson = 3;
                colIncome = 4; colExpense = 5; colBalance = 6;
                dataStartLine = 2;
            }

            var existingKeys = new HashSet<string>(
                (await db.Transactions.AsNoTracking()
                    .Where(t => t.DepartmentId == departmentId)
                    .Select(t => t.Date.ToString("yyyyMMddHHmm") + "_" + t.Description + "_" + t.Income + "_" + t.Expense)
                    .ToListAsync()));

            for (int i = dataStartLine; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var v = SplitCsvLine(line);
                if (v.Count <= Math.Max(colDate, Math.Max(colDesc, Math.Max(colIncome, colExpense)))) continue;

                string Clean(int idx) => idx >= 0 && idx < v.Count ? v[idx].Trim().Trim('"').Trim() : "";

                string dateStr = Clean(colDate);
                if (!DateTime.TryParse(dateStr, out DateTime date)) continue;

                string desc = Clean(colDesc);
                string memo = colMemo >= 0 ? Clean(colMemo) : "";
                string person = colPerson >= 0 ? Clean(colPerson) : "";

                decimal income = ParseMoney(Clean(colIncome));
                decimal expense = ParseMoney(Clean(colExpense));

                if (income == 0 && expense == 0) continue;

                string note = string.Join(" ", new[] { person, memo }.Where(s => !string.IsNullOrWhiteSpace(s)));
                string type = income > 0 ? "수입" : "지출";
                string searchText = $"{desc} {person} {memo}";
                string category = ClassifyTransaction(searchText, type, dbMappings);

                string key = $"{date:yyyyMMddHHmm}_{desc}_{income}_{expense}";
                if (existingKeys.Contains(key)) continue;
                existingKeys.Add(key);

                int fiscalYear = date.Year;
                if ((date.Month == 11 || date.Month == 12))
                {
                    var q4End = await GetQuarterDateRangeAsync(departmentId, date.Year, 4);
                    if (date > q4End.End) fiscalYear = date.Year + 1;
                }
                int quarter = await GetQuarterNumberAsync(departmentId, fiscalYear, date);

                list.Add(new LedgerEntry
                {
                    Date = date,
                    Description = desc,
                    Income = income,
                    Expense = expense,
                    DepartmentId = departmentId,
                    FiscalYear = fiscalYear,
                    Quarter = quarter,
                    Type = type,
                    Category = category,
                    Note = note
                });
            }
            return list;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuote = !inQuote; }
                else if (c == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
            result.Add(current.ToString());
            return result;
        }

        public async Task<int> GetQuarterNumberAsync(int deptId, int fiscalYear, DateTime date)
        {
            var q1 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 1);
            var q2 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 2);
            var q3 = await GetQuarterDateRangeAsync(deptId, fiscalYear, 3);
            if (date.Date >= q1.Start.Date && date.Date <= q1.End.Date) return 1;
            if (date.Date >= q2.Start.Date && date.Date <= q2.End.Date) return 2;
            if (date.Date >= q3.Start.Date && date.Date <= q3.End.Date) return 3;
            return 4;
        }

        private static string ClassifyTransaction(string text, string type, List<CategoryMapping> mappings)
        {
            foreach (var m in mappings)
                if (!string.IsNullOrEmpty(m.Keyword) && text.Contains(m.Keyword))
                    return m.Category;

            if (type == "수입")
            {
                if (text.Contains("주정헌금") || text.Contains("주일헌금") || text.Contains("주정힌금") || text.Contains("주정헌긍") || text.Contains("작정헌금")) return "주일헌금";
                if (text.Contains("인천중앙교회") && !text.Contains("초등부") && !text.Contains("영유아") && !text.Contains("유치")) return "교회보조금";
                if (text.Contains("후원") || text.Contains("찬조") || text.Contains("권사회") || text.Contains("집사회")) return "찬조금";
                if (text.Contains("이자") || text.Contains("이자소득") || text.Contains("예금이자")) return "은행이자";
                if (text.Contains("환급") || text.Contains("반납") || text.Contains("취소")) return "환급금";
                return "회비수입";
            }

            if (text.Contains("성경학교") || text.Contains("볼베어") || text.Contains("웅진플레이") || text.Contains("캠프") || text.Contains("트래블로버") || text.Contains("여행자보험") || text.Contains("손해보험"))
            {
                if (text.Contains("겨울") || text.Contains("12") || text.Contains("1월") || text.Contains("2월")) return "겨울성경학교";
                return "여름성경학교";
            }
            if (text.Contains("두란노") || text.Contains("세계로") || text.Contains("씨유") || text.Contains("CU") || text.Contains("이마트24") || text.Contains("킹식자재") || text.Contains("탐나는피자") || text.Contains("빵") || text.Contains("컵케익") || text.Contains("약봉투") || text.Contains("점토") || text.Contains("우리동네할인")) return "공과비";
            if (text.Contains("파리바게트") || text.Contains("명랑시대") || text.Contains("돌담옥") || text.Contains("뚝배기") || text.Contains("안스") || text.Contains("수푸드") || text.Contains("솔리드퍼퓸") || text.Contains("교사간담")) return "교사회의비";
            if (text.Contains("QT") || text.Contains("공과책") || text.Contains("훈련") || text.Contains("MT") || text.Contains("춘천") || text.Contains("삼악산") || text.Contains("이디야") || text.Contains("목향원") || text.Contains("KH에너지")) return "훈련비";
            if (text.Contains("현수막") || text.Contains("명찰") || text.Contains("마이크") || text.Contains("테이블") || text.Contains("바구니") || text.Contains("테이블보")) return "부서관리비";
            if (text.Contains("다이소") || text.Contains("아트박스") || text.Contains("크로바") || text.Contains("와글") || text.Contains("캣플") || text.Contains("롤링파스타") || text.Contains("101번지") || text.Contains("중화가정") || text.Contains("암송") || text.Contains("꽃") || text.Contains("펜던트") || text.Contains("십자가")) return "행사비";
            if (text.Contains("쿠팡") || text.Contains("네이버파이낸셜") || text.Contains("카카오페이") || text.Contains("비바리퍼블리카")) return "행사비";

            return "미분류";
        }
    }
}
