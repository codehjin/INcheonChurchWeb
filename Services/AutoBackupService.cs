using INcheonChurchWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Services
{
    // 백그라운드에서 365일 24시간 돌아가는 자동 백업 & 정리 서비스
    public class AutoBackupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoBackupService> _logger;

        public AutoBackupService(IServiceProvider serviceProvider, ILogger<AutoBackupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✨ [시스템] 부서별 심야 자동 백업 및 정리 타이머가 시작되었습니다.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;

                // 다음날 밤 12시(자정) 시간 계산
                var nextMidnight = now.Date.AddDays(1);
                var timeUntilMidnight = nextMidnight - now;

                _logger.LogInformation($"⏱️ 다음 자동 백업 및 정리까지 대기하는 시간: {timeUntilMidnight.Hours}시간 {timeUntilMidnight.Minutes}분");

                // 자정이 될 때까지 백그라운드에서 조용히 대기
                await Task.Delay(timeUntilMidnight, stoppingToken);

                // 자정이 땡 치면 백업 및 청소 로직 실행!
                await PerformBackupAndCleanupAsync();
            }
        }

        private async Task PerformBackupAndCleanupAsync()
        {
            try
            {
                _logger.LogInformation("🚀 [시스템] 자정입니다! 전 부서 자동 백업 및 오래된 파일 정리를 시작합니다...");

                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var accService = scope.ServiceProvider.GetRequiredService<AccountingService>();

                    // 1. 운영 중인 모든 부서 목록 가져오기
                    var departments = await dbContext.Departments
                                        .Where(d => d.Name != "관리자" && d.Name != "시스템")
                                        .ToListAsync();

                    int backupCount = 0;

                    // 2. 오늘의 새로운 백업 생성
                    foreach (var dept in departments)
                    {
                        string memo = $"시스템 심야 자동 백업 ({DateTime.Now:MM/dd})";
                        await accService.CreateBackupAsync(dept.Id, "Auto", memo);
                        backupCount++;
                    }
                    _logger.LogInformation($"✅ [시스템] 총 {backupCount}개 부서의 자동 백업이 안전하게 완료되었습니다.");

                    // =========================================================
                    // 🚀 3. [추가된 기능] 10일이 지난 오래된 자동 백업 데이터 청소
                    // =========================================================
                    var cutoffDate = DateTime.Now.AddDays(-10); // 정확히 10일 전의 시간

                    // 수동 백업("Manual")은 지우지 않고, 자동 백업("Auto") 중 10일이 지난 것만 찾습니다.
                    var oldBackups = await dbContext.DataBackups
                                        .Where(b => b.DataType == "Auto" && b.BackupDate < cutoffDate)
                                        .ToListAsync();

                    if (oldBackups.Any())
                    {
                        int deletedCount = oldBackups.Count;
                        dbContext.DataBackups.RemoveRange(oldBackups);
                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation($"🧹 [시스템] 10일이 경과된 오래된 자동 백업 데이터 {deletedCount}건을 깔끔하게 삭제했습니다.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [시스템] 자동 백업 및 정리 실행 중 심각한 오류가 발생했습니다.");
            }
        }
    }
}