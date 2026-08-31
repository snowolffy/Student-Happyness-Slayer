using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// Retention ของ DB หลัก (2026-08-31, จาก security audit) - คู่ขนานกับ ActivityLogCleanupService
/// ที่ดูแล DB ของ activity log อยู่แล้ว
///
/// ก่อนหน้านี้ DB หลักไม่มี retention เลยสักตาราง ทั้งที่ 2 ตารางนี้โตขึ้นเรื่อยๆ ไม่มีที่สิ้นสุด:
///
///   LogEntry  - POST /api/logs ไม่ต้อง auth (ตามดีไซน์เดิมของ agent-facing endpoint ทั้งหมด)
///               และไม่จำกัดความยาว field ใดๆ ใครก็ตามที่อยู่ใน LAN เดียวกันยิง log ปลอมเข้ามารัวๆ
///               จนดิสก์เต็มได้ ซึ่งดิสก์ก้อนเดียวกันนี้เก็บทั้ง DB หลักและ activity log = ล่มทั้งระบบ
///               retention ไม่ได้ปิดช่องนั้น (ต้องมี auth ต่อ agent ถึงจะปิดจริง - ดู PLAN ใน
///               HARDENING-REPORT) แต่ทำให้การใช้งานปกติไม่โตไม่มีขีดจำกัด และจำกัดความเสียหาย
///               ของการ flood ให้อยู่ในกรอบเวลาหนึ่ง แทนที่จะสะสมถาวร
///
///   AuthToken - เพิ่มแถวใหม่ทุกครั้งที่ครู login และไม่เคยถูกลบเลยแม้แต่ตอนหมดอายุ ปริมาณน้อย
///               (วันละไม่กี่แถว) แต่ token ที่หมดอายุแล้วไม่มีเหตุผลอะไรที่ต้องเก็บไว้ และการ
///               ลบทิ้งช่วยให้ตารางนี้เล็กพอที่จะสแกนเร็วเสมอตอน GetAuthorizedAdmin
///
/// LogRetentionDays ตั้งจาก appsettings.json ได้ (Retention:LogEntryDays) - ค่า default 90 วัน
/// เลือกจากบริบทจริง: 1 ภาคเรียน ยาวพอให้ครูย้อนดูเหตุการณ์ที่เกิดเมื่อเดือนก่อนได้ แต่ไม่เก็บ
/// ข้ามปีการศึกษาโดยไม่มีใครดู
/// </summary>
public class MainDbRetentionService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);
    private const int DefaultLogRetentionDays = 90;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MainDbRetentionService> _logger;
    private readonly int _logRetentionDays;

    public MainDbRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<MainDbRetentionService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configured = configuration.GetValue("Retention:LogEntryDays", DefaultLogRetentionDays);
        // 0 หรือติดลบ = ตั้งค่าผิด ไม่ใช่ "ปิด retention" - ถ้าอยากปิดจริงต้องตั้งค่าสูงๆ ไปเลย
        // ตีความว่า "ปิด" แล้วปล่อยโตไม่จำกัดจากค่าที่พิมพ์ผิดเป็นพฤติกรรมที่อันตรายเกินไป
        _logRetentionDays = configured > 0 ? configured : DefaultLogRetentionDays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

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
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var logCutoff = DateTime.UtcNow.AddDays(-_logRetentionDays);
            var deletedLogs = await db.LogEntries
                .Where(e => e.Timestamp < logCutoff)
                .ExecuteDeleteAsync(ct);

            // เผื่อเวลาไว้ 1 วันหลังหมดอายุก่อนลบจริง - กัน edge case ที่นาฬิกาเครื่อง server
            // ถูกปรับถอยหลังเล็กน้อย (เช่น NTP sync ครั้งแรกหลังเปลี่ยนถ่าน CMOS) แล้ว token ที่
            // ยังใช้งานได้อยู่จริงถูกลบทิ้งกลางคาบ
            var tokenCutoff = DateTime.UtcNow.AddDays(-1);
            var deletedTokens = await db.AuthTokens
                .Where(t => t.ExpiresAt < tokenCutoff)
                .ExecuteDeleteAsync(ct);

            if (deletedLogs > 0 || deletedTokens > 0)
            {
                _logger.LogInformation(
                    "MainDbRetentionService: ลบ log ที่เก่ากว่า {Days} วัน {Logs} แถว และ token ที่หมดอายุ {Tokens} แถว",
                    _logRetentionDays, deletedLogs, deletedTokens);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainDbRetentionService: cleanup รอบนี้ล้มเหลว - ข้ามไปรอบถัดไป");
        }
    }
}
