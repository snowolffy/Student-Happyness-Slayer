using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

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
/// </summary>
public partial class MainWindow : Window
{
    private readonly string _productCode;
    private readonly string _installFolder;
    private readonly string _guardPath;
    private readonly string _flagPath;
    private readonly bool _hasPassword;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        _productCode = args.Length > 1 ? args[1] : "";

        // .exe นี้ถูกติดตั้งที่ [INSTALLFOLDER]\UninstallHelper\ แต่ guard/flag file ทั้งคู่อยู่ที่
        // [INSTALLFOLDER] ตรงๆ (เขียนโดย setup-client.ps1/check-uninstall-password.ps1 - path เดิม
        // จากก่อนมี wrapper นี้ด้วยซ้ำ ไม่เปลี่ยนเพื่อไม่ต้องแตะสคริปต์ PowerShell ที่ทำงานถูกอยู่แล้ว)
        _installFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        _guardPath = Path.Combine(_installFolder, "uninstall-guard.json");
        _flagPath = Path.Combine(_installFolder, "uninstall-auth-ok.flag");

        _hasPassword = TryReadGuard(_guardPath);

        if (string.IsNullOrEmpty(_productCode))
        {
            IntroText.Text = "This tool must be launched from Add/Remove Programs, not run directly.";
            PasswordPanel.Visibility = Visibility.Collapsed;
            DeleteDataCheckBox.IsEnabled = false;
            ActionButton.IsEnabled = false;
            return;
        }

        if (_hasPassword)
        {
            IntroText.Text = "This machine is uninstall-protected. Enter the password to continue.";
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
        StatusText.Text = "Uninstalling...";

        var password = PasswordBox.Password;
        var deleteData = DeleteDataCheckBox.IsChecked == true;

        // ลบ flag เก่าทิ้งก่อนเสมอ (เผื่อรอบก่อนหน้าเคย authorize ค้างไว้จากการลองรหัสผ่านที่ถูกต้องแต่
        // msiexec ล้มเหลวด้วยเหตุผลอื่น - กัน false-positive "ผ่าน" ในรอบนี้ทั้งที่ยังไม่ได้ verify จริง)
        try { File.Delete(_flagPath); } catch { /* ไม่มีไฟล์อยู่แล้วก็ไม่เป็นไร */ }

        var exitCode = await RunMsiUninstallAsync(password, deleteData);

        if (exitCode == 0)
        {
            IntroText.Text = "Onion ProcOparetor has been removed from this computer.";
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

        _busy = false;
        ActionButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        PasswordPanel.IsEnabled = true;
        DeleteDataCheckBox.IsEnabled = true;
    }

    /// <summary>
    /// ยิง msiexec /x ตัวจริงเบื้องหลัง - ใช้ ArgumentList (ไม่ใช่ต่อ string เอง) ให้ .NET escape ค่าที่มี
    /// space/quote ใน password ให้ถูกต้องเอง กัน command-line injection/parsing พังถ้ารหัสผ่านมีอักขระพิเศษ
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
        psi.ArgumentList.Add($"UNINSTPWD={password}");
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
