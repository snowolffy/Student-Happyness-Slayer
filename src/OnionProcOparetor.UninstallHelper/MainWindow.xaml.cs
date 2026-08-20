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
    private readonly string _productDisplayName;
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

        _busy = false;
        ActionButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        PasswordPanel.IsEnabled = true;
        DeleteDataCheckBox.IsEnabled = true;
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
