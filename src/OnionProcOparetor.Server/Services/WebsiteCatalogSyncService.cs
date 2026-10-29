using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// เดินรอบ sync catalog เป็นระยะ (2026-09-12, ทุก 24 ชม. ตามที่ user เลือก - "sync อัตโนมัติ")
/// คู่ขนานกับ MainDbRetentionService ในเรื่องโครงสร้าง (PeriodicTimer + IServiceScopeFactory +
/// try/catch ต่อรอบ) ตัวจริงที่ทำงาน fetch/parse/diff อยู่ที่ WebsiteCatalogSyncer - ตัวนี้แค่เป็น
/// ตัวเดินเวลาให้ เพื่อให้ endpoint "sync-now" เรียก WebsiteCatalogSyncer ตรงๆ ได้โดยไม่ต้องพึ่ง
/// BackgroundService (ซึ่งไม่มีทาง invoke ตามต้องการจาก HTTP request ได้)
/// </summary>
public class WebsiteCatalogSyncService : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebsiteCatalogSyncService> _logger;

    public WebsiteCatalogSyncService(IServiceScopeFactory scopeFactory, ILogger<WebsiteCatalogSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval);

        // รันทันทีตอน start ครั้งแรกด้วย - ไม่งั้น source ที่เพิ่งเพิ่มต้องรอ 24 ชม. กว่าจะเห็นผล
        // ถ้าไม่ได้กด "Sync Now" เอง
        await SyncAllOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncAllOnceAsync(stoppingToken);
        }
    }

    private async Task SyncAllOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syncer = scope.ServiceProvider.GetRequiredService<WebsiteCatalogSyncer>();

            var sources = await db.WebsiteCatalogSources
                .Where(s => s.IsEnabled)
                .ToListAsync(ct);

            foreach (var source in sources)
            {
                // แต่ละ source ผิดพลาดได้อิสระจากกัน - SyncSourceAsync จัดการ try/catch ของตัวเองแล้ว
                // ไม่ปล่อย exception ออกมาที่นี่เพื่อไม่ให้ source หนึ่งพังแล้วรอบนี้ข้าม source ที่เหลือ
                await syncer.SyncSourceAsync(db, source, ct);
            }

            if (sources.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebsiteCatalogSyncService: รอบ sync นี้ล้มเหลวทั้งชุด - ข้ามไปรอบถัดไป");
        }
    }
}
