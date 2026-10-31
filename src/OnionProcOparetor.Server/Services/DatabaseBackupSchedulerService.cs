using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// ตัวเดินเวลาสำรอง DB อัตโนมัติตาม BackupSettings.Frequency (WP3-1, 2026-10-31) - เช็คทุก 30 นาทีว่า
/// "ครบรอบหรือยัง" แทนการตั้ง timer ให้ตรงเวลานาฬิกาเป๊ะๆ (เช่น 18:00 ตาม wireframe) เพราะ server
/// อาจถูก restart กลางวัน/กลางคืนได้ตลอด (MSI upgrade, Windows update ฯลฯ) การเช็ค "นานแค่ไหนแล้วจาก
/// ครั้งก่อน" ทนต่อการ restart ได้ดีกว่าตั้ง absolute time ที่พลาดรอบได้ง่ายถ้า service ไม่ได้รันอยู่พอดี
/// ตอนนั้น - ความแม่นยำของเวลา (18:00 เป๊ะ) ไม่ใช่สิ่งที่ roadmap เน้นเท่ากับ "มีสำรองเกิดขึ้นสม่ำเสมอ"
///
/// scoped service (DatabaseBackupService) ต้องสร้าง scope เองทุกรอบ - ตัว BackgroundService เองเป็น
/// singleton แต่ AppDbContext/ActivityLogDbContext เป็น scoped (pattern เดียวกับ ActivityLogCleanupService)
/// </summary>
public class DatabaseBackupSchedulerService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseBackupSchedulerService> _logger;

    public DatabaseBackupSchedulerService(IServiceScopeFactory scopeFactory, ILogger<DatabaseBackupSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
                var settings = await backupService.GetOrCreateSettingsAsync();

                if (IsDue(settings))
                {
                    _logger.LogInformation("ถึงรอบสำรอง DB อัตโนมัติแล้ว (ความถี่: {Frequency}) - กำลังสำรอง...", settings.Frequency);
                    await backupService.BackupNowAsync();
                }
            }
            catch (Exception ex)
            {
                // ไม่ให้ loop การสำรองอัตโนมัติตายทั้งตัวเพราะ error ชั่วคราวครั้งเดียว (เช่น disk เต็มแป๊บเดียว) -
                // รอรอบถัดไปแล้วลองใหม่
                _logger.LogError(ex, "สำรองข้อมูลตามกำหนดเวลาไม่สำเร็จ - จะลองใหม่รอบถัดไปใน {Minutes} นาที", CheckInterval.TotalMinutes);
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // ปกติตอน service กำลังจะปิด (stoppingToken ถูก cancel) - ไม่ใช่ error
            }
        }
    }

    private static bool IsDue(BackupSettings settings)
    {
        if (settings.LastBackupAt is null)
        {
            return true; // ยังไม่เคยสำรองเลยตั้งแต่ติดตั้ง - สำรองทันทีรอบแรกที่ service เริ่ม
        }

        var elapsed = DateTime.UtcNow - settings.LastBackupAt.Value;
        return settings.Frequency == BackupFrequency.Daily
            ? elapsed >= TimeSpan.FromHours(23) // เผื่อ 1 ชม. กันพลาดรอบถ้า restart ช้าไปนิดหน่อย
            : elapsed >= TimeSpan.FromDays(6.9); // รายสัปดาห์ เผื่อเวลาเหมือนกัน
    }
}
