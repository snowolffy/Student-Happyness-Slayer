using System.Diagnostics;
using System.Text.Json;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// Shells out to tailscale.exe the same way OnionProcOparetor.Agent/Services/TailscaleHealthService.cs
/// already does (ProcessStartInfo + RedirectStandardOutput) - there is no supported .NET Tailscale
/// client library, and the WPF Console/Agent side of this codebase already established "just run the
/// CLI" as the accepted pattern here, so this follows it rather than introducing a new approach.
///
/// This service has two jobs, both about the "gate access via the tailnet, not app code" design decided
/// for OnlineConsole (see the approved plan):
///   1. ResolveOwnTailnetIpAsync() - used once at startup so Kestrel can bind to this machine's own
///      tailnet interface instead of ListenAnyIP. That alone makes the login page unreachable to anyone
///      not on the tailnet - no auth-adjacent code needed for that requirement.
///   2. WhoIsAsync(remoteIp) - used per-request to answer "who is this visitor on the tailnet", shown in
///      the sidebar footer so whoever is logged in can see what device/identity they're connecting as.
/// </summary>
public class TailscaleGatewayService
{
    private readonly ILogger<TailscaleGatewayService> _logger;

    public TailscaleGatewayService(ILogger<TailscaleGatewayService> logger)
    {
        _logger = logger;
    }

    private static string TailscaleExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");

    /// <summary>
    /// รันตอน startup เท่านั้น (ก่อน Kestrel bind) - เรียกแบบ blocking ตรงนี้ตั้งใจ (ไม่ใช่ BackgroundService)
    /// เพราะ Kestrel ต้องรู้ IP ที่จะ bind ก่อนจะเริ่ม accept connection ได้เลย ไม่มีทาง "bind ทีหลัง"
    /// </summary>
    public async Task<string?> ResolveOwnTailnetIpAsync(CancellationToken ct = default)
    {
        if (!File.Exists(TailscaleExePath))
        {
            _logger.LogError("ไม่พบ tailscale.exe ที่ {Path} - ติดตั้ง Tailscale บนเครื่องนี้ก่อนรัน OnlineConsole", TailscaleExePath);
            return null;
        }

        var output = await RunTailscaleAsync("ip -4", ct);
        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    /// <summary>
    /// ดึงตัวตนบน tailnet ของ IP ที่ระบุ (เช่น IP ของคนที่กำลังเปิดหน้านี้อยู่) - ใช้โชว์ใน sidebar footer
    /// ว่า "เชื่อมต่อ Tailscale ในฐานะใครอยู่" คืน null เงียบๆ ถ้าใช้ไม่ได้ (เช่น dev mode ที่ bind loopback
    /// ไม่ใช่ tailnet IP จริง) - ไม่ควรทำให้หน้าเว็บพังเพราะแค่แสดงข้อมูลเสริมไม่ได้
    /// </summary>
    public async Task<string?> WhoIsAsync(string remoteIp, CancellationToken ct = default)
    {
        if (!File.Exists(TailscaleExePath) || string.IsNullOrWhiteSpace(remoteIp))
        {
            return null;
        }

        var json = await RunTailscaleAsync($"whois --json {remoteIp}", ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? deviceName = null;
            if (root.TryGetProperty("Node", out var node) && node.TryGetProperty("ComputedName", out var computedName))
            {
                deviceName = computedName.GetString();
            }

            string? loginName = null;
            if (root.TryGetProperty("UserProfile", out var userProfile) && userProfile.TryGetProperty("LoginName", out var login))
            {
                loginName = login.GetString();
            }

            if (deviceName is null && loginName is null)
            {
                return null;
            }

            return loginName is null ? deviceName : $"{deviceName} ({loginName})";
        }
        catch (JsonException ex)
        {
            // รูปแบบ JSON ของ `tailscale whois` อาจเปลี่ยนได้ระหว่าง Tailscale version - อ่านแบบ defensive
            // (TryGetProperty ด้านบน) แล้ว log ไว้เฉยๆ ถ้า parse ไม่ได้เลย ไม่ throw ต่อให้หน้าเว็บพัง
            _logger.LogWarning(ex, "แปลงผล tailscale whois --json ไม่สำเร็จ");
            return null;
        }
    }

    private async Task<string?> RunTailscaleAsync(string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = TailscaleExePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
                return null;
            }

            return process.ExitCode == 0 ? await stdOutTask : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "เรียก tailscale.exe {Arguments} ไม่สำเร็จ", arguments);
            return null;
        }
    }
}
