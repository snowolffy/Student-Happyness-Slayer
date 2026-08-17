namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// รายงาน "ผลจริง" ของ command กลับไปหา Server หลัง Agent process เสร็จ - ต่างจาก
/// ServerClient.AckCommandAsync เดิมที่บอกแค่ว่า "ได้รับ command แล้ว" (delivery) ไม่ได้บอกว่า
/// action ที่สั่งไปทำได้จริงหรือไม่
///
/// ใช้ pattern SignalR-first + HTTP-fallback เหมือนทั้งระบบ:
/// - ถ้า SignalR ต่ออยู่ → เรียก hub method "ReportCommandResult" (เร็วกว่า ไม่ต้องเปิด HTTP
///   connection ใหม่ และ Server push ต่อให้ Console ได้ทันทีในทริปเดียว)
/// - ถ้าไม่ได้ต่อ/ส่งไม่สำเร็จ → POST /api/commands/{id}/result (ปลายทางเดียวกันเป๊ะฝั่ง Server
///   คือ CommandResultRecorder ตัวเดียวกัน)
///
/// **เรื่องโครงสร้าง DI ที่ต้องระวัง:** LabHubConnection ต้อง inject CommandProcessor อยู่แล้ว
/// (เพื่อรับ ReceiveCommand) ถ้าให้ CommandProcessor inject LabHubConnection กลับไปตรงๆ เพื่อ
/// ส่งผลจะกลายเป็น circular dependency ทันที (DI container throw ตอน resolve) เลยใช้ class
/// ตัวกลางนี้ที่ไม่ขึ้นกับใครนอกจาก ServerClient แล้วให้ LabHubConnection มา "เสียบ" ตัวส่ง
/// SignalR ให้ทีหลังตอน StartAsync (property SignalRSender) แทน - ตัดวงจรตรงจุดนี้จุดเดียว
/// </summary>
public class CommandResultReporter
{
    private readonly ILogger<CommandResultReporter> _logger;
    private readonly ServerClient _serverClient;

    /// <summary>
    /// LabHubConnection เป็นคน set ให้ตอน StartAsync (และ set null คืนไม่ได้ - แค่คืน false
    /// ถ้า connection ไม่พร้อม) คืน true = ส่งผ่าน SignalR สำเร็จแล้ว ไม่ต้อง fallback
    /// </summary>
    public Func<int, bool, string?, CancellationToken, Task<bool>>? SignalRSender { get; set; }

    public CommandResultReporter(ILogger<CommandResultReporter> logger, ServerClient serverClient)
    {
        _logger = logger;
        _serverClient = serverClient;
    }

    /// <summary>
    /// ไม่ throw ไม่ว่ากรณีใด - ถ้ารายงานผลไม่สำเร็จทั้ง 2 ทาง Server จะ mark เป็น TimedOut
    /// ให้เองหลังครบเวลา (ดู CommandResultTimeoutService) ซึ่งเป็นผลลัพธ์ที่ถูกต้องอยู่แล้ว
    /// สำหรับสถานการณ์ "เครื่องนี้ติดต่อ server ไม่ได้"
    /// </summary>
    public async Task ReportAsync(int commandId, bool success, string? message, CancellationToken ct)
    {
        var signalRSender = SignalRSender;

        if (signalRSender is not null)
        {
            try
            {
                if (await signalRSender(commandId, success, message, ct))
                {
                    _logger.LogInformation(
                        "รายงานผลจริงของ command {CommandId} ผ่าน SignalR สำเร็จ (success: {Success})",
                        commandId, success);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "รายงานผลจริงของ command {CommandId} ผ่าน SignalR ไม่สำเร็จ - จะลองผ่าน HTTP แทน", commandId);
            }
        }

        await _serverClient.ReportCommandResultAsync(commandId, success, message, ct);
    }
}
