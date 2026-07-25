using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// จัดการ lifecycle ของ winvnc.exe (UltraVNC server, bundle มาเองใน VncBundle/ - ดู .csproj) - ย้ายมา
/// จาก Agent (Windows Service, Session 0) เพราะ Session 0 isolation ทำให้ process ใน Session 0 capture
/// หน้าจอของ user session ไม่ได้เลย ไม่ว่าจะ config ยังไงก็ตาม (verify แล้วจริงจากการ debug: winvnc.exe
/// ที่ Agent spawn เอง connect+auth ผ่านหมดแต่ตอบ ServerInit ไม่ได้เพราะไม่เห็น desktop) AgentTray รันใน
/// user session จริง เลยเป็นที่เดียวที่ capture หน้าจอได้
///
/// รับผิดชอบแค่ spawn/monitor/stop process + สร้าง password เท่านั้น - ไม่ทำ port discovery หรือ
/// firewall management (Agent ทำแทน หลังได้ PID กลับไปผ่าน local HTTP callback - ดู ClientApiClient
/// .PostVncSessionResultAsync) เพราะ AgentTray รันเป็น user ธรรมดา (มักไม่มีสิทธิ์แก้ Windows Firewall
/// ถ้าไม่ใช่ local admin) ในขณะที่ Agent รันเป็น LocalSystem มีสิทธิ์เต็มอยู่แล้ว
///
/// ไม่มี crash-auto-restart/circuit-breaker แบบที่ Agent เวอร์ชันเดิมเคยมี (ตัดออกโดยตั้งใจ - ลด scope
/// ให้เหมาะกับ AgentTray ที่เป็น per-user process อายุสั้นกว่า Agent service, ถ้า winvnc.exe ตายกลางคัน
/// การ request StartVncSession รอบถัดไปจาก Console จะ detect ว่าไม่ได้รันอยู่แล้ว start ใหม่ให้เอง)
/// </summary>
public sealed class VncProcessManager
{
    private const int VncPort = 5950;
    private const int PasswordLength = 8;

    private readonly string _bundleDir;
    private readonly string _winvncPath;
    private readonly string _createPasswordPath;
    private readonly string _iniPath;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Process? _vncProcess;

    public int? CurrentPid { get; private set; }
    public string? CurrentPassword { get; private set; }

    public VncProcessManager()
    {
        _bundleDir = Path.Combine(AppContext.BaseDirectory, "VncBundle");
        _winvncPath = Path.Combine(_bundleDir, "winvnc.exe");
        _createPasswordPath = Path.Combine(_bundleDir, "createpassword.exe");
        _iniPath = Path.Combine(_bundleDir, "ultravnc.ini");
    }

    /// <summary>
    /// idempotent: เรียกซ้ำตอน winvnc.exe รันอยู่แล้วจะ return ค่าปัจจุบันทันทีไม่ทำอะไรซ้ำ สร้าง
    /// password ใหม่ทุกครั้งที่ start จริง (ไม่ hardcode ไม่ reuse ข้าม session)
    /// </summary>
    public async Task<(bool Success, int Pid, string? Password, string? FailureReason)> EnsureStartedAsync(CancellationToken ct)
    {
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (IsRunningLocked())
            {
                return (true, _vncProcess!.Id, CurrentPassword, null);
            }

            if (!File.Exists(_winvncPath) || !File.Exists(_createPasswordPath))
            {
                return (false, 0, null, $"ไม่พบ winvnc.exe/createpassword.exe ที่ {_bundleDir}");
            }

            var password = GenerateRandomPassword();

            WriteBaseIni();

            var passwordSetOk = await RunCreatePasswordAsync(password, ct);
            if (!passwordSetOk)
            {
                return (false, 0, null, "createpassword.exe ตั้งรหัสผ่านลง ultravnc.ini ไม่สำเร็จ");
            }

            var pid = StartProcessLocked();
            if (pid is null)
            {
                return (false, 0, null, "start winvnc.exe ไม่สำเร็จ");
            }

            CurrentPassword = password;
            return (true, pid.Value, password, null);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>เรียกตอน AgentTray กำลังจะปิด (log off/exit) - กัน winvnc.exe ค้างเป็น orphan process หลัง user ออกจาก session ไปแล้ว (ไม่มี desktop ให้ serve ต่อได้อยู่ดี)</summary>
    public void Stop()
    {
        _lifecycleLock.Wait();
        try
        {
            if (_vncProcess is null)
            {
                return;
            }

            try
            {
                if (!_vncProcess.HasExited)
                {
                    _vncProcess.Kill(entireProcessTree: true);
                    _vncProcess.WaitForExit(5000);
                }
            }
            catch
            {
                // ปิดไม่สมบูรณ์ก็ปล่อยผ่าน - AgentTray กำลังจะ exit อยู่แล้ว
            }
            finally
            {
                _vncProcess.Dispose();
                _vncProcess = null;
                CurrentPid = null;
                CurrentPassword = null;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private bool IsRunningLocked()
    {
        if (_vncProcess is null)
        {
            return false;
        }

        try
        {
            return !_vncProcess.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// เขียน ultravnc.ini ขั้นต่ำสุด - PortNumber ตรงนี้เป็นแค่ค่าที่ "ขอ" เท่านั้น ไม่ได้แปลว่า
    /// winvnc.exe จะ listen ที่ port นี้จริง (Agent เป็นคนเช็ค port จริงหลัง start ผ่าน netstat)
    /// </summary>
    private void WriteBaseIni()
    {
        var content =
            "[ultravnc]\r\n" +
            $"PortNumber={VncPort}\r\n" +
            "AutoPortSelect=0\r\n" +
            "HTTPConnect=0\r\n" +
            "QueryAccept=0\r\n" +
            "QuerySetting=0\r\n";

        File.WriteAllText(_iniPath, content);
    }

    /// <summary>
    /// เข้ารหัสรหัสผ่านลง ultravnc.ini ผ่าน createpassword.exe ของ UltraVNC เอง - ห้ามใส่ -secure
    /// (เปิด UltraVNC's Secure Mode/DSM plugin extension นอกเหนือ RFB มาตรฐาน ทำให้ handshake ล้มเหลว
    /// หลัง ClientInit - verify แล้วจริงตอน debug ดู comment เดิมที่ VncSessionManager ฝั่ง Agent)
    /// </summary>
    private async Task<bool> RunCreatePasswordAsync(string password, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _createPasswordPath,
            Arguments = password,
            WorkingDirectory = _bundleDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    /// <summary>-multi = "Allow multiple instances (disables running check)" - จำเป็นเพราะเครื่องอาจมี UltraVNC ตัวอื่นรันอยู่ก่อนแล้ว (เช่น IT tool อื่น) ไม่งั้น port ที่ตั้งไว้จะไม่ถูก honor</summary>
    private int? StartProcessLocked()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _winvncPath,
                Arguments = "-multi -run",
                WorkingDirectory = _bundleDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                // simplified - ไม่มี auto-restart (ดู comment บนสุดของไฟล์) แค่เคลียร์ state ให้
                // request ถัดไป detect ว่าต้อง start ใหม่
                _vncProcess = null;
                CurrentPid = null;
                CurrentPassword = null;
            };

            if (!process.Start())
            {
                return null;
            }

            _vncProcess = process;
            CurrentPid = process.Id;
            return process.Id;
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateRandomPassword()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> randomBytes = stackalloc byte[PasswordLength];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[PasswordLength];
        for (var i = 0; i < PasswordLength; i++)
        {
            chars[i] = alphabet[randomBytes[i] % alphabet.Length];
        }

        return new string(chars);
    }
}
