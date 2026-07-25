using System.Diagnostics;
using System.Globalization;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// จัดการ "หลังบ้าน" ของ VNC session ฝั่ง Agent เท่านั้น - port discovery + Windows Firewall exception +
/// เก็บ state ปัจจุบัน (CurrentPort/CurrentPassword) ให้ CommandProcessor อ่านไปตอบกลับ Console
///
/// ไม่ spawn/monitor winvnc.exe เองอีกต่อไป (ย้ายไปที่ OnionProcOparetor.AgentTray.Services.
/// VncProcessManager แล้ว) เพราะพิสูจน์แล้วจริงว่า Agent (Windows Service, Session 0) capture หน้าจอ
/// ของ user session ไม่ได้เลยตาม Windows Session 0 Isolation (ตั้งแต่ Vista) - winvnc.exe ที่ Agent
/// เคย spawn เอง connect+auth ผ่านหมดแต่ตอบ ServerInit ไม่ได้เพราะไม่เห็น desktop จริงๆ ของ user
///
/// Flow ปัจจุบัน: CommandProcessor สั่ง AgentTray ผ่าน named pipe (AgentTrayNotifier) ให้ start
/// winvnc.exe เอง (รันใน user session จริง) → AgentTray รายงาน PID+password กลับมาทาง local HTTP
/// callback (/vnc/session-result ใน Program.cs) → HandleAgentTrayStartedAsync ที่นี่ทำ port discovery
/// (netstat) + เปิด firewall rule ให้ port จริง (สิทธิ์ LocalSystem ทำได้ ต่างจาก AgentTray ที่รันเป็น
/// user ธรรมดาซึ่งมักไม่มีสิทธิ์แก้ firewall) แล้วเก็บ state ไว้ให้ CommandProcessor relay ต่อไป Console
/// </summary>
public sealed class VncSessionManager
{
    /// <summary>
    /// ชื่อ Windows Firewall rule ที่เปิด/ปิดแบบ dynamic ตาม port จริงที่ winvnc.exe listen อยู่ - ต้อง
    /// dynamic เพราะพิสูจน์แล้วว่า port จริงอาจไม่ตรงกับที่ตั้งใน ini ถ้ามี UltraVNC ตัวอื่นรันอยู่ก่อน
    /// บนเครื่อง - ใช้ชื่อเดียวกันเสมอกัน rule ค้างสะสมข้าม session
    /// </summary>
    private const string FirewallRuleName = "OnionProcOparetor Agent VNC";

    private readonly ILogger<VncSessionManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public int? CurrentPort { get; private set; }
    public string? CurrentPassword { get; private set; }

    public VncSessionManager(ILogger<VncSessionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// เรียกจาก /vnc/session-result หลัง AgentTray รายงานว่า start winvnc.exe สำเร็จ (PID+password) -
    /// เช็ค port จริงที่ listen อยู่ (ห้ามสมมติว่าตรงกับที่ตั้งใน ini - ดู comment ใน
    /// VncProcessManager.WriteBaseIni ฝั่ง AgentTray) แล้วเปิด firewall rule ให้ตรง port นั้น
    /// </summary>
    public async Task<(bool Success, int Port, string? FailureReason)> HandleAgentTrayStartedAsync(int pid, string password, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var actualPort = await DiscoverListeningPortAsync(pid, ct);
            if (actualPort is null)
            {
                _logger.LogError("VncSessionManager: AgentTray start winvnc.exe สำเร็จ (PID: {Pid}) แต่หา listening port จริงไม่เจอภายในเวลาที่กำหนด", pid);
                return (false, 0, "หา listening port ของ winvnc.exe ไม่เจอ");
            }

            // เปิด firewall เฉพาะ port จริงที่เจอ - ต้องทำก่อนตอบกลับ Console เสมอ ไม่งั้น connect ได้
            // ผ่าน handshake ทุกอย่างยกเว้นโดน Windows Firewall บล็อก TCP เงียบๆ (อาการที่เจอจริงตอน
            // debug: RfbClient timeout แม้ winvnc.exe listen อยู่จริง)
            await EnsureFirewallExceptionAsync(actualPort.Value, ct);

            CurrentPort = actualPort.Value;
            CurrentPassword = password;

            _logger.LogInformation("VncSessionManager: VNC session พร้อมใช้งาน - Port: {Port} (PID: {Pid})", actualPort.Value, pid);
            return (true, actualPort.Value, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>เคลียร์ state + ปิด firewall rule (ไม่ได้แปลว่า winvnc.exe หยุดจริง - AgentTray เป็นคนคุม lifecycle ของ process เอง ที่นี่แค่ไม่ถือ state ค้างไว้เกินจำเป็น)</summary>
    public void ClearSession()
    {
        CurrentPort = null;
        CurrentPassword = null;
        RemoveFirewallExceptionSync();
    }

    /// <summary>
    /// Poll หา TCP port ที่ process นี้ listen อยู่จริง (retry สั้นๆ เพราะ winvnc.exe ใช้เวลาสักครู่หลัง
    /// Process.Start ก่อนจะ bind socket จริง) - UltraVNC เปิดทั้ง RFB port (5900+N) และ HTTP java-viewer
    /// port (5800+N) พร้อมกันเสมอ (แม้ตั้ง HTTPConnect=0 ใน ini ก็ยังไม่ถูก honor เหมือนกับ PortNumber -
    /// verify แล้วจากเครื่องทดสอบจริง) RFB port จะสูงกว่า HTTP port เสมอ (ต่างกันคงที่ 100) เลยเลือกค่า
    /// สูงสุดที่เจอ ปลอดภัยไม่ว่า HTTPConnect จะถูก honor ในอนาคตหรือไม่ (ถ้าถูก honor จะเหลือ port เดียว
    /// ก็ยังถูกต้องอยู่ดี)
    /// </summary>
    private async Task<int?> DiscoverListeningPortAsync(int pid, CancellationToken ct)
    {
        const int maxAttempts = 15;
        var delay = TimeSpan.FromMilliseconds(300);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var ports = await GetListeningPortsForProcessAsync(pid, ct);
            if (ports.Count > 0)
            {
                return ports.Max();
            }

            await Task.Delay(delay, ct);
        }

        return null;
    }

    /// <summary>
    /// อ่าน listening TCP port ของ PID ที่ระบุผ่าน netstat.exe ตรงๆ (เหมือนหลักการเดียวกับ createpassword.exe
    /// - ใช้ tool ของระบบอ่านค่าจริงแทนเดา/reimplement การ query ผ่าน P/Invoke) ใช้งานได้แม้ PID นั้นอยู่
    /// คนละ session กับ Agent (netstat ทำงานระดับ network stack ของทั้งเครื่อง ไม่ scope ตาม session)
    /// </summary>
    private static async Task<List<int>> GetListeningPortsForProcessAsync(int pid, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano -p TCP",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new List<int>();
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var ports = new List<int>();
        var pidText = pid.ToString(CultureInfo.InvariantCulture);

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // netstat -ano columns: Proto  LocalAddress  ForeignAddress  State  PID
            if (parts.Length != 5 || parts[3] != "LISTENING" || parts[4] != pidText)
            {
                continue;
            }

            var localAddr = parts[1];
            var colonIdx = localAddr.LastIndexOf(':');
            if (colonIdx >= 0 && int.TryParse(localAddr.AsSpan(colonIdx + 1), out var port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    /// <summary>
    /// เปิด Windows Firewall inbound rule สำหรับ port จริงที่ winvnc.exe listen อยู่ (dynamic - ไม่ใช่
    /// port ตายตัวอีกต่อไป ดู comment ที่ FirewallRuleName) ลบ rule เก่าชื่อเดียวกันก่อนเสมอ กัน rule
    /// ค้างชี้ port ผิดจาก session ก่อนหน้า - Agent รันเป็น LocalSystem service มีสิทธิ์แก้ firewall
    /// rule อยู่แล้วไม่ต้อง elevate เพิ่ม (ต่างจาก AgentTray ที่รันเป็น user ธรรมดา - เหตุผลที่ port
    /// discovery/firewall ทั้งหมดยังอยู่ฝั่ง Agent แม้ตัว winvnc.exe เองย้ายไป AgentTray แล้ว)
    /// </summary>
    private async Task EnsureFirewallExceptionAsync(int port, CancellationToken ct)
    {
        await RunNetshAsync($"advfirewall firewall delete rule name=\"{FirewallRuleName}\"", ct);

        var addExitCode = await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port}", ct);

        if (addExitCode != 0)
        {
            _logger.LogWarning(
                "VncSessionManager: เปิด Windows Firewall rule สำหรับ port {Port} ไม่สำเร็จ (exit code {ExitCode}) - Console อาจต่อ VNC ไม่ได้ถ้ามี firewall block อยู่",
                port, addExitCode);
        }
    }

    private static async Task<int> RunNetshAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
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

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    /// <summary>ลบ firewall rule แบบ sync - ตาม principle เดิมของฟีเจอร์นี้: ลด attack surface ไม่เปิดอะไรค้างทิ้งไว้เกินความจำเป็น</summary>
    private static void RemoveFirewallExceptionSync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall delete rule name=\"{FirewallRuleName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // เคลียร์ firewall rule ไม่ได้ก็ปล่อยผ่าน - ไม่ควรทำให้ agent ทำงานหลักพังเพราะเหตุนี้
        }
    }
}
