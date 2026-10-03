using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace INcheonChurchWeb.Models
{
    // ───────────────────────────────────────────────
    // 🚀 학부모 포털(ijch-school.kro.kr)의 원본.
    //   여기(church.db)에서 쓰고, 공개할 때 HomePublisher 가 home.db 로 내보낸다.
    //   학부모앱은 church.db 를 열지 못하므로 이 테이블들을 직접 보지 않는다.
    //
    //   이름에 Parent 를 붙인다 — home.db 쪽(Church.Home.Data)에 Notice·ParentAccount 가 따로 있다.
    //
    //   HomeSynced: home.db 가 이 행의 '지금 상태'를 반영하고 있는가.
    //     공개 중이면 같은 내용이 들어가 있어야 하고, 비공개면 home.db 에 없어야 한다.
    //     내보내기가 실패하면 false 로 남아 목록에 경고가 뜬다 → 다시 누르거나 [전체 재동기화].
    //
    //   공개 중인 통신문은 임시저장할 수 없다 (ParentPortalService.SaveDraftAsync 참고).
    //   그래서 공개 중인 통신문의 내용은 늘 '학부모가 받은 내용'과 같다 — 재동기화가 안전하다.
    // ───────────────────────────────────────────────

    /// <summary>월간 가정통신문. 부서 · 연 · 월에 한 건.</summary>
    public class ParentNotice : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public int DepartmentId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>인사말·이달의 안내. 「」로 감싼 말은 학부모 화면에서 강조된다.</summary>
        public string? Greeting { get; set; }

        /// <summary>일정표 아래 메모 (예: 활동약어 : 반(반별활동), 생(생일잔치))</summary>
        public string? ScheduleNote { get; set; }

        /// <summary>학부모에게 공개 중인가. false 면 임시저장.</summary>
        public bool IsPublished { get; set; }

        /// <summary>마지막으로 [공개]를 누른 시각</summary>
        public DateTime? PublishedAt { get; set; }

        public bool HomeSynced { get; set; } = true;

        public List<ParentNoticeWeek> Weeks { get; set; } = new();
        public List<ParentNoticeSection> Sections { get; set; } = new();
        public List<ParentNoticeLink> Links { get; set; } = new();
    }

    /// <summary>일정표 한 줄 = 한 주. 주차는 SortOrder 순서.</summary>
    public class ParentNoticeWeek
    {
        public int Id { get; set; }
        public int ParentNoticeId { get; set; }

        /// <summary>그 주의 주일</summary>
        public DateTime Date { get; set; }

        public string? Scripture { get; set; }    // 말씀
        public string? SermonTitle { get; set; }  // 제목
        public string? Activity { get; set; }     // 활동 — 연간계획 행사명으로 미리 채운다
        public string? Offering { get; set; }     // 봉헌(어린이)
        public string? Prayer { get; set; }       // 대표기도

        public int SortOrder { get; set; }
    }

    /// <summary>안내 한 꼭지 (예: 봄맞이 야외예배 안내)</summary>
    public class ParentNoticeSection
    {
        public int Id { get; set; }
        public int ParentNoticeId { get; set; }
        public string Title { get; set; } = "";
        public string? Body { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>그달 행사 사진 — 구글포토 공유 앨범 링크</summary>
    public class ParentNoticeLink
    {
        public int Id { get; set; }
        public int ParentNoticeId { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 부서 소개 — 통신문마다 바뀌지 않는 머리말·꼬리말·바로가기·연락처. 부서당 한 줄.
    /// 임시저장이 없다. 저장하면 바로 학부모 화면에 반영된다.
    /// </summary>
    public class ParentPortalProfile
    {
        [Key]
        public int DepartmentId { get; set; }

        public string? DisplayName { get; set; }   // 하나온 유년부
        public string? Motto { get; set; }         // 한마음으로 사랑하고, 한뜻으로 하나되자!
        public string? MottoVerse { get; set; }    // (빌립보서 2장 2절)

        public string? OpenChatUrl { get; set; }
        public string? OpenChatCode { get; set; }
        public string? YouTubeUrl { get; set; }
        public string? PhotoAlbumUrl { get; set; } // 구글포토 부서 사진첩

        // 🔒 연락처는 교역자·부장 두 자리뿐 (학년별 교사 연락처는 싣지 않는다)
        public string MinisterTitle { get; set; } = "전도사";
        public string? MinisterName { get; set; }
        public string? MinisterPhone { get; set; }
        public string? HeadName { get; set; }
        public string? HeadPhone { get; set; }

        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public bool HomeSynced { get; set; } = true;
    }

    /// <summary>
    /// 학부모 공용 계정 (부서당 하나를 단체방에 공지). 스태프 Users 와 섞이지 않는다 —
    /// 이 아이디로는 재정앱에 로그인할 수 없다.
    /// </summary>
    public class ParentPortalAccount
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }
        public string Username { get; set; } = "";

        /// <summary>PasswordHasher.Hash 결과만 저장한다. 평문은 어디에도 남기지 않는다.</summary>
        public string PasswordHash { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public bool HomeSynced { get; set; } = true;
    }
}
