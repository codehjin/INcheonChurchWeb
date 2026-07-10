using System;

namespace INcheonChurchWeb.Models
{
    // 🚀 모든 생명주기 엔티티의 공통 부모.
    // 감사 추적(Audit Trail) 및 소프트 삭제(Soft Delete)를 일괄 제공한다.
    public abstract class BaseEntity
    {
        // 생성 일시 (기본값: 현재 시각). SaveChanges 시 Added 상태면 자동 갱신.
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 생성자(로그인 아이디 등). 서비스 계층에서 주입.
        public string? CreatedBy { get; set; }

        // 최종 수정 일시. SaveChanges 시 Modified 상태면 자동 갱신.
        public DateTime? UpdatedAt { get; set; }

        // 최종 수정자.
        public string? UpdatedBy { get; set; }

        // 소프트 삭제 플래그. 글로벌 쿼리 필터로 true 인 행은 조회에서 제외된다.
        public bool IsDeleted { get; set; } = false;
    }
}
