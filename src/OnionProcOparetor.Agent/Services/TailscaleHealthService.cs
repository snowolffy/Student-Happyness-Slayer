using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เฝ้าดูว่าเส้นทางสำรอง (Tailscale) ยังใช้งานได้จริงหรือไม่ (2026-08-31)
///
/// ===================== ทำไมต้องมี =====================
/// เครื่อง lab ทุกเครื่องมี Reboot Restore Rx และ Tailscale เก็บ node key ของตัวเองไว้ที่
/// C:\ProgramData\Tailscale\tailscaled.state ซึ่งอยู่บนไดรฟ์ที่ถูก freeze เหมือนกันทุกอย่าง
/// แปลว่า:
///
///   ครูสั่ง RunTailscaleLogin -> เครื่องเข้า tailnet ได้จริง -> ใช้งานได้ทั้งวัน
///   -> reboot -> node key หายไปกับ restore -> tailscale.exe ยังอยู่แต่ไม่ได้ login แล้ว
///
/// และไม่มีโค้ดตรงไหนใน Agent ที่ re-enroll ให้เองตอนบูต (RunTailscaleLogin เป็น command ที่ครู
/// สั่งเองครั้งเดียว ส่วน GetTailscaleStatus ก็เป็น on-demand ตอนครูเปิดหน้าต่างดู)
///
/// อาการที่ตามมาคือแบบที่แย่ที่สุดของ failover: <b>มันพังเงียบ และไปรู้ตอนที่ต้องใช้จริง</b>
/// Worker จะยังสลับไป FallbackBaseUrl ตามปกติเมื่อ primary ล่ม (SwitchToFallback ไม่ได้ตรวจว่า
/// เส้นทางสำรองใช้ได้ไหมก่อน) แต่ IP ปลายทางนั้นไม่มีทางไปถึงเพราะ interface ไม่ได้ขึ้นมาเลย
///
/// ===================== สิ่งที่ service นี้ทำ (และไม่ทำ) =====================
/// ทำ: ตรวจเป็นระยะว่า "ติดตั้งแล้วแต่ยังไม่ได้ login" หรือไม่ แล้วส่งเสียงให้ครูเห็นที่ Console
///     ผ่าน log กลาง - รายงานเฉพาะตอน "สถานะเปลี่ยน" ไม่ใช่ทุกรอบ (ไม่งั้นจะกลบ log อื่นหมด)
///
/// ไม่ทำ: re-enroll ให้อัตโนมัติ - ต้องมี authkey ซึ่งโดยดีไซน์เดิมตั้งใจไม่เก็บไว้บนเครื่องเลย
///        (ดู HandleRunTailscaleLoginAsync) การเก็บ authkey ไว้ที่ Server แล้วส่งมาให้อัตโนมัติ
///        เป็นฟีเจอร์ที่มีนัยด้านความปลอดภัยของตัวเอง - บันทึกไว้เป็นแผนใน HARDENING-REPORT
///
/// วิธีแก้ที่ถูกต้องจริงๆ ของอาการนี้คือฝั่งกระบวนการ ไม่ใช่โค้ด: enroll ตอน freeze ปิดอยู่
/// แล้ว commit baseline ใหม่ เพื่อให้ tailscaled.state ติดไปกับ baseline - service นี้มีไว้เพื่อ
/// ให้ "รู้ตัว" ว่ายังไม่ได้ทำ/หลุดไปแล้ว ไม่ใช่เพื่อแก้แทน
/// </summary>
public sealed class TailscaleHealthService : BackgroundService
{
    /// <summary>รอให้เครือข่าย/tailscaled สตาร์ทให้เรียบร้อยก่อนตรวจครั้งแรก</summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    private readonly ILogger<TailscaleHealthService> _logger;
    private readonly ServerSettings _settings;
    private readonly ServerClient _serverClient;
    private readonly ClientState _state;

    /// <summary>สถานะที่รายงานไปแล้วรอบก่อน - รายงานซ้ำเฉพาะตอนเปลี่ยนเท่านั้น</summary>
    private bool? _lastReportedEnrolled;

    public TailscaleHealthService(
        ILogger<TailscaleHealthService> logger,
        IOptions<ServerSettings> settings,
        ServerClient serverClient,
        ClientState state)
    {
        _logger = logger;
        _settings = settings.Value;
        _serverClient = serverClient;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ไม่ได้ตั้ง FallbackBaseUrl ไว้ = โรงเรียนนี้ไม่ได้ใช้ Tailscale เป็นเส้นทางสำรอง
        // ไม่ต้องตรวจอะไรเลย และที่สำคัญกว่าคือต้องไม่ไปเตือนเรื่องที่ไม่เกี่ยวกับเขา
        if (string.IsNullOrWhiteSpace(_settings.FallbackBaseUrl))
        {
            _logger.LogInformation("ไม่ได้ตั้งค่า FallbackBaseUrl - ข้าม TailscaleHealthService ทั้งหมด");
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ตรวจสถานะ Tailscale ไม่สำเร็จ - จะลองใหม่รอบหน้า");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        var exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");

        if (!File.Exists(exePath))
        {
            // ตั้ง FallbackBaseUrl ไว้แต่ไม่มี Tailscale เลย = ติดตั้งไม่สมบูรณ์ (Scheduled Task ที่
            // ลง Tailscale แบบ deferred อาจล้มเหลว) - เป็นคนละปัญหากับ "ลงแล้วแต่ไม่ได้ login"
            await ReportAsync(false, "ตั้งค่า FallbackBaseUrl ไว้ แต่ไม่พบ tailscale.exe บนเครื่องนี้ - การติดตั้ง Tailscale อาจไม่สำเร็จ", ct);
            return;
        }

        var tailnetIp = await GetTailnetIpAsync(exePath, ct);
        var enrolled = !string.IsNullOrWhiteSpace(tailnetIp);

        if (enrolled)
        {
            await ReportAsync(true, $"เชื่อมต่อ tailnet อยู่ (IP: {tailnetIp})", ct);
            return;
        }

        var locked = ReadAlwaysOnPolicy();
        await ReportAsync(false,
            "ติดตั้ง Tailscale แล้วแต่ยังไม่ได้เข้า tailnet (ไม่มี tailnet IP) - เส้นทางสำรองใช้งานไม่ได้จริงในตอนนี้ "
            + "สาเหตุที่พบบ่อยที่สุดคือ node key หายไปกับ Reboot Restore Rx หลัง reboot "
            + "แก้โดยสั่ง RunTailscaleLogin ใหม่ขณะที่ freeze ปิดอยู่ แล้ว commit baseline ใหม่ "
            + $"(AlwaysOn policy ตอนนี้: {(locked ? "เปิดอยู่" : "ปิดอยู่")})",
            ct);
    }

    private async Task<string> GetTailnetIpAsync(string exePath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "ip -4",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null) return string.Empty;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return process.ExitCode == 0 ? (await stdOutTask).Trim() : string.Empty;
        }
        catch
        {
            // tailscaled ยังไม่ขึ้น / ไม่มีสิทธิ์ / ฯลฯ - ถือว่ายังไม่พร้อม ไม่ใช่ error ที่ต้อง log ดังๆ
            return string.Empty;
        }
    }

    private bool ReadAlwaysOnPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Tailscale");
            return key?.GetValue("AlwaysOn.Enabled") is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// รายงานเฉพาะตอนสถานะเปลี่ยนจากรอบก่อน - ส่งเป็น LogEntry ผ่านช่องทางเดิมที่ Console แสดงอยู่แล้ว
    /// (ไม่เพิ่ม endpoint/ตารางใหม่เพื่อเรื่องนี้อย่างเดียว) ครูจึงเห็นได้จากหน้า log ปกติ
    /// </summary>
    private async Task ReportAsync(bool enrolled, string detail, CancellationToken ct)
    {
        if (_lastReportedEnrolled == enrolled)
        {
            return;
        }

        _lastReportedEnrolled = enrolled;

        if (enrolled)
        {
            _logger.LogInformation("Tailscale: {Detail}", detail);
        }
        else
        {
            _logger.LogWarning("Tailscale: {Detail}", detail);
        }

        var entry = new LogEntryDto
        {
            ClientGuid = _state.ClientGuid,
            MachineName = Environment.MachineName,
            ProcessPath = "[TAILSCALE]",
            DetectedPublisher = enrolled ? "TAILSCALE_OK" : "TAILSCALE_NOT_ENROLLED",
            WasSignedMatch = false,
            MatchedRuleName = "Tailscale Fallback Health",
            ActionTaken = detail
        };

        _state.AddLog(entry);
        await _serverClient.SendLogAsync(entry, ct);
    }
}
