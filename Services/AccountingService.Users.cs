using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using PasswordHasher = Church.Home.Data.PasswordHasher;
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
    /// <summary>계정 규칙에 맞지 않아 만들지 않았다. Message 를 그대로 화면에 보여 준다.</summary>
    public class AccountRuleException : Exception
    {
        public AccountRuleException(string message) : base(message) { }
    }

    // 사용자 · 부서 정보
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // =========================================================
        // 6. 사용자 관리 및 부서 정보 관리
        // =========================================================
        public async Task<List<User>> GetAllUsersAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Users.AsNoTracking().ToListAsync();
        }
        public async Task AddUserAsync(User user)
        {
            using var db = _dbFactory.CreateDbContext();
            if (!await db.Users.AnyAsync(u => u.Username == user.Username)) { db.Users.Add(user); await db.SaveChangesAsync(); }
        }
        public async Task DeleteUserAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { db.Users.Remove(u); await db.SaveChangesAsync(); }
        }
        public async Task ChangePasswordAsync(string id, string pw)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { u.Password = PasswordHasher.Hash(pw); await db.SaveChangesAsync(); }
        }
        public async Task ResetPasswordAsync(string id)
        {
            using var db = _dbFactory.CreateDbContext();
            var u = await db.Users.FindAsync(id); if (u != null) { u.Password = PasswordHasher.Hash("1234"); await db.SaveChangesAsync(); }
        }

        // 부서 목록 전체 불러오기
        public async Task<List<Department>> GetDepartmentsAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Departments.AsNoTracking().ToListAsync();
        }

        // 🚀 신규 추가: 단일 부서 정보 가져오기 (환경설정용)
        public async Task<Department?> GetDepartmentAsync(int departmentId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId);
        }

        // 🚀 신규 추가: 단일 부서 정보 업데이트 (환경설정용)
        public async Task UpdateDepartmentAsync(Department updatedDept)
        {
            using var db = _dbFactory.CreateDbContext();

            var existing = await db.Departments.FindAsync(updatedDept.Id);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(updatedDept);
                await db.SaveChangesAsync();
            }
        }

        // =========================================================
        // 🚀 부서 계정 추가 — '계정추가'를 받은 부서운영자
        //   최고관리자가 체크해 준 부서운영자는 자기 부서에 부서운영자 · 교사 계정을 만든다.
        //   권한은 화면이 아니라 여기서 판단하고, 요청한 사람을 DB 에서 다시 읽는다 —
        //   관리자가 체크를 풀거나 계정을 정지하면 열려 있던 화면에서도 바로 막힌다.
        //   (최고관리자는 기존 '사용자 계정 관리' 화면을 그대로 쓴다)
        // =========================================================

        /// <summary>부서운영자가 만들 수 있는 역할 — 부서운영자 · 교사(User). 최고관리자·감사는 만들 수 없다.</summary>
        public static readonly IReadOnlyList<string> RolesManagersCanCreate = new[] { "Manager", "User" };

        /// <summary>내 부서 계정 목록 (계정추가 권한이 있는 부서운영자)</summary>
        public async Task<List<User>> GetDepartmentAccountsAsync(User actor)
        {
            using var db = _dbFactory.CreateDbContext();
            var me = await AccountManagerAsync(db, actor);
            return await db.Users.AsNoTracking()
                .Where(u => u.DepartmentId == me.DepartmentId)
                .OrderBy(u => u.Role).ThenBy(u => u.Username)
                .ToListAsync();
        }

        /// <summary>내 부서에 계정을 만든다. 만든 아이디를 돌려준다.</summary>
        public async Task<string> CreateDepartmentAccountAsync(User actor, string? username, string? fullName, string role, string? password)
        {
            using var db = _dbFactory.CreateDbContext();
            var me = await AccountManagerAsync(db, actor);

            var id = (username ?? "").Trim();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_.-]{3,30}$"))
                throw new AccountRuleException("아이디는 영문 · 숫자 · 밑줄(_) · 점(.) · 빼기(-)로 3~30자로 정해 주세요.");
            if (!RolesManagersCanCreate.Contains(role))
                throw new AccountRuleException("부서운영자 또는 교사 계정만 만들 수 있습니다.");
            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 6)
                throw new AccountRuleException("처음 비밀번호는 6자 이상으로 정해 주세요.");

            var lower = id.ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Username.ToLower() == lower))
                throw new AccountRuleException("이미 쓰고 있는 아이디입니다.");

            db.Users.Add(new User
            {
                Username = id,
                FullName = (fullName ?? "").Trim(),
                Role = role,
                DepartmentId = me.DepartmentId,              // 🔒 부서는 만든 사람의 부서로 고정
                Password = PasswordHasher.Hash(password.Trim()),
                IsActive = true,
                CanManageAccounts = false                     // 🔒 '계정추가'는 최고관리자만 줄 수 있다
            });
            await db.SaveChangesAsync();

            await LogActivityAsync(me.Username, "계정 추가", $"{id} ({User.RoleLabel(role)}, 부서 {me.DepartmentId})");
            return id;
        }

        /// <summary>최고관리자 — 부서운영자에게 '계정추가' 권한을 주거나 거둔다.</summary>
        public async Task SetCanManageAccountsAsync(User actor, string username, bool allowed)
        {
            if (!actor.IsSystemAdmin)
                throw new UnauthorizedAccessException("최고 관리자만 할 수 있습니다.");

            using var db = _dbFactory.CreateDbContext();
            var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username)
                         ?? throw new AccountRuleException("계정을 찾을 수 없습니다.");
            if (target.Role != "Manager")
                throw new AccountRuleException("'계정추가'는 부서운영자에게만 줄 수 있습니다.");

            target.CanManageAccounts = allowed;
            await db.SaveChangesAsync();
            await LogActivityAsync(actor.Username, allowed ? "계정추가 권한 부여" : "계정추가 권한 회수", username);
        }

        // 요청한 사람을 DB 에서 다시 읽어 지금도 계정추가 권한이 있는지 본다 (화면에 들고 있는 값은 낡았을 수 있다)
        private static async Task<User> AccountManagerAsync(AppDbContext db, User actor)
        {
            var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == actor.Username);
            if (me == null || !me.IsActive || me.Role != "Manager" || !me.CanManageAccounts || me.DepartmentId <= 0)
                throw new UnauthorizedAccessException("계정을 추가할 권한이 없습니다. 최고 관리자에게 '계정추가' 권한을 요청하세요.");
            return me;
        }

        // 사용자 정보 통째로 업데이트 (이름, 부서, 활성상태 변경용)
        public async Task UpdateUserAsync(User user)
        {
            using var db = _dbFactory.CreateDbContext();

            var existing = await db.Users.FindAsync(user.Username);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(user);
                await db.SaveChangesAsync();
            }
        }
    }
}
