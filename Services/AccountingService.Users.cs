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
