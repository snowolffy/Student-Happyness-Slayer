using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace OnionProcOparetor.UninstallHelper;

/// <summary>
/// เรียกจาก Add/Remove Programs โดยตรง (ดู UninstallString ใน installer\OnionProcOparetor.wxs's
/// synthetic ARP entry) - ต่างจาก msiexec /x ธรรมดาตรงที่ .exe ตัวนี้ถูก Explorer เรียกตรงๆ ผ่าน
/// CreateProcess เต็มรูปแบบ (ไม่ใช่ MSI engine's reduced-UI InstallExecuteSequence) จึงมีสิทธิ์เข้าถึง
/// desktop เต็มที่ โชว์ popup รหัสผ่านจริงๆ ได้ - แก้ปัญหาที่ check-uninstall-password.ps1 เจอเดิม
/// (WinForms popup crash เพราะไม่มี desktop access ตอนรันใน MSI's reduced UI level)
///
/// ไม่ได้ implement salted-hash verification ซ้ำที่นี่โดยเจตนา - แค่เก็บรหัสผ่านที่กรอกแล้วส่งต่อให้
/// msiexec /x ... UNINSTPWD=... เหมือนเดิมทุกประการ ให้ check-uninstall-password.ps1 (เรียกจาก
/// CheckUninstallPasswordHeadless CustomAction) เป็นคน verify จริงเหมือนเดิม - single source of
/// truth อันเดียว กันสอง implementation (C# ที่นี่ กับ PowerShell เดิม) เพี้ยนไม่ตรงกันในอนาคต
/// ตัวหน้าต่างนี้แค่เป็น "หน้าตาสวยๆ" ที่ห่อคำสั่ง msiexec เดิมไว้ แล้วอ่าน uninstall-auth-ok.flag
/// (marker file เดียวกับที่ LaunchCondition ใช้จริง) กลับมาเพื่อรู้ว่า exit code ที่ไม่ใช่ 0 หมายถึง
/// "รหัสผ่านผิด" หรือ "error อื่น" เพื่อโชว์ข้อความที่ตรงจุดกว่า generic MSI error
///
/// เพิ่มทางเลือกใหม่ (Server login แทนรหัสผ่าน local): ถ้าเครื่องนี้ตั้งรหัสผ่าน uninstall ไว้แล้ว
/// (_hasPassword) จะเสนอให้ login ด้วย Server admin account ก่อนเสมอ (เหมือนตอนเปิด AgentTray Status
/// panel) - login สำเร็จ = generate รหัสผ่านชั่วคราวแบบสุ่ม เขียนเป็น "session guard" แยกไฟล์ต่างหาก
/// (ไม่แตะ uninstall-guard.json เดิมเลย - รหัสผ่าน local ที่ตั้งไว้ตอน install ยังใช้ได้ตลอดไปไม่ว่าจะ
/// login กี่ครั้งก็ตาม) แล้วส่งรหัสชั่วคราวนั้นเป็น UNINSTPWD ต่อไปเหมือนเดิมทุกประการ -
/// check-uninstall-password.ps1 เป็นคน verify ทั้งสองทาง (local password เดิม + session guard ใหม่)
/// ยังคง single-source-of-truth เดิมไว้ ไม่มี hash verification ซ้ำใน C# ที่นี่เลย
///
/// ถ้าต่อ Server ไม่ได้ (offline) fallback อัตโนมัติไปตีความค่าในช่อง password เป็นรหัสผ่าน local
/// เดิมทันที - ไม่ fail-closed แบบ AgentTray's LoginWindow เพราะจะเป็น regression จากที่ทำงาน
/// offline ได้อยู่แล้วเดิม (ดู [[project_running_process_rule_editor]] ผลสำรวจ Part C ก่อนตัดสินใจนี้)
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan ServerLoginTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionGuardLifetime = TimeSpan.FromMinutes(5);

    private const string RegistryPath = @"SOFTWARE\OnionProcOparetor";
    private const string ClientGuidValueName = "ClientGuid";

    private readonly string _productCode;
    private readonly string _productDisplayName;
    private readonly string _installFolder;
    private readonly string _guardPath;
    private readonly string _sessionGuardPath;
    private readonly string _flagPath;
    private readonly bool _hasPassword;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        _productCode = args.Length > 1 ? args[1] : "";

        // args[2] (ถ้ามี) = ชื่อ product จริงที่ ARP UninstallString ของแต่ละ MSI ส่งมาเอง (ดู
        // OnionProcOparetor.Agent.wxs / OnionProcOparetor.ServerConsole.wxs) - .exe ตัวนี้เป็น binary
        // เดียวกันที่ bundle เข้าไปในทั้ง 2 MSI (Agent กับ Server+Console) โดยตั้งใจ (ดู comment ยาวใน
        // OnionProcOparetor.Agent.wxs สำหรับเหตุผล) แต่ก่อนหน้านี้ title/header เป็น "Onion ProcOparetor"
        // hardcoded เหมือนกันหมดไม่ว่าจะ uninstall ตัวไหน - ทำให้แยกไม่ออกว่ากำลังจะลบเครื่อง Agent หรือ
        // Server+Console อยู่ (บั๊กจริงที่เจอตอน live-test 2026-08-11) fallback เป็น "Onion ProcOparetor"
        // เฉยๆ ถ้าไม่มี arg นี้มา (เผื่อ ARP entry เก่าที่ build ก่อนหน้ายังไม่มี arg นี้)
        _productDisplayName = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : "Onion ProcOparetor";
        Title = $"Uninstall {_productDisplayName}";
        ProductNameText.Text = _productDisplayName;

        // .exe นี้ถูกติดตั้งที่ [INSTALLFOLDER]\UninstallHelper\ แต่ guard/flag file ทั้งคู่อยู่ที่
        // [INSTALLFOLDER] ตรงๆ (เขียนโดย setup-client.ps1/check-uninstall-password.ps1)
        //
        // บั๊กจริงที่เจอ (2026-08-20): เดิม derive _installFolder จาก AppContext.BaseDirectory ตรงๆ
        // (".." จาก exe's own folder) แต่ App.xaml.cs.RelaunchFromTempAndExit copy ตัวเองไป %TEMP%\...
        // แล้ว relaunch จากตรงนั้นก่อนถึงจุดนี้เสมอ (กันปัญหา lock ไฟล์ตัวเองตอน msiexec ลบ - ดู comment
        // ที่นั่น) เท่ากับ AppContext.BaseDirectory ของ instance ที่รันจริงกลายเป็น %TEMP%\<guid>\ ไม่ใช่
        // [INSTALLFOLDER]\UninstallHelper\ อีกต่อไป - _installFolder เดิมเลยคำนวณผิดเป็น %TEMP% เฉยๆ
        // ทำให้ TryReadGuard(_guardPath) หาไฟล์ guard ไม่เจอเสมอ (ไม่ว่าเครื่องนั้นจะตั้ง uninstall
        // password ไว้จริงหรือเปล่าก็ตาม) = _hasPassword เป็น false ตลอด = ไม่มีวันโชว์ช่องกรอกรหัสผ่านเลย
        // บนเครื่องที่ตั้งรหัสไว้จริง กด uninstall ไปก็โดน CheckUninstallPasswordHeadless (ซึ่งอ่าน path
        // ที่ถูกต้องจริงๆ เพราะรับมาเป็น literal string ผ่าน MSI property ไม่ได้พึ่ง exe's own location)
        // บล็อกไว้เงียบๆ ทุกครั้ง - ตรงกับอาการที่เจอจริง (ไม่มีช่องรหัสผ่าน, ลบไม่ได้)
        //
        // แก้ด้วยการรับ [INSTALLFOLDER] ตัวจริงมาเป็น arg ที่ 4 (args[3]) จาก UninstallString โดยตรง
        // (เหมือนที่ args[2] ส่ง product display name มาแล้ว) ไม่ต้อง derive จาก exe's own location อีก
        // ต่อไป - fallback กลับไปวิธีเดิมถ้าไม่มี arg นี้มา (เผื่อ ARP entry เก่าจาก build ก่อนหน้าที่ยัง
        // ไม่มี arg นี้ - จะพังแบบเดิมก็ไม่แย่ไปกว่าที่เป็นอยู่)
        _installFolder = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3])
            ? args[3]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        _guardPath = Path.Combine(_installFolder, "uninstall-guard.json");
        _sessionGuardPath = Path.Combine(_installFolder, "uninstall-session-guard.json");
        _flagPath = Path.Combine(_installFolder, "uninstall-auth-ok.flag");

        _hasPassword = TryReadGuard(_guardPath);

        // args[4] = "1" เฉพาะ product ที่รองรับ "delete data on uninstall" จริง (Server+Console เท่านั้น -
        // ดู OnionProcOparetor.ServerConsole.wxs) บั๊กจริงที่เจอ (2026-08-20): checkbox นี้เดิมโชว์เสมอไม่ว่า
        // product ไหน (เป็น .exe ตัวเดียวกันที่ bundle เข้าทั้ง 2 MSI) ทั้งที่ Agent product ไม่มี DELETEDATA
        // property/CustomAction รองรับเลย - ครูเห็น checkbox บนเครื่อง lab ที่ไม่มีอะไรให้มันลบจริง
        if (args.Length <= 4 || args[4] != "1")
        {
            DeleteDataCheckBox.Visibility = Visibility.Collapsed;
        }

        if (string.IsNullOrEmpty(_productCode))
        {
            IntroText.Text = "This tool must be launched from Add/Remove Programs, not run directly.";
            PasswordPanel.Visibility = Visibility.Collapsed;
            DeleteDataCheckBox.IsEnabled = false;
            ActionButton.IsEnabled = false;
            return;
        }

        IntroText.Text = $"Are you sure you want to remove {_productDisplayName} from this computer?";

        if (_hasPassword)
        {
            IntroText.Text = "This machine is uninstall-protected. Sign in with your Server admin account, " +
                "or leave the username blank and enter the local uninstall password if offline.";
            PasswordPanel.Visibility = Visibility.Visible;
        }
    }

    /// <summary>เช็คแค่ว่ามีการตั้งรหัสผ่านไว้จริงไหม (มีไฟล์ + มี salt/hash) - ไม่ verify ค่าเองที่นี่ (ดู comment บนคลาส)</summary>
    private static bool TryReadGuard(string guardPath)
    {
        try
        {
            if (!File.Exists(guardPath))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(guardPath));
            var root = doc.RootElement;
            var hasSalt = root.TryGetProperty("salt", out var salt) && salt.GetString() is { Length: > 0 };
            var hasHash = root.TryGetProperty("hash", out var hash) && hash.GetString() is { Length: > 0 };
            return hasSalt && hasHash;
        }
        catch
        {
            // ไฟล์เสีย/อ่านไม่ได้ - ถือว่าไม่มีรหัสผ่าน (ตรงกับ fallback เดิมใน check-uninstall-password.ps1)
            return false;
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_productCode))
        {
            return;
        }

        _busy = true;
        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        PasswordPanel.IsEnabled = false;
        DeleteDataCheckBox.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.LightGray;

        var username = UsernameBox.Text.Trim();
        var enteredSecret = PasswordBox.Password;
        var deleteData = DeleteDataCheckBox.IsChecked == true;

        // ลบ flag เก่าทิ้งก่อนเสมอ (เผื่อรอบก่อนหน้าเคย authorize ค้างไว้จากการลองรหัสผ่านที่ถูกต้องแต่
        // msiexec ล้มเหลวด้วยเหตุผลอื่น - กัน false-positive "ผ่าน" ในรอบนี้ทั้งที่ยังไม่ได้ verify จริง)
        try { File.Delete(_flagPath); } catch { /* ไม่มีไฟล์อยู่แล้วก็ไม่เป็นไร */ }

        var uninstallPassword = enteredSecret;

        // มี username กรอกมาด้วย = ลอง login ผ่าน Server ก่อนเสมอ (ไม่มี username = ข้ามไปใช้
        // enteredSecret เป็นรหัสผ่าน local ตรงๆ ทันที เหมือนพฤติกรรมเดิมทุกประการ)
        if (!string.IsNullOrEmpty(username))
        {
            StatusText.Text = "Signing in to Server...";
            var (outcome, token, baseUrl, message) = await TryLoginAsync(username, enteredSecret);

            switch (outcome)
            {
                case LoginOutcome.Success:
                    uninstallPassword = GenerateOneTimeUninstallPassword();
                    StatusText.Text = "Signed in. Uninstalling...";
                    if (token is not null && baseUrl is not null)
                    {
                        _ = PostUninstallAuditLogAsync(baseUrl, token);
                    }
                    break;

                case LoginOutcome.InvalidCredentials:
                    StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    StatusText.Text = "Incorrect username or password.";
                    ResetBusyState();
                    return;

                case LoginOutcome.ConnectionFailed:
                default:
                    // Offline fallback - ตีความ enteredSecret เป็นรหัสผ่าน local เดิมทันที (hybrid
                    // ตามที่ตกลงไว้ - ไม่ fail-closed เหมือน AgentTray's LoginWindow)
                    StatusText.Text = $"Could not reach the Server ({message}) - using the local uninstall password instead.";
                    uninstallPassword = enteredSecret;
                    break;
            }
        }

        var exitCode = await RunMsiUninstallAsync(uninstallPassword, deleteData);

        if (exitCode == 0)
        {
            IntroText.Text = $"{_productDisplayName} has been removed from this computer.";
            FormPanel.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            ActionButton.Content = "CLOSE";
            ActionButton.Click -= ActionButton_Click;
            ActionButton.Click += (_, _) => Close();
            ActionButton.IsEnabled = true;
            return;
        }

        // exit code ไม่ใช่ 0 - เช็คว่า msiexec block เพราะรหัสผ่านผิด (ไม่มี flag file แม้จะตั้งรหัสไว้)
        // หรือ error อื่นจริงๆ - ใช้ signal เดียวกับที่ LaunchCondition ใช้จริง ไม่ duplicate การ verify เอง
        var authGranted = File.Exists(_flagPath);

        if (!authGranted && _hasPassword)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusText.Text = "Incorrect password. Please try again.";
            PasswordBox.Password = "";
            PasswordBox.Focus();
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusText.Text = $"Uninstall failed (exit code {exitCode}). Contact your system administrator.";
        }

        ResetBusyState();
    }

    private void ResetBusyState()
    {
        _busy = false;
        ActionButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        PasswordPanel.IsEnabled = true;
        DeleteDataCheckBox.IsEnabled = true;
    }

    private enum LoginOutcome
    {
        Success,
        InvalidCredentials,
        ConnectionFailed
    }

    /// <summary>
    /// Login ผ่าน Server กลาง - รูปแบบเดียวกับ AgentTray/Services/ClientApiClient.LoginToServerAsync
    /// (HttpClient แยกอายุสั้น, POST { username, password } ไป /api/auth/login) ต่างจากตัวนั้นตรงที่
    /// เก็บ token ไว้ใช้ต่อ (ยิง audit-log POST หลังสำเร็จ) และแยก "ต่อ Server ไม่ได้" ออกจาก "username/
    /// password ผิด" ให้ caller ตัดสินใจ fallback ได้ถูกทาง (ดู doc comment บนคลาส)
    /// </summary>
    private async Task<(LoginOutcome Outcome, string? Token, string? BaseUrl, string Message)> TryLoginAsync(string username, string password)
    {
        string? baseUrl;
        try
        {
            baseUrl = ReadServerBaseUrl();
        }
        catch (Exception ex)
        {
            return (LoginOutcome.ConnectionFailed, null, null, $"could not read server address: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return (LoginOutcome.ConnectionFailed, null, null, "server address not configured");
        }

        try
        {
            using var serverClient = new HttpClient { Timeout = ServerLoginTimeout };
            var url = baseUrl.TrimEnd('/') + "/api/auth/login";
            var response = await serverClient.PostAsJsonAsync(url, new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                return (LoginOutcome.InvalidCredentials, null, null, "invalid username or password");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var token = doc.RootElement.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() : null;

            if (string.IsNullOrEmpty(token))
            {
                return (LoginOutcome.InvalidCredentials, null, null, "login response missing token");
            }

            return (LoginOutcome.Success, token, baseUrl, "signed in");
        }
        catch (Exception ex)
        {
            // ต่อ Server ไม่ได้เลย (network/timeout) - ไม่ใช่รหัสผ่านผิด แยกให้ caller fallback ไป local password ได้
            return (LoginOutcome.ConnectionFailed, null, null, ex.Message);
        }
    }

    /// <summary>
    /// อ่าน ServerSettings:BaseUrl จาก [INSTALLFOLDER]\Client\appsettings.json - config ตัวเดียวกับที่
    /// Agent service ใช้เอง (setup-client.ps1's Patch-BaseUrl เขียนไว้ตอน install) และเป็นตัวเดียวกับที่
    /// AgentTray's LoginWindow อ่านจากโฟลเดอร์ของตัวเอง - ที่นี่ต้องอ่านจาก path เต็มเพราะ _installFolder
    /// เป็น root ไม่ใช่โฟลเดอร์ของ Client/ClientTray โดยตรง
    /// </summary>
    private string? ReadServerBaseUrl()
    {
        var appsettingsPath = Path.Combine(_installFolder, "Client", "appsettings.json");
        if (!File.Exists(appsettingsPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        if (doc.RootElement.TryGetProperty("ServerSettings", out var serverSettings) &&
            serverSettings.TryGetProperty("BaseUrl", out var baseUrlProp))
        {
            return baseUrlProp.GetString();
        }

        return null;
    }

    /// <summary>
    /// Login สำเร็จ - generate รหัสผ่านชั่วคราวแบบสุ่ม แล้วเขียน session guard file แยกต่างหากจาก
    /// uninstall-guard.json เดิม (ไม่แตะ/ไม่ลบไฟล์เดิมเลย - รหัสผ่าน local ที่ตั้งไว้ตอน install ยังใช้ได้
    /// ตลอดไปไม่ว่าจะมีใคร login ผ่าน Server กี่ครั้งก็ตาม) check-uninstall-password.ps1 เช็คไฟล์นี้เพิ่ม
    /// เป็นทางเลือกที่สอง (single-use, มี expiry สั้นๆ) โดยไม่ต้องแก้ logic เดิมของมันเลย
    ///
    /// ใช้ hex string แทน Base64 สำหรับรหัสผ่านชั่วคราว - เลี่ยงอักขระ '='/'/' ที่เคยเป็นต้นเหตุบั๊ก
    /// UNINSTPWD= ว่างชนกับ REMOVE=ALL มาก่อน (ดู comment ยาวใน RunMsiUninstallAsync ด้านล่าง)
    /// </summary>
    private string GenerateOneTimeUninstallPassword()
    {
        var oneTimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(salt + oneTimePassword));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var guard = new
        {
            salt,
            hash,
            expiresAtUtc = DateTime.UtcNow.Add(SessionGuardLifetime).ToString("O")
        };

        File.WriteAllText(_sessionGuardPath, JsonSerializer.Serialize(guard));

        return oneTimePassword;
    }

    /// <summary>
    /// ยิง audit-log ไปที่ Server หลัง login สำเร็จ (ก่อนเรียก msiexec จริง) - fire-and-forget โดยตั้งใจ:
    /// audit log เป็นของเสริม ไม่ควรทำให้ uninstall ที่ login ผ่านแล้วล้มเหลวไปด้วยถ้า Server สะดุดพอดี
    /// ตอนนี้ (ต่างจาก login เองที่ต้องรอผลจริงก่อนตัดสินใจ path ต่อไป)
    /// </summary>
    private static async Task PostUninstallAuditLogAsync(string baseUrl, string token)
    {
        try
        {
            using var clientGuidKey = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
            var clientGuid = clientGuidKey?.GetValue(ClientGuidValueName) as string ?? "";

            using var serverClient = new HttpClient { Timeout = ServerLoginTimeout };
            serverClient.DefaultRequestHeaders.Add("X-Auth-Token", token);
            var url = baseUrl.TrimEnd('/') + "/api/activitylog/agent-uninstall-authorized";
            await serverClient.PostAsJsonAsync(url, new { clientGuid, machineName = Environment.MachineName });
        }
        catch
        {
            // ไม่สำเร็จก็ปล่อยผ่าน - ไม่ควรทำให้ uninstall ที่ login ผ่านแล้วล้มเหลวไปด้วยเพราะ audit log พลาด
        }
    }

    /// <summary>
    /// ยิง msiexec /x ตัวจริงเบื้องหลัง - ใช้ ArgumentList (ไม่ใช่ต่อ string เอง) ให้ .NET escape ค่าที่มี
    /// space/quote ใน password ให้ถูกต้องเอง กัน command-line injection/parsing พังถ้ารหัสผ่านมีอักขระพิเศษ
    ///
    /// บั๊กจริงที่เจอ (2026-08-11, live-tested ด้วย verbose msiexec log ตรงๆ ไม่ใช่เดา): ตอน password
    /// ว่างเปล่า (กรณีปกติที่สุด - ไม่มีใครตั้ง uninstall password ไว้เลย) ArgumentList entry
    /// "UNINSTPWD=" (ไม่มีอะไรต่อท้าย =) ไปชนกับ REMOVE=ALL ที่ /x ใส่ให้เองภายใน (ไม่ใช่ arg แยกที่เราส่ง
    /// เอง) กลายเป็น property เดียวกัน "UNINSTPWD=REMOVE=ALL" (ยืนยันจาก msiexec's own verbose log:
    /// "Command Line: UNINSTPWD=REMOVE=ALL ...") - REMOVE property เลย "ไม่เคยถูกตั้งค่าจริง" เท่ากับ
    /// Property(S): ACTION กลายเป็น INSTALL แทนที่จะเป็น REMOVE ทำให้ msiexec /x "reconfigure/reinstall"
    /// สินค้ากลับแทนที่จะ "uninstall" มันจริงๆ - service/ไฟล์ยังอยู่ครบเหมือนเดิมทั้งที่ exit code เป็น 0
    /// (ดูเหมือน "สำเร็จ" แต่ไม่ได้ลบอะไรเลย) เกิดขึ้นทุกครั้งที่ password ว่าง (กรณี default ของทุกเครื่อง
    /// Server+Console และของเครื่อง Agent ที่ไม่ได้ตั้ง uninstall password ไว้)
    ///
    /// แก้ด้วยการ "ไม่ส่ง property นี้เลย" ถ้า password ว่างเปล่า แทนที่จะพยายาม quote เอง (ลองแล้วพัง
    /// หนักกว่าเดิม - live-tested: .NET's ArgumentList escaping ใส่ backslash หนี quote ที่เรา literal
    /// ใส่เข้าไปเอง (เช่น "UNINSTPWD=&quot;&quot;") กลายเป็น command line ที่ msiexec เอง parse ไม่ออก -
    /// แสดง usage/help dialog ("Windows Installer... msiexec /Option...") แทนที่จะรันจริง ยืนยันจาก
    /// screenshot จริงตอน live-test) [UNINSTPWD] ที่ CheckUninstallPasswordHeadless CA ใช้ (ดู .wxs)
    /// resolve เป็น "" อยู่แล้วถ้า property ไม่เคยถูกตั้งค่าเลย - ผลลัพธ์เหมือนกันทุกอย่างกับตั้งเป็นค่าว่าง
    /// แต่ไม่ต้องยุ่งกับการ quote/escape เลย ปลอดภัยกว่า
    /// </summary>
    private Task<int> RunMsiUninstallAsync(string password, bool deleteData)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("/x");
        psi.ArgumentList.Add(_productCode);
        psi.ArgumentList.Add("/qn");
        if (!string.IsNullOrEmpty(password))
        {
            psi.ArgumentList.Add($"UNINSTPWD={password}");
        }
        if (deleteData)
        {
            psi.ArgumentList.Add("DELETEDATA=1");
        }

        return Task.Run(() =>
        {
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            return process.ExitCode;
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
