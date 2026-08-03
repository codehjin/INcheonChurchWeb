using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace INcheonChurchWeb.Models
{
    /// <summary>
    /// 🚀 행사 사후 보고서.
    /// 행사가 끝난 뒤 "언제·누가·어디서·어떻게, 돈은 어떻게 흘렀나"를 남긴다.
    /// 2년마다 바뀌는 임원에게 넘겨줄 인수인계 자료이자 이 시스템의 최종 산출물.
    ///
    /// ⚠️ 기존 ExpenseReport(결재·입금용 지출보고서)와는 다른 엔티티다.
    ///
    /// 재정 수치(예산·지출·수입)는 여기에 저장하지 않는다.
    /// 장부(Transactions)와 예산(BudgetPlans)에서 EventName으로 매칭해 항상 최신값을 집계한다.
    /// </summary>
    public class EventReport : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public int DepartmentId { get; set; }

        /// <summary>회계연도. 같은 행사명이 해마다 반복되므로 연도로 구분한다.</summary>
        public int FiscalYear { get; set; }

        /// <summary>
        /// 행사명. 예산·장부의 Category와 같은 문자열이어야 재정 자동 집계가 된다.
        /// (예: 여름성경학교, 겨울성경학교, 달란트)
        /// </summary>
        public string EventName { get; set; } = "";

        /// <summary>연간계획에서 시작한 경우 연결. 장부 분류에서 시작하면 null.</summary>
        public int? AnnualPlanId { get; set; }

        [ForeignKey(nameof(AnnualPlanId))]
        public virtual AnnualPlan? AnnualPlan { get; set; }

        // ── 행사 기록 (직접 입력) ──
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        /// <summary>장소</summary>
        public string? Location { get; set; }

        /// <summary>참석 인원</summary>
        public int? Attendees { get; set; }

        /// <summary>담당자. "이 행사는 누가 맡았나"를 남긴다.</summary>
        public string? Organizer { get; set; }

        /// <summary>진행 내용 — 무엇을 어떻게 했나</summary>
        public string? Content { get; set; }

        /// <summary>잘된 점 — 다음에도 이어갈 것</summary>
        public string? WhatWentWell { get; set; }

        /// <summary>개선할 점 — 다음 담당자가 참고할 것 (인수인계의 핵심)</summary>
        public string? WhatToImprove { get; set; }
    }
}
