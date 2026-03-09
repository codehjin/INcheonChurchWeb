using INcheonChurchWeb.Models;

namespace INcheonChurchWeb.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            context.Database.EnsureCreated();

            // 이미 예산 데이터가 있으면 아무것도 안 함 (중복 방지)
            if (context.BudgetPlans.Any())
            {
                return;
            }

            // PDF 내용을 바탕으로 2026년 초기 데이터 생성
            var budget2026 = new BudgetPlan[]
            {
                // === 수입 (Income) ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Income", Category="교회보조금", CalcDetail="기본 보조", Amount=7800000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Income", Category="주일헌금", CalcDetail="매주 헌금 예상", Amount=1500000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Income", Category="찬조금", CalcDetail="특별 찬조", Amount=3239000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Income", Category="회비수입", CalcDetail="수련회비 등", Amount=1500000 },

                // === 지출 (Expense) - 겨울성경학교 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="겨울성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="겨울성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="겨울성경학교", SubCategory="예비비", CalcDetail="보조교사 선물 등", Amount=200000 },

                // === 지출 - 여름성경학교 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="여름성경학교", SubCategory="식사", CalcDetail="8,000원*38명*2회", Amount=608000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="여름성경학교", SubCategory="프로그램 준비비", CalcDetail="교재, 데코비 등", Amount=600000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="여름성경학교", SubCategory="외부 물놀이", CalcDetail="30,000원*38명", Amount=1140000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="여름성경학교", SubCategory="예비비", CalcDetail="기타 진행비", Amount=200000 },

                // === 지출 - 행사비 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="생일축하행사", CalcDetail="10,000원*38명", Amount=380000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="성탄절준비비", CalcDetail="20,000원*25명", Amount=500000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="성탄절선물", CalcDetail="10,000원*38명", Amount=380000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="졸업선물", CalcDetail="20,000원*8명", Amount=160000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="부활절 특별활동", CalcDetail="계란 등", Amount=300000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="야외예배", CalcDetail="10,000원*38명*2회", Amount=760000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="전도비", CalcDetail="새친구 선물 등", Amount=200000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="반데이트", CalcDetail="20,000원*38명*2회", Amount=1520000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="행사비", SubCategory="달란트행사", CalcDetail="20,000원*25명*2회", Amount=1000000 },

                // === 지출 - 훈련비 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="훈련비", SubCategory="공과교재", CalcDetail="4,500원*35명*2회", Amount=315000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="훈련비", SubCategory="교사훈련(MT)", CalcDetail="30,000원*13명*2회", Amount=780000 },

                // === 지출 - 부서관리비 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="부서관리비", SubCategory="물품구입", CalcDetail="가방, 명찰, 앞치마", Amount=100000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="부서관리비", SubCategory="환경미화", CalcDetail="현수막 및 데코", Amount=100000 },

                // === 지출 - 교사회의비 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="교사회의비", SubCategory="교사회식", CalcDetail="10,000원*13명*4분기", Amount=520000 },
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="교사회의비", SubCategory="교사간식", CalcDetail="3,000원*13명*12달", Amount=468000 },

                // === 지출 - 공과비 ===
                new BudgetPlan { Year=2026, Department="유년부", Type="Expense", Category="공과비", SubCategory="주간공과", CalcDetail="2,000원*25명*52주", Amount=2600000 },
            };

            context.BudgetPlans.AddRange(budget2026);
            context.SaveChanges();
        }
    }
}