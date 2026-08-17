using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สแกนเป็นระยะหา command ที่ส่งไปแล้วแต่ Agent ไม่เคยรายงาน "ผลจริง" กลับมาเลยภายในเวลาที่
/// กำหนด แล้ว mark เป็น TimedOut พร้อม push ให้ Console ที่เปิดหน้า report อยู่เห็นทันที
///
/// ทำไมต้องอยู่ฝั่ง Server ไม่ใช่ฝั่ง Console: ครูปิดหน้า report ไปแล้ว (หรือปิด Console ทั้งตัว)
/// command ที่ค้างต้องถูกสรุปผลอยู่ดี ไม่งั้นจะค้างเป็น Pending ตลอดกาลใน DB แล้วครูที่เปิดดู
/// ทีหลังจะเห็นสถานะที่ไม่มีวันเปลี่ยน - แถมถ้าให้ Console เป็นคนตัดสิน timeout เอง ครู 2 คน
/// ที่เปิดอยู่พร้อมกันอาจตัดสินไม่ตรงกันด้วย
///
/// ชั้นเสริมเท่านั้น: ถ้า service นี้ไม่ทำงาน ระบบอื่นยังปกติทุกอย่าง แค่ command ที่เงียบหาย
/// จะค้างเป็น Pending แทนที่จะกลายเป็น TimedOut
/// </summary>
public class CommandResultTimeoutService : BackgroundService
{
    /// <summary>ค่า default ถ้าไม่ตั้งใน appsettings.json - ตรงกับที่โจทย์กำหนด (30 วิ)</summary>
    public const int DefaultResultTimeoutSeconds = 30;

    /// <summary>
    /// สแกนถี่กว่า timeout มาก (5 วิ) เพื่อให้ครูเห็นสถานะ Timeout เร็วที่สุดหลังเลยเวลาจริง
    /// ไม่ใช่รออีกรอบเต็มๆ - query มี index รองรับอยู่แล้ว (ExecutionStatus, CreatedAt)
    /// </summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<LabHub> _hub;
    private readonly ILogger<CommandResultTimeoutService> _logger;
    private readonly int _timeoutSeconds;

    public CommandResultTimeoutService(
        IServiceScopeFactory scopeFactory,
        Microsoft.AspNetCore.SignalR.IHubContext<LabHub> hub,
        IConfiguration configuration,
        ILogger<CommandResultTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;

        var configured = configuration.GetValue<int>("CommandTracking:ResultTimeoutSeconds", DefaultResultTimeoutSeconds);

        // กันค่า config ที่ตั้งพลาด (0 หรือค่าลบ = ทุก command กลายเป็น Timeout ทันทีที่สร้าง)
        _timeoutSeconds = configured > 0 ? configured : DefaultResultTimeoutSeconds;

        if (configured != _timeoutSeconds)
        {
            _logger.LogWarning(
                "CommandTracking:ResultTimeoutSeconds ตั้งไว้เป็น {Configured} ซึ่งใช้ไม่ได้ - ใช้ค่า default {Default} วิแทน",
                configured, _timeoutSeconds);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CommandResultTimeoutService เริ่มทำงาน - ถือว่า timeout ถ้าไม่มีผลจริงกลับมาภายใน {TimeoutSeconds} วิ (สแกนทุก {ScanSeconds} วิ)",
            _timeoutSeconds, ScanInterval.TotalSeconds);

        using var timer = new PeriodicTimer(ScanInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommandResultTimeoutService: สแกนรอบนี้ล้มเหลว - ข้ามไปรอบถัดไป");
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddSeconds(-_timeoutSeconds);

        var staleCommands = await db.ClientCommands
            .Where(c => c.ExecutionStatus == CommandExecutionStatus.Pending && c.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (staleCommands.Count == 0)
        {
            return;
        }

        foreach (var command in staleCommands)
        {
            command.ExecutionStatus = CommandExecutionStatus.TimedOut;
            command.ExecutedAt = DateTime.UtcNow;
            command.ExecutionMessage =
                $"ไม่มีผลตอบกลับจากเครื่องภายใน {_timeoutSeconds} วินาที (เครื่องอาจปิดอยู่ เน็ตหลุด หรือ Agent ไม่ทำงาน)";

            _logger.LogWarning(
                "Command {CommandId} ('{CommandType}') ของเครื่อง {ClientGuid} ไม่มีผลจริงกลับมาภายใน {TimeoutSeconds} วิ - mark เป็น TimedOut",
                command.Id, command.CommandType, command.ClientGuid, _timeoutSeconds);
        }

        await db.SaveChangesAsync(ct);

        // push หลัง save เท่านั้น - ถ้า push ก่อนแล้ว save ล้มเหลว Console จะเห็นสถานะที่ไม่มีใน DB
        foreach (var command in staleCommands)
        {
            await _hub.Clients.BroadcastCommandResult(command);
        }
    }
}
