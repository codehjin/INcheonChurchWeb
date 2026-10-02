using System.ComponentModel.DataAnnotations;

namespace Church.Home.Data
{
    // ───────────────────────────────────────────────
    // home.db 는 church.db 의 '공개된 것만' 담은 투영본이다.
    //   - 원본은 전부 church.db 에 있고, 재정앱이 공개 버튼을 누를 때 여기로 내보낸다.
    //   - 그래서 IsPublished · IsDeleted 컬럼이 없다. 여기 있으면 공개된 것이다.
    //   - 🔒 금액 컬럼이 하나도 없다. 담을 그릇이 없으니 실수로 새어 나갈 수도 없다.
    // ───────────────────────────────────────────────

    /// <summary>
    /// 학부모 계정. 부서마다 공용 계정 하나를 단체방에 공지한다.
    /// Id 는 church.db 원본의 Id 를 그대로 쓴다 (재동기화 때 짝을 맞추는 열쇠).
    /// </summary>
    public class ParentAccount
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }

        /// <summary>부서명 (비정규화). home.db 에는 부서 테이블이 없다.</summary>
        public string DeptName { get; set; } = "";

        public string Username { get; set; } = "";

        /// <summary>PasswordHasher 형식(pbkdf2$...)만 들어온다. 평문은 받지 않는다.</summary>
        public string PasswordHash { get; set; } = "";

        /// <summary>false 면 로그인·세션 복원이 모두 막힌다. 비번이 퍼진 공용 계정을 끊는 유일한 수단.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 부서 소개 — 통신문마다 바뀌지 않는 머리말·꼬리말·바로가기·연락처.
    /// (가정통신문 예시: "하나온 유년부" / 오픈채팅·유튜브 QR / 하단 표어 / 선생님 연락처)
    /// </summary>
    public class PortalProfile
    {
        [Key]
        public int DepartmentId { get; set; }

        /// <summary>부서명 (예: 유년부)</summary>
        public string DeptName { get; set; } = "";

        /// <summary>머리말에 크게 쓰는 이름 (예: 하나온 유년부). 비우면 DeptName.</summary>
        public string? DisplayName { get; set; }

        /// <summary>하단 표어 (예: 한마음으로 사랑하고, 한뜻으로 하나되자!)</summary>
        public string? Motto { get; set; }

        /// <summary>표어 아래 말씀 (예: 그렇다면 서로 한 마음으로 … (빌립보서 2장 2절))</summary>
        public string? MottoVerse { get; set; }

        /// <summary>부서 오픈채팅방 주소</summary>
        public string? OpenChatUrl { get; set; }

        /// <summary>오픈채팅방 참여코드 (예: 9191)</summary>
        public string? OpenChatCode { get; set; }

        /// <summary>부서 유튜브 주소</summary>
        public string? YouTubeUrl { get; set; }

        /// <summary>부서 사진첩 — 구글포토 공유 앨범 주소</summary>
        public string? PhotoAlbumUrl { get; set; }

        // ── 🔒 연락처는 두 자리뿐이다: 교역자(전도사)와 부장 ──
        //    학년별 교사 연락처를 담을 칸이 아예 없다. 늘리려면 스키마부터 바꿔야 한다.

        /// <summary>교역자 직함. 기본 "전도사" (부서에 따라 목사 등)</summary>
        public string MinisterTitle { get; set; } = "전도사";
        public string? MinisterName { get; set; }
        public string? MinisterPhone { get; set; }

        public string? HeadName { get; set; }
        public string? HeadPhone { get; set; }
    }

    /// <summary>
    /// 공개된 월간 가정통신문. 부서 · 연 · 월에 한 건.
    /// Id 는 church.db 원본의 Id 를 그대로 쓴다.
    /// </summary>
    public class Notice
    {
        public int Id { get; set; }
        public int DepartmentId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>인사말·이달의 안내. 「」로 감싼 말은 강조해서 보여 준다.</summary>
        public string? Greeting { get; set; }

        /// <summary>일정표 아래 메모 (예: 활동약어 : 반(반별활동), 생(생일잔치), 레(레크레이션))</summary>
        public string? ScheduleNote { get; set; }

        public DateTime PublishedAt { get; set; }

        public List<NoticeWeek> Weeks { get; set; } = new();
        public List<NoticeSection> Sections { get; set; } = new();
        public List<NoticeLink> Links { get; set; } = new();
    }

    /// <summary>
    /// 일정표 한 줄 = 한 주.
    /// 주차는 SortOrder 순서로 매긴다. 열이 전부 비어 있으면 학부모 화면에서 그 열을 숨긴다
    /// (부서마다 쓰는 열이 다르다).
    /// </summary>
    public class NoticeWeek
    {
        public int Id { get; set; }
        public int NoticeId { get; set; }

        /// <summary>그 주의 주일</summary>
        public DateTime Date { get; set; }

        /// <summary>말씀 (예: 마태복음 28:5~10)</summary>
        public string? Scripture { get; set; }

        /// <summary>설교 제목</summary>
        public string? SermonTitle { get; set; }

        /// <summary>활동 (예: 반, 생, 야외예배). 연간계획 행사로 미리 채워진다.</summary>
        public string? Activity { get; set; }

        /// <summary>봉헌 (봉헌어린이)</summary>
        public string? Offering { get; set; }

        /// <summary>대표기도</summary>
        public string? Prayer { get; set; }

        public int SortOrder { get; set; }
    }

    /// <summary>안내 한 꼭지 (예: 봄맞이 야외예배 안내, 달란트 프로그램 안내)</summary>
    public class NoticeSection
    {
        public int Id { get; set; }
        public int NoticeId { get; set; }
        public string Title { get; set; } = "";

        /// <summary>본문. 줄바꿈을 그대로 보여 준다 (white-space: pre-wrap).</summary>
        public string? Body { get; set; }

        public int SortOrder { get; set; }
    }

    /// <summary>그달 행사 사진 — 구글포토 공유 앨범 링크. http(s) 주소만 들어온다 (SafeUrl).</summary>
    public class NoticeLink
    {
        public int Id { get; set; }
        public int NoticeId { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public int SortOrder { get; set; }
    }
}
