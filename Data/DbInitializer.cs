using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace INcheonChurchWeb.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            context.Database.EnsureCreated();

            // 0. 🚀 인코딩 손상 찌꺼기 1회성 정화 보정
            // (부서명/계좌 정보에 끼어든 연속된 물음표·유니코드 대체문자 등을 제거)
            CleanCorruptedDepartmentData(context);

            // 1. 기본 부서 데이터 세팅
            if (!context.Departments.Any())
            {
                var departments = new Department[]
                {
                    new Department { Name = "영유아부" }, new Department { Name = "유치부" },
                    new Department { Name = "유년부" }, new Department { Name = "초등부" },
                    new Department { Name = "중고등부" }, new Department { Name = "교회학교 운영팀" },
                    new Department { Name = "관리자" }
                };
                context.Departments.AddRange(departments);
                context.SaveChanges();
            }

            // 2. 🚀 모든 기본 계정 일괄 생성
            if (!context.Users.Any())
            {
                var depts = context.Departments.ToList();
                int GetDeptId(string name) => depts.FirstOrDefault(d => d.Name == name)?.Id ?? 1;

                var defaultUsers = new User[]
                {
                    new User { Username = "admin", Password = "1234", Role = "Admin", FullName = "최고 관리자", DepartmentId = GetDeptId("관리자") },
                    new User { Username = "manager", Password = "1234", Role = "User", FullName = "운영팀장", DepartmentId = GetDeptId("교회학교 운영팀") },
                    new User { Username = "child", Password = "1234", Role = "User", FullName = "유년부 회계", DepartmentId = GetDeptId("유년부") },
                    new User { Username = "infant", Password = "1234", Role = "User", FullName = "영유아부 회계", DepartmentId = GetDeptId("영유아부") },
                    new User { Username = "kinder", Password = "1234", Role = "User", FullName = "유치부 회계", DepartmentId = GetDeptId("유치부") },
                    new User { Username = "elementary", Password = "1234", Role = "User", FullName = "초등부 회계", DepartmentId = GetDeptId("초등부") },
                    new User { Username = "middle", Password = "1234", Role = "User", FullName = "중고등부 회계", DepartmentId = GetDeptId("중고등부") }
                };
                context.Users.AddRange(defaultUsers);
                context.SaveChanges();
            }

            // 3. 예산 데이터 (유년부 예시 - 생략 없이 기존 코드 유지)
            if (!context.BudgetPlans.Any())
            {
                var youthDept = context.Departments.FirstOrDefault(d => d.Name == "유년부");
                if (youthDept != null)
                {
                    // Department="유년부" 였던 부분을 전부 DepartmentId = youthDept.Id 로 수정했습니다.
                    var budget2026 = new BudgetPlan[]
                {
                    // === 수입 (Income) ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Income", Category="교회보조금", CalcDetail="기본 보조", Amount=7800000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Income", Category="주일헌금", CalcDetail="매주 헌금 예상", Amount=1500000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Income", Category="찬조금", CalcDetail="특별 찬조", Amount=3239000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Income", Category="회비수입", CalcDetail="수련회비 등", Amount=1500000 },

                    // === 지출 (Expense) - 겨울성경학교 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="겨울성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="겨울성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="겨울성경학교", SubCategory="예비비", CalcDetail="보조교사 선물 등", Amount=200000 },

                    // === 지출 - 여름성경학교 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="여름성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="여름성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="여름성경학교", SubCategory="외부 물놀이", CalcDetail="30,000원*38명", Amount=1140000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="여름성경학교", SubCategory="예비비", CalcDetail="기타 진행비", Amount=200000 },

                    // === 지출 - 행사비 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="생일축하행사", CalcDetail="10,000원*38명", Amount=380000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="성탄절준비비", CalcDetail="20,000원*25명", Amount=500000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="성탄절선물", CalcDetail="10,000원*38명", Amount=380000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="졸업선물", CalcDetail="20,000원*8명", Amount=160000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="부활절 특별활동", CalcDetail="계란 등", Amount=300000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="야외예배", CalcDetail="10,000원*38명*2회", Amount=760000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="전도비", CalcDetail="새친구 선물 등", Amount=200000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="반데이트", CalcDetail="20,000원*38명*2회", Amount=1520000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="행사비", SubCategory="달란트행사", CalcDetail="20,000원*25명*2회", Amount=1000000 },

                    // === 지출 - 훈련비 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="훈련비", SubCategory="공과교재", CalcDetail="4,500원*35명*2회", Amount=315000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="훈련비", SubCategory="교사훈련(MT)", CalcDetail="30,000원*13명*2회", Amount=780000 },

                    // === 지출 - 부서관리비 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="부서관리비", SubCategory="물품구입", CalcDetail="가방, 명찰, 앞치마", Amount=100000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="부서관리비", SubCategory="환경미화", CalcDetail="현수막 및 데코", Amount=100000 },

                    // === 지출 - 교사회의비 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="교사회의비", SubCategory="교사회식", CalcDetail="10,000원*13명*4분기", Amount=520000 },
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="교사회의비", SubCategory="교사간식", CalcDetail="3,000원*13명*12달", Amount=468000 },

                    // === 지출 - 공과비 ===
                    new BudgetPlan { Year=2026, DepartmentId=youthDept.Id, Type="Expense", Category="공과비", SubCategory="주간공과", CalcDetail="2,000원*25명*52주", Amount=2600000 },
                };

                    context.BudgetPlans.AddRange(budget2026);
                    context.SaveChanges();
                }
            }
        }

        // 🚀 부서명(Name)과 계좌 정보(은행명/계좌번호/예금주)에 남은 인코딩 찌꺼기를 정리합니다.
        // 예) "????? 영유아부" -> "영유아부"
        private static void CleanCorruptedDepartmentData(AppDbContext context)
        {
            // 부서 테이블이 아직 없으면(최초 생성 직후) 정리할 데이터도 없으므로 건너뜀
            if (!context.Departments.Any()) return;

            bool changed = false;
            foreach (var dept in context.Departments)
            {
                string name = CleanGarbage(dept.Name);
                string bank = CleanGarbage(dept.BankName);
                string accNo = CleanGarbage(dept.AccountNumber);
                string holder = CleanGarbage(dept.AccountHolder);

                if (name != dept.Name || bank != dept.BankName || accNo != dept.AccountNumber || holder != dept.AccountHolder)
                {
                    dept.Name = name;
                    dept.BankName = bank;
                    dept.AccountNumber = accNo;
                    dept.AccountHolder = holder;
                    changed = true;
                }
            }

            if (changed) context.SaveChanges();
        }

        // 문자열 앞뒤에 붙은 물음표(?)·유니코드 대체문자(�)와 주변 공백을 제거합니다.
        private static string CleanGarbage(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            // 앞뒤의 찌꺼기 문자/공백을 한 번에 제거 (예: "?????　영유아부 " -> "영유아부")
            string cleaned = value.Trim('?', '�', ' ', '\t', '　').Trim();
            return cleaned;
        }
    }
}

