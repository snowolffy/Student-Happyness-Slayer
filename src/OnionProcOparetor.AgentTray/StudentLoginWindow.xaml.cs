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
    private readonly DispatcherTimer _connectivityTimer;

    private bool _allowClose;
    private string? _pendingToken;
    private bool _emergencyCodeConfigured;

    /// <summary>ยิงทันทีที่ login/เปลี่ยนรหัสผ่านสำเร็จ - ก่อนที่ ForceClose() ตัวเองทำงาน (App.xaml.cs ฟังไว้อัปเดตสถานะ + toast + tray menu)</summary>
    public event Action<StudentDto, int?>? LoginSucceeded;

    /// <summary>ยิงตอนกด "สำหรับครู: Staff session" - App.xaml.cs เป็นคนเปิด StaffSessionStartWindow (ไม่ทำที่นี่เพื่อไม่ให้หน้าต่างนี้รู้จัก StaffSessionStartWindow เลย)</summary>
    public event Action? StaffSessionRequested;

    /// <summary>
    /// ยิงทันทีที่ปลดด้วยรหัสฉุกเฉินสำเร็จ (WP3-4b, 2026-10-31) - App.xaml.cs ฟังไว้ปิดจอนี้ทันที แทนที่
    /// จะรอ Worker.cs ส่ง SignInStatus รอบถัดไป (ปกติ <=10 วิ อยู่แล้วจาก ScanRunningProcessesAsync แต่
    /// action ที่ครูเพิ่งกดเองควรได้ feedback ทันทีกว่านั้น)
    /// </summary>
    public event Action? EmergencyUnlockSucceeded;

    /// <summary>
    /// โหมดตรวจ UI เท่านั้น (--ui-preview=student-login --server-down) - จำลองสถานะ "ติดต่อ Server
    /// ไม่ได้" + "ตั้งค่ารหัสฉุกเฉินไว้แล้ว" โดยไม่ต้องมี Agent จริงรันอยู่ (เครื่อง dev นี้รัน Agent จริง
    /// ไม่ได้ - ต้องเป็น LocalSystem/HKLM) ไม่ได้ #if DEBUG เพราะเป็นแค่ property เปล่าๆ ไม่มีผลอะไรถ้า
    /// ไม่ถูกตั้ง (เหมือน Console/DashboardWindow.PreviewMode ที่ก็เป็น public property ธรรมดาเช่นกัน
    /// ส่วนจุดที่ set ค่านี้ใน App.xaml.cs ถึงจะ #if DEBUG)
    ///
    /// Custom setter (ไม่ใช่ auto-property เฉยๆ) โดยตั้งใจ: App.xaml.cs ตั้งค่านี้ผ่าน object
    /// initializer (`new StudentLoginWindow(...) { PreviewForceServerDown = ... }`) ซึ่ง C# รัน
    /// setter "หลัง" constructor ทำงานเสร็จแล้วเท่านั้น - ถ้าเป็น auto-property เฉยๆ
    /// RefreshEmergencyAndConnectivityStateAsync() ที่เรียกจาก constructor จะยิงไปแล้วด้วยค่า false
    /// (default) ก่อนที่ object initializer จะทันตั้งเป็น true เลย (บั๊กจริงที่เจอตอนถ่ายภาพ
    /// --server-down ครั้งแรก - ได้ภาพเหมือนปกติทุกอย่าง ไม่มีแบนเนอร์) setter นี้ trigger refresh ซ้ำ
    /// อีกทีให้เอง กันปัญหานี้โดยไม่ต้องเปลี่ยน constructor signature
    /// </summary>
    public bool PreviewForceServerDown
    {
        get => _previewForceServerDown;
        set
        {
            _previewForceServerDown = value;
            _ = RefreshEmergencyAndConnectivityStateAsync();
        }
    }

    private bool _previewForceServerDown;

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

        // รหัสฉุกเฉิน (WP3-4b, 2026-10-31) - poll เองทุก 5 วิ (loopback ไปที่ Agent บนเครื่องเดียวกัน
        // เบาพอที่จะ poll ถี่กว่า pipe push ของ Worker ได้โดยไม่กระทบอะไร) เพื่อ: (1) เปิด/ปิดลิงก์
        // "รหัสฉุกเฉิน" ตาม EmergencyCodeConfigured (2) โชว์/ซ่อนแบนเนอร์เด่นตอนติดต่อ Server ไม่ได้
        // เริ่มเรียกทันทีครั้งแรกด้วย (ไม่ต้องรอ 5 วิแรก) ไม่ใช้ _clockTimer เดิมเพราะ interval คนละ
        // ความหมายกัน (เวลาเปลี่ยนทุกวิ แต่ไม่จำเป็นต้องเช็ค connectivity ถี่ขนาดนั้น)
        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _connectivityTimer.Tick += async (_, _) => await RefreshEmergencyAndConnectivityStateAsync();
        _connectivityTimer.Start();
        Closed += (_, _) => _connectivityTimer.Stop();
        _ = RefreshEmergencyAndConnectivityStateAsync();

#if DEBUG
        // --ui-preview=student-login --login-error|--logged-in-elsewhere|--must-change-password (UI-6,
        // 2026-11-03) - จำลองอีก 3 สถานะของฟอร์มเพื่อถ่ายภาพตรวจว่าการ์ดที่ย่อลง 75% แล้วไม่มีอะไรล้น/
        // ถูกตัดในทุกสถานะ (roadmap WP UI-6 + orchestrator ขอให้ตรวจ LoggedInElsewherePanel/
        // MustChangePasswordPanel เพิ่มด้วย เพราะเป็นสถานะที่มีช่องกรอกเยอะสุด เสี่ยงล้นสุด) อ่าน args
        // เองตรงนี้แทนการแก้ App.xaml.cs เพราะไฟล์นี้อยู่นอกขอบเขตไฟล์ที่ WP UI-6 แตะได้
        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Contains("--login-error"))
        {
            ShowLoginError("ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง");
        }
        else if (cmdArgs.Contains("--logged-in-elsewhere"))
        {
            LoggedInElsewhereText.Text = "บัญชีนี้กำลังใช้อยู่ที่ LAB-PC-23\nถ้าดำเนินการต่อ เครื่องนั้นจะถูกออกจากระบบทันที";
            ShowPanel(LoggedInElsewherePanel);
        }
        else if (cmdArgs.Contains("--must-change-password"))
        {
            MustChangePasswordGreeting.Text = "ยินดีต้อนรับ สมชาย ใจดี - กรุณาตั้งรหัสผ่านใหม่ก่อนใช้งาน";
            ShowPanel(MustChangePasswordPanel);
        }

        // วัด overflow จริงด้วย TransformToAncestor แทนการเพ่งภาพ (ตามที่ orchestrator ขอ 2026-11-03) -
        // วัดเฉพาะ panel ที่กำลัง Visible อยู่จริง (panel ที่ Collapsed มี ActualHeight=0/ไม่ได้ arrange
        // เต็มที่ วัดแล้วไม่มีความหมาย) เทียบกับความสูงจริงของ LoginCard แล้ว log ผลลง TrayDiagnostics
        // เพื่อตรวจสอบได้โดยไม่ต้องเพ่งภาพ
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                var (name, panel) = LoggedInElsewherePanel.Visibility == Visibility.Visible
                    ? ("LoggedInElsewherePanel", (FrameworkElement)LoggedInElsewherePanel)
                    : MustChangePasswordPanel.Visibility == Visibility.Visible
                        ? ("MustChangePasswordPanel", MustChangePasswordPanel)
                        : ("LoginPanel", LoginPanel);

                var bottom = panel.TransformToAncestor(LoginCard).Transform(new Point(0, panel.ActualHeight)).Y;
                var overflow = bottom - LoginCard.ActualHeight;
                TrayDiagnostics.Log(
                    $"UI6-OVERFLOW-CHECK panel={name} LoginCard.ActualHeight={LoginCard.ActualHeight:F1} "
                    + $"panel.bottom={bottom:F1} overflow={overflow:F1} " + (overflow > 0 ? "CLIPPED" : "OK"));
            }
            catch (Exception ex)
            {
                TrayDiagnostics.Log($"UI6-OVERFLOW-CHECK measurement failed: {ex.Message}");
            }
        });
#endif
    }

    /// <summary>
    /// ดึงสถานะ "ตั้งค่ารหัสฉุกเฉินไว้ไหม" + "ติดต่อ Server ได้ไหม" จาก Agent /status แล้วอัปเดต
    /// ลิงก์/แบนเนอร์ - คืนเงียบๆ ถ้า Agent local API ไม่ตอบ (คงค่าที่โชว์อยู่เดิมไว้ ไม่กระพริบ UI)
    /// </summary>
    private async Task RefreshEmergencyAndConnectivityStateAsync()
    {
        bool lastPollSucceeded;
        bool emergencyCodeConfigured;

        if (PreviewForceServerDown)
        {
            lastPollSucceeded = false;
            emergencyCodeConfigured = true;
        }
        else
        {
            var status = await _agentApiClient.GetStatusAsync();
            if (status is null)
            {
                return;
            }

            lastPollSucceeded = status.LastPollSucceeded;
            emergencyCodeConfigured = status.EmergencyCodeConfigured;
        }

        ApplyEmergencyCodeAvailability(emergencyCodeConfigured);

        // แบนเนอร์เด่นตามที่ roadmap ระบุ ("จอฉุกเฉินควรขึ้นเมื่อติดต่อ Server ไม่ได้") - โชว์เฉพาะตอน
        // เครื่องนี้มีรหัสฉุกเฉินให้ใช้จริงเท่านั้น (ไม่มีประโยชน์ถ้าไม่ได้ตั้งค่าไว้)
        ServerUnreachableBanner.Visibility = (!lastPollSucceeded && emergencyCodeConfigured)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyEmergencyCodeAvailability(bool configured)
    {
        _emergencyCodeConfigured = configured;
        EmergencyCodeLinkText.Text = configured ? "   รหัสฉุกเฉิน" : "   รหัสฉุกเฉิน (ยังไม่พร้อมใช้งาน)";
        EmergencyCodeLinkText.Cursor = configured ? Cursors.Hand : Cursors.Arrow;
        EmergencyCodeLinkText.TextDecorations = configured ? TextDecorations.Underline : null;
        EmergencyCodeLinkText.Foreground = (System.Windows.Media.Brush)FindResource(configured ? "Brush.Accent.Dark" : "Brush.Text.Secondary");
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

    private void EmergencyCodeLink_Click(object sender, MouseButtonEventArgs e) => OpenEmergencyWindow();

    private void OpenEmergencyFromBannerButton_Click(object sender, RoutedEventArgs e) => OpenEmergencyWindow();

    /// <summary>
    /// เปิด EmergencyWindow ซ้อนทับจอล็อกอินนักเรียน (WP3-4b, 2026-10-31) - ทั้งจากลิงก์เล็กๆ และจาก
    /// ปุ่มในแบนเนอร์เด่นเรียกตัวนี้ตัวเดียวกัน ไม่ทำอะไรถ้ายังไม่ได้ตั้งค่า secret ไว้ (ลิงก์ถูกปิดอยู่แล้ว
    /// ตาม ApplyEmergencyCodeAvailability แต่เช็คซ้ำอีกชั้นกันกด race กับตอนกำลัง refresh สถานะอยู่พอดี)
    /// </summary>
    private void OpenEmergencyWindow()
    {
        if (!_emergencyCodeConfigured)
        {
            return;
        }

        var window = new EmergencyWindow(_agentApiClient) { Owner = this };
        window.UnlockSucceeded += () => Dispatcher.BeginInvoke(() => EmergencyUnlockSucceeded?.Invoke());
        window.Show();
    }

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
