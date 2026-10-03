using Church.Home.Data;
using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 부서 계정 추가 — 최고관리자가 '계정추가'를 체크해 준 부서운영자만, 자기 부서에, 교사·부서운영자만.
/// 권한은 화면이 넘긴 사용자 정보가 아니라 DB 에 있는 지금 값으로 판단한다.
/// </summary>
public class DepartmentAccountTests : IDisposable
{
    private const int 유년부 = 3;
    private const int 초등부 = 4;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;

    public DepartmentAccountTests()
    {
        _svc = new AccountingService(_db, new TestEnv());

        계정("admin", "Admin", 99);
        계정("leader", "Manager", 유년부, canManage: true);       // 계정추가를 받은 부서운영자
        계정("plain_mgr", "Manager", 유년부);                       // 받지 않은 부서운영자
        계정("teacher", "User", 유년부);
        계정("auditor", "Auditor", 6);
        계정("other_t", "User", 초등부);
    }

    public void Dispose() => _db.Dispose();

    private void 계정(string id, string role, int dept, bool canManage = false, bool active = true)
    {
        using var db = _db.CreateDbContext();
        db.Users.Add(new User
        {
            Username = id, Password = PasswordHasher.Hash("pw"), Role = role, DepartmentId = dept,
            IsActive = active, CanManageAccounts = canManage, FullName = id
        });
        db.SaveChanges();
    }

    /// <summary>로그인해 있는 화면이 들고 있는 사용자 정보 (DB 에서 읽은 그때의 값)</summary>
    private User 세션(string id)
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().Single(u => u.Username == id);
    }

    private User? 찾기(string id)
    {
        using var db = _db.CreateDbContext();
        return db.Users.AsNoTracking().SingleOrDefault(u => u.Username == id);
    }

    [Fact]
    public async Task 계정추가를_받은_부서운영자는_자기_부서에_교사와_부서운영자를_만든다()
    {
        await _svc.CreateDepartmentAccountAsync(세션("leader"), "kim_t", "김교사", "User", "hanaon91");
        await _svc.CreateDepartmentAccountAsync(세션("leader"), "lee_m", "이부장", "Manager", "hanaon91");

        var t = 찾기("kim_t")!;
        Assert.Equal((유년부, "User", "김교사", true), (t.DepartmentId, t.Role, t.FullName, t.IsActive));
        Assert.True(t.IsTeacher);
        Assert.True(PasswordHasher.IsHashed(t.Password));
        Assert.True(PasswordHasher.Verify("hanaon91", t.Password));

        var m = 찾기("lee_m")!;
        Assert.Equal((유년부, "Manager"), (m.DepartmentId, m.Role));
        Assert.False(m.CanManageAccounts);   // 🔒 새 부서운영자에게 계정추가 권한이 따라가지 않는다
    }

    [Fact]
    public async Task 계정추가를_받지_않은_부서운영자_교사_감사는_만들_수_없다()
    {
        foreach (var who in new[] { "plain_mgr", "teacher", "auditor" })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _svc.CreateDepartmentAccountAsync(세션(who), "new_" + who, null, "User", "hanaon91"));

        Assert.Null(찾기("new_plain_mgr"));
        Assert.Null(찾기("new_teacher"));
    }

    [Fact]
    public async Task 최고관리자나_감사_계정은_만들_수_없다()
    {
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "boss", null, "Admin", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "watcher", null, "Auditor", "hanaon91"));
        Assert.Null(찾기("boss"));
        Assert.Null(찾기("watcher"));
    }

    [Fact]
    public async Task 부서는_화면이_보낸_값이_아니라_만든_사람의_부서로_고정된다()
    {
        // 화면이 들고 있는 사용자 정보를 고쳐 다른 부서에 만들려는 경우
        var forged = 세션("leader");
        forged.DepartmentId = 초등부;

        await _svc.CreateDepartmentAccountAsync(forged, "sneaky", null, "User", "hanaon91");

        Assert.Equal(유년부, 찾기("sneaky")!.DepartmentId);
    }

    [Fact]
    public async Task 권한을_거두거나_정지하면_열려_있던_화면에서도_바로_막힌다()
    {
        var screen = 세션("leader");   // 권한이 있을 때 연 화면

        using (var db = _db.CreateDbContext())
        {
            db.Users.Single(u => u.Username == "leader").CanManageAccounts = false;
            db.SaveChanges();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.CreateDepartmentAccountAsync(screen, "late1", null, "User", "hanaon91"));

        using (var db = _db.CreateDbContext())
        {
            var u = db.Users.Single(u => u.Username == "leader");
            u.CanManageAccounts = true;
            u.IsActive = false;
            db.SaveChanges();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.CreateDepartmentAccountAsync(screen, "late2", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.GetDepartmentAccountsAsync(screen));
    }

    [Fact]
    public async Task 겹치는_아이디_잘못된_아이디_짧은_비밀번호는_거절한다()
    {
        await _svc.CreateDepartmentAccountAsync(세션("leader"), "kim_t", null, "User", "hanaon91");

        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "KIM_T", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "teacher", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "a b", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "ab", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "김교사", null, "User", "hanaon91"));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.CreateDepartmentAccountAsync(세션("leader"), "park_t", null, "User", "123"));
    }

    [Fact]
    public async Task 부서_계정_목록은_내_부서_것만()
    {
        var list = await _svc.GetDepartmentAccountsAsync(세션("leader"));

        Assert.All(list, u => Assert.Equal(유년부, u.DepartmentId));
        Assert.Contains(list, u => u.Username == "teacher");
        Assert.DoesNotContain(list, u => u.Username == "other_t");
    }

    [Fact]
    public async Task 계정추가_권한은_최고관리자만_부서운영자에게_준다()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.SetCanManageAccountsAsync(세션("leader"), "plain_mgr", true));
        await Assert.ThrowsAsync<AccountRuleException>(() => _svc.SetCanManageAccountsAsync(세션("admin"), "teacher", true));

        await _svc.SetCanManageAccountsAsync(세션("admin"), "plain_mgr", true);
        Assert.True(찾기("plain_mgr")!.CanManageAccounts);
        await _svc.CreateDepartmentAccountAsync(세션("plain_mgr"), "now_ok", null, "User", "hanaon91");

        await _svc.SetCanManageAccountsAsync(세션("admin"), "plain_mgr", false);
        Assert.False(찾기("plain_mgr")!.CanManageAccounts);
    }
}
