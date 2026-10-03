using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 교사 계정 (저장값 "User" — 예전 '일반조회'가 그대로 교사가 되었다).
///   - 장부 열람 · 영수증 제출은 예전 그대로, 장부 편집은 없다
///   - 연간계획 · 주간 회의록 · 행사보고서를 본다
///   - 주간 회의록은 추가하고, 자기가 쓴 기록만 고치거나 지운다
/// 회의록 쓰기 권한은 서비스가 판단한다 — 작성자는 화면이 보낸 값이 아니라 DB 에 저장된 값으로 본다.
/// </summary>
public class TeacherPermissionTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;

    private static readonly User 교사A = new() { Username = "teacher_a", Role = "User", DepartmentId = 유년부 };
    private static readonly User 교사B = new() { Username = "teacher_b", Role = "User", DepartmentId = 유년부 };
    private static readonly User 부장 = new() { Username = "leader", Role = "Manager", DepartmentId = 유년부 };
    private static readonly User 감사 = new() { Username = "auditor", Role = "Auditor", DepartmentId = 6 };
    private static readonly User 남의교사 = new() { Username = "other_t", Role = "User", DepartmentId = 초등부 };

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public TeacherPermissionTests() => _svc = new AccountingService(_db, new TestEnv());

    public void Dispose() => _db.Dispose();

    private static WeeklyMeeting 새기록(int dept = 유년부, string memo = "달란트 시장 준비")
        => new() { DepartmentId = dept, MeetingDate = new DateTime(2026, 4, 5), FreeMemo = memo };

    private WeeklyMeeting 저장된(int id)
    {
        using var db = _db.CreateDbContext();
        return db.WeeklyMeetings.IgnoreQueryFilters().AsNoTracking().Single(m => m.Id == id);
    }

    [Fact]
    public void 교사_권한표()
    {
        Assert.True(교사A.IsTeacher);
        Assert.True(교사A.CanViewPlanning);           // 연간계획 · 회의록 · 행사보고서 보기
        Assert.True(교사A.CanAddMeetings);            // 회의록 추가
        Assert.True(교사A.CanAccessDataCollection);   // 영수증 제출 — 예전 일반조회 그대로
        Assert.False(교사A.CanEdit);                  // 계획·보고서·장부 편집 없음
        Assert.False(교사A.CanEditLedger);
        Assert.False(교사A.CanAddAccounts);
        Assert.Equal("교사", User.RoleLabel("User"));

        Assert.True(감사.CanViewPlanning);
        Assert.False(감사.CanAddMeetings);
    }

    [Fact]
    public async Task 교사는_회의록을_추가하고_작성자로_남는다()
    {
        var m = 새기록();

        await _svc.SaveWeeklyMeetingAsync(m, 교사A);

        Assert.Equal("teacher_a", 저장된(m.Id).CreatedBy);
    }

    [Fact]
    public async Task 교사는_자기_기록만_고치고_지운다()
    {
        var m = 새기록();
        await _svc.SaveWeeklyMeetingAsync(m, 교사A);

        // 다른 교사는 못 고치고 못 지운다
        var byB = 새기록(memo: "B 가 고침"); byB.Id = m.Id;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveWeeklyMeetingAsync(byB, 교사B));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.DeleteWeeklyMeetingAsync(m.Id, 교사B));
        Assert.Equal("달란트 시장 준비", 저장된(m.Id).FreeMemo);

        // 쓴 사람은 고치고 지운다
        var byA = 새기록(memo: "A 가 고침"); byA.Id = m.Id;
        await _svc.SaveWeeklyMeetingAsync(byA, 교사A);
        Assert.Equal("A 가 고침", 저장된(m.Id).FreeMemo);

        await _svc.DeleteWeeklyMeetingAsync(m.Id, 교사A);
        Assert.True(저장된(m.Id).IsDeleted);
    }

    [Fact]
    public async Task 작성자는_화면이_보낸_값이_아니라_저장된_값으로_본다()
    {
        var m = 새기록();
        await _svc.SaveWeeklyMeetingAsync(m, 교사A);

        var forged = 새기록(memo: "덮어쓰기"); forged.Id = m.Id; forged.CreatedBy = "teacher_b";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveWeeklyMeetingAsync(forged, 교사B));
        Assert.Equal("teacher_a", 저장된(m.Id).CreatedBy);
    }

    [Fact]
    public async Task 부서운영자는_교사가_쓴_기록도_고친다()
    {
        var m = 새기록();
        await _svc.SaveWeeklyMeetingAsync(m, 교사A);

        var edit = 새기록(memo: "부장이 정리함"); edit.Id = m.Id;
        await _svc.SaveWeeklyMeetingAsync(edit, 부장);

        Assert.Equal("부장이 정리함", 저장된(m.Id).FreeMemo);
        Assert.Equal("teacher_a", 저장된(m.Id).CreatedBy);   // 작성자는 그대로
    }

    [Fact]
    public async Task 다른_부서_회의록은_쓰거나_고칠_수_없다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveWeeklyMeetingAsync(새기록(초등부), 교사A));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveWeeklyMeetingAsync(새기록(유년부), 남의교사));

        var m = 새기록();
        await _svc.SaveWeeklyMeetingAsync(m, 교사A);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.DeleteWeeklyMeetingAsync(m.Id, 남의교사));
    }

    [Fact]
    public async Task 감사는_회의록을_쓸_수_없다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SaveWeeklyMeetingAsync(새기록(6), 감사));
    }
}
