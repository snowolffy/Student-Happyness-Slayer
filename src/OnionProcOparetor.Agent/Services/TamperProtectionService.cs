using System.Diagnostics;
using System.Text.Json;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ครึ่งหนึ่งของระบบ tamper protection ที่อยู่ฝั่ง Agent (อีกครึ่งคือ Scheduled Task ที่รัน
/// installer/CustomActions/agent-watchdog.ps1 - อ่าน header comment ที่นั่นก่อนเพื่อเข้าใจภาพรวม)
///
/// ทำ 2 อย่างที่ต้องมาจากในตัว Agent เท่านั้น:
///
/// 1. <b>Heartbeat</b> - เขียน timestamp ลง %ProgramData%\OnionProcOparetor\Agent\heartbeat.json
///    ทุก <see cref="HeartbeatIntervalSeconds"/> วินาที watchdog ใช้ค่านี้แยกแยะ "service ขึ้นว่า
///    Running แต่ process จริงถูก freeze/suspend/hollow ไว้" ออกจาก "ทำงานปกติ" ได้ - ซึ่ง
///    Get-Service มองไม่เห็นเลยเพราะ SCM รายงานแค่สถานะที่ลงทะเบียนไว้ ไม่ได้วัดว่าโค้ดยังเดินอยู่จริง
///
/// 2. <b>Self-heal ของ Scheduled Task</b> - ตรวจทุก <see cref="TaskCheckIntervalSeconds"/> วินาที
///    ว่า task ยังอยู่ ถ้าถูกลบไปก็สร้างใหม่ทันที จุดประสงค์คือทำให้ทั้ง 2 ฝั่งค้ำกันเอง:
///    ลบ task อย่างเดียว -> Agent สร้างคืนภายใน 1 นาที
///    ฆ่า Agent อย่างเดียว -> task เห็น heartbeat ค้าง แล้ว reboot ภายใน ~2 นาที
///    ต้องทำทั้งสองอย่างสำเร็จพร้อมกันในหน้าต่างแคบๆ ถึงจะรอด ซึ่งคือระดับความยากที่ตั้งใจไว้
///
/// ทุก path มี try/catch ครอบหมดโดยตั้งใจ: service นี้เป็น "ของเสริม" - ถ้ามันพัง Agent ตัวจริง
/// (enforcement/poll/command) ต้องทำงานต่อได้ตามปกติ ห้ามลาก host ลงไปด้วยเด็ดขาด
/// </summary>
public sealed class TamperProtectionService : BackgroundService
{
    private const int HeartbeatIntervalSeconds = 30;
    private const int TaskCheckIntervalSeconds = 60;
    private const string TaskName = "OnionProcOparetor Agent Watchdog";

    private readonly ILogger<TamperProtectionService> _logger;
    private readonly string _dataDir;
    private readonly string _heartbeatPath;

    // นับรอบ heartbeat เพื่อรู้ว่าถึงคิวเช็ค task หรือยัง แทนที่จะเปิด timer/loop ตัวที่ 2
    // (task check เรียก schtasks.exe ซึ่งเป็น subprocess - ไม่ควรยิงถี่เท่า heartbeat)
    private int _ticksSinceTaskCheck;

    private bool _loggedMissingScript;

    public TamperProtectionService(ILogger<TamperProtectionService> logger)
    {
        _logger = logger;
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "OnionProcOparetor", "Agent");
        _heartbeatPath = Path.Combine(_dataDir, "heartbeat.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TamperProtectionService เริ่มทำงาน - heartbeat ทุก {Hb} วิ, ตรวจ watchdog task ทุก {Task} วิ",
            HeartbeatIntervalSeconds, TaskCheckIntervalSeconds);

        // เตือนดังๆ ถ้า agent.key ไม่ได้มาจากตอนติดตั้ง - บนเครื่องที่มี disk freeze นี่แปลว่าไฟล์
        // อยู่นอก baseline และจะถูกสร้างใหม่ทุก reboot ทำให้ Server เห็น secret ไม่ตรงตลอดไป
        // (อาการที่จะเห็นคือ AgentSecretMismatch ใน Activity Log ทุกครั้งที่เครื่องบูต)
        if (AgentCredentials.WasGeneratedAtRuntime)
        {
            _logger.LogWarning(
                "agent.key ถูกสร้างขึ้นใหม่ตอน runtime (ไม่ได้ถูกสร้างตอนติดตั้ง) - ถ้าเครื่องนี้มี disk freeze " +
                "ไฟล์นี้จะหายทุก reboot และ Server จะเห็น agent secret ไม่ตรงทุกครั้ง " +
                "แก้โดยรัน install-watchdog.ps1 ใหม่ตอนที่ freeze ปิดอยู่ แล้ว commit baseline ใหม่");
        }

        // เช็ค task ทันทีตั้งแต่รอบแรก ไม่ต้องรอครบ interval - ครอบคลุมกรณีที่ CustomAction ตอน
        // ติดตั้งล้มเหลวเงียบๆ (Return="ignore") แล้วเครื่องนั้นจะไม่มี watchdog เลยตลอดกาล
        _ticksSinceTaskCheck = int.MaxValue / 2;

        while (!stoppingToken.IsCancellationRequested)
        {
            WriteHeartbeat();

            _ticksSinceTaskCheck += HeartbeatIntervalSeconds;
            if (_ticksSinceTaskCheck >= TaskCheckIntervalSeconds)
            {
                _ticksSinceTaskCheck = 0;
                await EnsureWatchdogTaskAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // ตอนหยุดแบบปกติ (uninstall/upgrade/reboot ที่สั่งจาก Console) ให้ลบ heartbeat ทิ้ง
        // ไม่ใช่ปล่อยค้างไว้ให้เก่าลงเรื่อยๆ - แต่เคสนั้น service ก็ไม่ได้ Running แล้ว watchdog
        // จะเข้าเงื่อนไข SERVICE_NOT_RUNNING แทน (SOFT) ซึ่งเป็นสิ่งที่ maintenance ticket คุมอยู่
        TryDeleteHeartbeat();
    }

    /// <summary>
    /// เขียนแบบ write-temp-then-move เพื่อกัน watchdog อ่านเจอไฟล์ที่เขียนค้างครึ่งทาง
    /// (จะกลายเป็น HEARTBEAT_UNREADABLE = soft finding ที่เป็น false-positive ล้วนๆ)
    /// </summary>
    private void WriteHeartbeat()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);

            var payload = JsonSerializer.Serialize(new
            {
                utc = DateTime.UtcNow.ToString("o"),
                pid = Environment.ProcessId,
                machine = Environment.MachineName
            });

            var tempPath = _heartbeatPath + ".tmp";
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, _heartbeatPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "เขียน heartbeat ไม่สำเร็จ - ถ้าเกิดต่อเนื่องเกิน HeartbeatStaleSeconds watchdog จะถือว่า Agent ตายและ reboot เครื่อง");
        }
    }

    private void TryDeleteHeartbeat()
    {
        try
        {
            if (File.Exists(_heartbeatPath))
            {
                File.Delete(_heartbeatPath);
            }
        }
        catch
        {
            // ไม่สำคัญพอที่จะ log - service กำลังจะปิดอยู่แล้ว
        }
    }

    private async Task EnsureWatchdogTaskAsync(CancellationToken ct)
    {
        try
        {
            if (await WatchdogTaskExistsAsync(ct))
            {
                return;
            }

            _logger.LogWarning("ไม่พบ Scheduled Task '{Task}' - กำลังสร้างใหม่ (อาจถูกลบโดยเจตนา)", TaskName);
            await ReinstallWatchdogTaskAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ตรวจ/ซ่อม watchdog task ไม่สำเร็จ");
        }
    }

    /// <summary>
    /// ใช้ schtasks.exe แทน TaskScheduler COM API เพราะ Agent เป็น .NET 10 บน Windows ที่ไม่ได้
    /// อ้าง TaskScheduler interop assembly ไว้ - subprocess ตัวเดียวทุก 1 นาทีถูกกว่าการเพิ่ม
    /// dependency ใหม่เข้ามาทั้งก้อนเพื่องานเดียวเท่านี้
    /// </summary>
    private async Task<bool> WatchdogTaskExistsAsync(CancellationToken ct)
    {
        var exitCode = await RunProcessAsync("schtasks.exe", $"/query /tn \"{TaskName}\"", TimeSpan.FromSeconds(20), ct);
        return exitCode == 0;
    }

    private async Task ReinstallWatchdogTaskAsync(CancellationToken ct)
    {
        // AppContext.BaseDirectory = ...\Onion ProcOparetor Agent\Client\ - สคริปต์อยู่ที่
        // ...\Onion ProcOparetor Agent\CustomActions\ (ดู layout ใน OnionProcOparetor.Agent.wxs)
        var installDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var scriptPath = Path.Combine(installDir, "CustomActions", "install-watchdog.ps1");

        if (!File.Exists(scriptPath))
        {
            if (!_loggedMissingScript)
            {
                _logger.LogError("ไม่พบ {Script} - สร้าง watchdog task ใหม่ไม่ได้ เครื่องนี้จะไม่มี tamper protection จนกว่าจะติดตั้งใหม่", scriptPath);
                _loggedMissingScript = true;
            }
            return;
        }

        _loggedMissingScript = false;

        var exitCode = await RunProcessAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -InstallDir \"{installDir}\"",
            TimeSpan.FromSeconds(90),
            ct);

        if (exitCode == 0)
        {
            _logger.LogInformation("สร้าง watchdog task ใหม่สำเร็จ");
        }
        else
        {
            _logger.LogError("install-watchdog.ps1 จบด้วย exit code {Code} - watchdog task อาจยังไม่ถูกสร้าง", exitCode);
        }
    }

    /// <summary>
    /// รัน subprocess แบบมี timeout เสมอ - ถ้า schtasks/powershell ค้าง (เคยเจอจริงตอนเครื่อง
    /// โหลดหนัก) ห้ามให้ loop นี้ค้างตามไปด้วย ไม่งั้น heartbeat จะหยุดเขียนแล้ว watchdog จะ
    /// reboot เครื่องเพราะบั๊กของตัวเราเอง ซึ่งเป็น failure mode ที่แย่ที่สุดของทั้งฟีเจอร์
    /// </summary>
    private async Task<int> RunProcessAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return -1;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ตายไปเองแล้วก็ไม่เป็นไร */ }
            _logger.LogWarning("{File} ไม่จบภายใน {Timeout} วิ - ถูก kill ทิ้ง", fileName, timeout.TotalSeconds);
            return -2;
        }
    }
}
