using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using OnionProcOparetor.AgentTray.Models;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// จอล็อกอินนักเรียน (WP3-3, 2026-10-31) - ดู doc comment ใน .xaml
///
/// สื่อสารกับ Server 2 ทาง: ผ่าน ClientApiClient (Agent, localhost:8788) เพื่ออ่าน clientGuid/machineName
/// ปัจจุบัน และผ่าน StudentApiClient (Server ตรงๆ) เพื่อ login/logout/เปลี่ยนรหัสผ่าน - ตามที่
/// StudentAuthEndpoints.cs (WP3-1) ตั้งใจออกแบบไว้ (AgentTray ยิงตรง ไม่ผ่าน Agent loopback)
/// </summary>
public partial class StudentLoginWindow : Window
{
    private readonly ClientApiClient _agentApiClient;
    private readonly StudentApiClient _studentApiClient;
    private readonly DispatcherTimer _clockTimer;

    private bool _allowClose;
    private string? _pendingToken;

    /// <summary>ยิงทันทีที่ login/เปลี่ยนรหัสผ่านสำเร็จ - ก่อนที่ ForceClose() ตัวเองทำงาน (App.xaml.cs ฟังไว้อัปเดตสถานะ + toast + tray menu)</summary>
    public event Action<StudentDto, int?>? LoginSucceeded;

    /// <summary>ยิงตอนกด "สำหรับครู: Staff session" - App.xaml.cs เป็นคนเปิด StaffSessionStartWindow (ไม่ทำที่นี่เพื่อไม่ให้หน้าต่างนี้รู้จัก StaffSessionStartWindow เลย)</summary>
    public event Action? StaffSessionRequested;

    public StudentLoginWindow(ClientApiClient agentApiClient, string? loginScreenMessage)
    {
        InitializeComponent();
        _agentApiClient = agentApiClient;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var serverBaseUrl = config["ServerSettings:BaseUrl"] ?? "http://localhost:8787";
        var fallbackBaseUrl = config["ServerSettings:FallbackBaseUrl"];
        _studentApiClient = new StudentApiClient(serverBaseUrl, fallbackBaseUrl);

        LoginScreenMessageText.Text = loginScreenMessage ?? string.Empty;
        LoginScreenMessageText.Visibility = string.IsNullOrWhiteSpace(loginScreenMessage) ? Visibility.Collapsed : Visibility.Visible;

        LoadHeroImage();
        RefreshClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => RefreshClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
    }

    /// <summary>ก๊อปแนวทางเดียวกับ Console/MainWindow.xaml.cs LoadHeroImage - โหลดจากไฟล์ข้างแอปก่อน (ให้ครูเปลี่ยนภาพเองได้) fallback ไปภาพฝังในตัว exe ถ้าไม่มี/อ่านไม่ได้ ห้าม throw ขึ้นไปเด็ดขาด</summary>
    private void LoadHeroImage()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LoginHero.png");
        try
        {
            if (File.Exists(externalPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(externalPath, UriKind.Absolute);
                bitmap.EndInit();
                HeroImage.Source = bitmap;
                return;
            }
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[student-login] โหลด {externalPath} ไม่สำเร็จ ({ex.Message}) - ใช้ภาพที่ฝังไว้แทน");
        }

        try
        {
            HeroImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/LoginHero.png"));
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[student-login] โหลดภาพที่ฝังไว้ไม่สำเร็จด้วย ({ex.Message}) - ไม่มีภาพแสดง แต่ไม่ crash");
        }
    }

    private void RefreshClock() => ClockText.Text = DateTime.Now.ToString("ddd d MMM yyyy · HH:mm");

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(sender, e);
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await SubmitLoginAsync(force: false);

    private async void ForceLoginButton_Click(object sender, RoutedEventArgs e) => await SubmitLoginAsync(force: true);

    private void CancelForceLoginButton_Click(object sender, RoutedEventArgs e) => ShowLoginPanel();

    private async System.Threading.Tasks.Task SubmitLoginAsync(bool force)
    {
        var identifier = IdentifierBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
        {
            ShowLoginError("กรุณากรอกเมลโรงเรียน/รหัสนักเรียน และรหัสผ่าน");
            return;
        }

        LoginButton.IsEnabled = false;
        ForceLoginButton.IsEnabled = false;

        var status = await _agentApiClient.GetStatusAsync();
        if (status is null || string.IsNullOrWhiteSpace(status.ClientGuid))
        {
            ShowLoginError("ติดต่อ Agent บนเครื่องนี้ไม่ได้ - ลองใหม่อีกครั้ง");
            LoginButton.IsEnabled = true;
            ForceLoginButton.IsEnabled = true;
            return;
        }

        var result = await _studentApiClient.LoginAsync(status.ClientGuid, status.MachineName, identifier, password, force);
        LoginButton.IsEnabled = true;
        ForceLoginButton.IsEnabled = true;

        if (result is null)
        {
            ShowLoginError("ติดต่อ Server ไม่ได้ - เช็คว่า Server เปิดอยู่");
            return;
        }

        switch (result.Status)
        {
            case "Success":
                await CompleteLoginAsync(result.Student, result.SessionId);
                break;

            case "MustChangePassword":
                _pendingToken = result.PendingToken;
                MustChangePasswordGreeting.Text = string.IsNullOrWhiteSpace(result.FullName)
                    ? "กรุณาตั้งรหัสผ่านใหม่ก่อนใช้งาน"
                    : $"สวัสดี {result.FullName} · กรุณาตั้งรหัสผ่านใหม่ก่อนใช้งาน";
                ShowPanel(MustChangePasswordPanel);
                break;

            case "LoggedInElsewhere":
                LoggedInElsewhereText.Text = $"บัญชีนี้กำลังใช้อยู่ที่ {result.OtherMachineName}\nถ้าดำเนินการต่อ เครื่องนั้นจะถูกออกจากระบบทันที";
                ForceLoginButton.Visibility = result.CanForce ? Visibility.Visible : Visibility.Collapsed;
                ShowPanel(LoggedInElsewherePanel);
                break;

            case "Locked":
                ShowLoginError("บัญชีนี้ถูกล็อกชั่วคราว เพราะกรอกรหัสผิดหลายครั้ง กรุณาแจ้งครูผู้สอนเพื่อปลดล็อก");
                break;

            default:
                // "InvalidCredentials" และกรณีไม่รู้จักอื่นๆ - ไม่บอกว่าผิดช่องไหน (ดู wireframe st-login-states #1)
                ShowLoginError("รหัสนักเรียนหรือรหัสผ่านไม่ถูกต้อง");
                break;
        }
    }

    private async void SavePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(_pendingToken))
        {
            ShowChangePasswordError("ลิงก์เปลี่ยนรหัสผ่านหมดอายุ - กรุณาล็อกอินใหม่");
            ShowLoginPanel();
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowChangePasswordError("รหัสผ่านใหม่ทั้งสองช่องไม่ตรงกัน");
            return;
        }

        SavePasswordButton.IsEnabled = false;
        var result = await _studentApiClient.ChangePasswordAsync(_pendingToken, newPassword, confirmPassword);
        SavePasswordButton.IsEnabled = true;

        if (result is null)
        {
            ShowChangePasswordError("ติดต่อ Server ไม่ได้ - เช็คว่า Server เปิดอยู่");
            return;
        }

        if (result.Status == "Success")
        {
            await CompleteLoginAsync(result.Student, result.SessionId);
            return;
        }

        ShowChangePasswordError(result.Error ?? "ตั้งรหัสผ่านใหม่ไม่สำเร็จ");
    }

    private async System.Threading.Tasks.Task CompleteLoginAsync(StudentDto? student, int? sessionId)
    {
        if (student is null)
        {
            ShowLoginError("ล็อกอินสำเร็จแต่ไม่ได้ข้อมูลนักเรียนกลับมา - กรุณาลองใหม่");
            return;
        }

        await _agentApiClient.NotifyStudentSessionAsync(true, student.StudentCode, student.FullName);
        LoginSucceeded?.Invoke(student, sessionId);
        ForceClose();
    }

    private void ShowLoginError(string message)
    {
        ShowPanel(LoginPanel);
        LoginErrorText.Text = message;
        LoginErrorBanner.Visibility = Visibility.Visible;
    }

    private void ShowChangePasswordError(string message)
    {
        ChangePasswordErrorText.Text = message;
        ChangePasswordErrorText.Visibility = Visibility.Visible;
    }

    private void ShowLoginPanel()
    {
        LoginErrorBanner.Visibility = Visibility.Collapsed;
        ShowPanel(LoginPanel);
    }

    private void ShowPanel(FrameworkElement panel)
    {
        LoginPanel.Visibility = panel == LoginPanel ? Visibility.Visible : Visibility.Collapsed;
        LoggedInElsewherePanel.Visibility = panel == LoggedInElsewherePanel ? Visibility.Visible : Visibility.Collapsed;
        MustChangePasswordPanel.Visibility = panel == MustChangePasswordPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StaffSessionLink_Click(object sender, MouseButtonEventArgs e) => StaffSessionRequested?.Invoke();

    private static void ConfirmThenRunShutdown(string action, string verb)
    {
        var confirmed = MessageBox.Show(
            $"ต้องการ{action}เครื่องนี้ใช่หรือไม่?",
            $"ยืนยันการ{action}เครื่อง",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("shutdown", verb) { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[student-login] เรียก shutdown.exe ไม่สำเร็จ: {ex.Message}");
        }
    }

    private void ShutdownButton_Click(object sender, RoutedEventArgs e) => ConfirmThenRunShutdown("ปิด", "/s /t 0");

    private void RestartButton_Click(object sender, RoutedEventArgs e) => ConfirmThenRunShutdown("รีสตาร์ท", "/r /t 0");

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (login สำเร็จแล้ว หรือ Agent แจ้งว่าไม่ต้องบังคับ login แล้ว) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง (เหมือน LockScreenWindow)</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // soft gate ระดับแอป (ดู doc comment บน .xaml) - กัน Alt+F4/การปิดอื่นๆ ที่ไม่ได้มาจาก
            // ForceClose() นักเรียนต้อง login หรือรอ Agent บอกว่าไม่บังคับแล้วเท่านั้นถึงปิดได้
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
