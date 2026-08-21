using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สแกนเป็นระยะลบแถว Activity Log ที่เก่ากว่า RetentionDays (ตามที่ผู้ใช้ระบุ: 7 วัน) - แยกจาก
/// ระบบหลักโดยตั้งใจ (ActivityLogDbContext มี volume สูงกว่า ต้องมี auto-cleanup ไม่งั้น DB โตไม่มีที่สิ้นสุด)
///
/// scan ทุก 1 ชั่วโมงพอสำหรับ retention 7 วัน (ไม่ต้องละเอียดกว่านี้) - pattern เดียวกับ
/// ClientOnlineStatusMonitorService/CommandResultTimeoutService (BackgroundService + PeriodicTimer)
/// </summary>
public class ActivityLogCleanupService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);
    private const int RetentionDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityLogCleanupService> _logger;

    public ActivityLogCleanupService(IServiceScopeFactory scopeFactory, ILogger<ActivityLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        // ล้างรอบแรกทันทีตอน start ด้วย (ไม่ต้องรอครบ 1 ชั่วโมงแรกก่อน) - เผื่อ server เพิ่ง
        // restart หลังปิดค้างไปหลายวัน
        await CleanupOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupOnceAsync(stoppingToken);
        }
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ActivityLogDbContext>();

            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var deletedCount = await db.ActivityLogEntries
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "ActivityLogCleanupService: ลบ activity log ที่เก่ากว่า {RetentionDays} วันไปแล้ว {DeletedCount} แถว",
                    RetentionDays, deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActivityLogCleanupService: cleanup รอบนี้ล้มเหลว - ข้ามไปรอบถัดไป");
        }
    }
}
