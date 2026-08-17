using Microsoft.Extensions.Configuration;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// Safety net: ถ้าส่ง pipe ไปหา AgentTray ไม่สำเร็จ ให้พยายามสตาร์ท AgentTray ขึ้นมาใหม่ใน
/// user session ที่ active อยู่ แล้วให้ผู้เรียก retry ส่งอีกครั้ง
///
/// ทำไมถึงจำเป็น: ถ้านักเรียนปิด AgentTray ทิ้ง (Task Manager → End task หรือกด "Exit Tray"
/// พลาด) ความสามารถทั้งหมดที่ต้องพึ่ง Tray จะตายเงียบๆ ทันที - lock screen ไม่ขึ้น, คำเตือน
/// ก่อน shutdown ไม่โผล่, broadcast ไม่เห็น - โดยที่ครูฝั่ง Console ไม่รู้เลยว่าเกิดอะไรขึ้น
/// (ก่อนหน้านี้ Agent แค่ log warning แล้วผ่านไป) HKCU Run key ที่ Tray ตั้งให้ตัวเองช่วยได้
/// แค่ตอน login ครั้งถัดไปเท่านั้น ไม่ช่วยอะไรเลยระหว่างคาบเรียนที่กำลังใช้งานอยู่
///
/// **กันสตาร์ทซ้ำ 2 ชั้น:**
/// 1. cooldown ที่นี่ - ไม่พยายามสตาร์ทถี่กว่า RecoveryCooldown (กัน command รัวๆ ที่ fail
///    ทุกตัวสั่งสตาร์ทเป็นสิบครั้งซ้อน)
/// 2. single-instance mutex ฝั่ง AgentTray เอง - instance ที่ 2 จะปิดตัวเองทันที (กันเคสที่
///    Tray ยังรันอยู่จริงแต่ pipe ชนกันพอดีตอนนั้น แล้วเราเข้าใจผิดว่ามันตายไปแล้ว)
/// </summary>
public class AgentTrayRecoveryService
{
    /// <summary>
    /// เว้นระยะขั้นต่ำระหว่างการพยายามสตาร์ทแต่ละครั้ง - 30 วิพอที่จะกันการสตาร์ทรัวๆ แต่ยัง
    /// สั้นพอที่ command ถัดไปในคาบเดียวกันจะได้ Tray ที่ recover แล้วใช้งาน
    /// </summary>
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// รอให้ AgentTray สตาร์ทจนเปิด named pipe listener เสร็จก่อน retry - WPF app เย็นๆ ใช้เวลา
    /// ประมาณ 1-2 วิกว่าจะถึง OnStartup ที่เรียก BroadcastPipeListener.Start()
    /// ตั้งไว้ 2.5 วิเป็นการประนีประนอมระหว่าง "รอนานพอ" กับ "ไม่ค้าง CommandProcessor นาน"
    /// </summary>
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMilliseconds(2500);

    private readonly ILogger<AgentTrayRecoveryService> _logger;
    private readonly UserSessionLauncher _sessionLauncher;
    private readonly string? _configuredTrayPath;

    private readonly object _lock = new();
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public AgentTrayRecoveryService(
        ILogger<AgentTrayRecoveryService> logger,
        UserSessionLauncher sessionLauncher,
        IConfiguration configuration)
    {
        _logger = logger;
        _sessionLauncher = sessionLauncher;
        _configuredTrayPath = configuration["AgentTray:ExecutablePath"];
    }

    /// <summary>
    /// พยายาม recover AgentTray - คืน true ถ้าเพิ่งสตาร์ทสำเร็จ (ผู้เรียกควร retry ส่ง pipe ต่อ)
    /// คืน false ถ้าไม่ได้พยายาม (ติด cooldown) หรือพยายามแล้วไม่สำเร็จ
    ///
    /// รอ StartupGracePeriod ให้ในตัวก่อน return true แล้ว - ผู้เรียก retry ได้ทันทีเลย
    /// </summary>
    public async Task<bool> TryRecoverAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            var sinceLastAttempt = DateTime.UtcNow - _lastAttemptUtc;

            if (sinceLastAttempt < RecoveryCooldown)
            {
                _logger.LogWarning(
                    "AgentTray auto-recover: ข้ามรอบนี้ - เพิ่งพยายามสตาร์ทไปเมื่อ {Seconds:F0} วิที่แล้ว (cooldown {Cooldown} วิ)",
                    sinceLastAttempt.TotalSeconds, RecoveryCooldown.TotalSeconds);
                return false;
            }

            _lastAttemptUtc = DateTime.UtcNow;
        }

        var trayPath = ResolveTrayExecutablePath();

        if (trayPath is null)
        {
            _logger.LogError(
                "AgentTray auto-recover ล้มเหลว: หา OnionProcOparetor.AgentTray.exe ไม่เจอ " +
                "(ลองหาจาก config 'AgentTray:ExecutablePath' และ path มาตรฐานข้างๆ Agent แล้ว)");
            return false;
        }

        _logger.LogWarning(
            "AgentTray auto-recover: ติดต่อ AgentTray ไม่ได้ - กำลังพยายามสตาร์ทใหม่จาก '{TrayPath}'", trayPath);

        var launched = _sessionLauncher.TryLaunchInActiveUserSession(trayPath);

        if (!launched)
        {
            _logger.LogError("AgentTray auto-recover ล้มเหลว: สตาร์ท '{TrayPath}' ใน user session ไม่สำเร็จ", trayPath);
            return false;
        }

        _logger.LogInformation(
            "AgentTray auto-recover: สตาร์ทสำเร็จ - รอ {GraceMs} ms ให้ Tray เปิด named pipe listener ก่อนลองส่งซ้ำ",
            StartupGracePeriod.TotalMilliseconds);

        try
        {
            await Task.Delay(StartupGracePeriod, ct);
        }
        catch (OperationCanceledException)
        {
            return false; // service กำลังหยุด - ไม่ต้อง retry ต่อ
        }

        return true;
    }

    /// <summary>
    /// หา path ของ AgentTray.exe ตามลำดับความน่าเชื่อถือ:
    /// 1. config 'AgentTray:ExecutablePath' ใน appsettings.json (ชนะทุกอย่าง - เผื่อ layout แปลก)
    /// 2. '..\ClientTray\OnionProcOparetor.AgentTray.exe' - layout จริงของ installer
    ///    (installer วาง Agent ไว้ที่ {app}\Client\ และ Tray ไว้ที่ {app}\ClientTray\)
    /// 3. โฟลเดอร์เดียวกับ Agent - layout ตอน dev/รันจาก build output
    /// </summary>
    private string? ResolveTrayExecutablePath()
    {
        const string trayExeName = "OnionProcOparetor.AgentTray.exe";

        if (!string.IsNullOrWhiteSpace(_configuredTrayPath))
        {
            if (File.Exists(_configuredTrayPath))
            {
                return _configuredTrayPath;
            }

            _logger.LogWarning(
                "config 'AgentTray:ExecutablePath' ชี้ไปที่ '{ConfiguredPath}' ซึ่งไม่มีไฟล์อยู่จริง - จะลองหาจาก path มาตรฐานแทน",
                _configuredTrayPath);
        }

        var agentDirectory = AppContext.BaseDirectory;

        var candidates = new[]
        {
            // layout ของ installer: {app}\Client\ (Agent) กับ {app}\ClientTray\ (Tray)
            Path.GetFullPath(Path.Combine(agentDirectory, "..", "ClientTray", trayExeName)),

            // layout ตอน dev - อยู่โฟลเดอร์เดียวกัน
            Path.Combine(agentDirectory, trayExeName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        _logger.LogDebug("หา AgentTray.exe ไม่เจอใน path ที่ลอง: {Candidates}", string.Join(" | ", candidates));
        return null;
    }
}
