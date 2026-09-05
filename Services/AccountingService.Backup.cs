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
    // 활동 로그 · 부서 백업/복구
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // =========================================================
        // 3. 로그 및 백업 (DataType 수정)
        // =========================================================
        public async Task LogActivityAsync(string username, string action, string details)
        {
            using var db = _dbFactory.CreateDbContext();

            db.ActivityLogs.Add(new ActivityLog { Username = username, Action = action, Details = details, Timestamp = DateTime.Now });
            await db.SaveChangesAsync();
        }

        public async Task CreateBackupAsync(int deptId, string type, string memo)
        {
            using var db = _dbFactory.CreateDbContext();

            string jsonData;
            if (type == "Ledger")
            {
                jsonData = JsonSerializer.Serialize(await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            }
            else if (type == "Budget")
            {
                jsonData = JsonSerializer.Serialize(await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).ToListAsync());
            }
            else if (type == "Mapping")
            {
                jsonData = JsonSerializer.Serialize(await db.CategoryMappings.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync());
            }
            else
            {
                var snapshot = new DepartmentBackupDto
                {
                    ExportDate = DateTime.Now,
                    DepartmentId = deptId,
                    Transactions = await db.Transactions.AsNoTracking().Where(t => t.DepartmentId == deptId).ToListAsync(),
                    BudgetPlans = await db.BudgetPlans.AsNoTracking().Where(b => b.DepartmentId == deptId).ToListAsync(),
                    CategoryMappings = await db.CategoryMappings.AsNoTracking().Where(m => m.DepartmentId == deptId).ToListAsync(),
                    ExpenseReports = await db.ExpenseReports.AsNoTracking().Where(r => r.DepartmentId == deptId).ToListAsync()
                };

                jsonData = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            }

            db.DataBackups.Add(new DataBackup { DepartmentId = deptId, DataType = type, Memo = memo, JsonData = jsonData, BackupDate = DateTime.Now });
            await db.SaveChangesAsync();
        }

        public async Task<List<DataBackup>> GetBackupsAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();
            return await db.DataBackups.AsNoTracking().Where(b => b.DepartmentId == deptId).OrderByDescending(b => b.BackupDate).ToListAsync();
        }

        public async Task RestoreFromBackupAsync(int backupId)
        {
            using var db = _dbFactory.CreateDbContext();

            var backup = await db.DataBackups.FindAsync(backupId);
            if (backup == null) return;

            if (backup.DataType == "Budget")
            {
                var old = await db.BudgetPlans.Where(b => b.DepartmentId == backup.DepartmentId).ToListAsync();
                db.BudgetPlans.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<BudgetPlan>>(backup.JsonData);
                if (restored != null) db.BudgetPlans.AddRange(restored);
            }
            else if (backup.DataType == "Ledger")
            {
                var old = await db.Transactions.Where(t => t.DepartmentId == backup.DepartmentId).ToListAsync();
                db.Transactions.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<LedgerEntry>>(backup.JsonData);
                if (restored != null) db.Transactions.AddRange(restored);
            }
            else if (backup.DataType == "Mapping")
            {
                var old = await db.CategoryMappings.Where(m => m.DepartmentId == backup.DepartmentId).ToListAsync();
                db.CategoryMappings.RemoveRange(old);
                var restored = JsonSerializer.Deserialize<List<CategoryMapping>>(backup.JsonData);
                if (restored != null) db.CategoryMappings.AddRange(restored);
            }
            else
            {
                var snapshot = JsonSerializer.Deserialize<DepartmentBackupDto>(backup.JsonData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (snapshot == null) return;

                var oldTrans = await db.Transactions.Where(t => t.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldBudgets = await db.BudgetPlans.Where(b => b.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldMappings = await db.CategoryMappings.Where(m => m.DepartmentId == backup.DepartmentId).ToListAsync();
                var oldReports = await db.ExpenseReports.Where(r => r.DepartmentId == backup.DepartmentId).ToListAsync();

                db.Transactions.RemoveRange(oldTrans);
                db.BudgetPlans.RemoveRange(oldBudgets);
                db.CategoryMappings.RemoveRange(oldMappings);
                db.ExpenseReports.RemoveRange(oldReports);

                if (snapshot.Transactions.Any()) db.Transactions.AddRange(snapshot.Transactions);
                if (snapshot.BudgetPlans.Any()) db.BudgetPlans.AddRange(snapshot.BudgetPlans);
                if (snapshot.CategoryMappings.Any()) db.CategoryMappings.AddRange(snapshot.CategoryMappings);
                if (snapshot.ExpenseReports.Any()) db.ExpenseReports.AddRange(snapshot.ExpenseReports);
            }

            await db.SaveChangesAsync();
        }
    }
}
