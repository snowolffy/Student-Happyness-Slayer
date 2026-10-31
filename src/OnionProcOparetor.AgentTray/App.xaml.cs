using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using OnionProcOparetor.AgentTray.Models;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

// System.Windows.Application ระบุเต็มเสมอ (ไม่ใช่แค่ "Application") เพราะ UseWindowsForms=true
// (สำหรับ Screen.AllScreens ใน LockScreenWindow เท่านั้น) เพิ่ม global using System.Windows.Forms
// เข้ามาชนกับ System.Windows.Application ของ WPF โดยตรง
public partial class App : System.Windows.Application
{
    private TaskbarIcon? _trayIcon;
    private readonly ClientApiClient _apiClient = new();
    private readonly BroadcastPipeListener _broadcastListener = new();
    private readonly CancellationTokenSource _broadcastListenerCts = new();
    private readonly VncProcessManager _vncProcessManager = new();
    private readonly PhysicalInputBlocker _physicalInputBlocker = new();
    private readonly SingleInstanceGuard _singleInstanceGuard = new();

    // instance ต่อจอ (System.Windows.Forms.Screen.AllScreens) - เฉพาะตัวแรก (index 0) เท่านั้นที่ติดตั้ง
    // keyboard/mouse hook + Agent watchdog จริง (ดู LockScreenWindow doc comment) รายการนี้ว่างเปล่า =
    // ไม่ได้ล็อกอยู่ (ใช้แทนการเช็ค field เดี่ยวเดิมที่รองรับแค่จอเดียว)
    private readonly List<LockScreenWindow> _lockScreenWindows = new();
    private ShutdownNotifyWindow? _shutdownNotifyWindow;
    private readonly JokeShutdownController _jokeShutdownController = new();

    /// <summary>ป้ายมุมขวาล่าง "โหมดสอบ" (WP2-2, 2026-10-31) - null = ไม่มีโหมดสอบทำงานอยู่ตอนนี้</summary>
    private ExamModeBadgeWindow? _examModeBadgeWindow;

    // ---- Sign-in (WP3-3, 2026-10-31) ----
    private StudentLoginWindow? _studentLoginWindow;
    private StaffSessionBadgeWindow? _staffSessionBadgeWindow;
    private readonly IdleActivityMonitor _idleMonitor = new();
    private readonly System.Windows.Controls.MenuItem _notMeMenuItem = new() { Header = "ไม่ใช่ฉัน (สลับผู้ใช้)" };
    private readonly System.Windows.Controls.MenuItem _logoutMenuItem = new() { Header = "ออกจากระบบ" };
    private readonly System.Windows.Controls.Separator _studentMenuSeparator = new();

    /// <summary>true ล่าสุดที่รู้จาก Agent (pull /status ตอนเริ่ม + push "SignInStatus" ทุกครั้งที่เปลี่ยน) - เก็บไว้ตัดสินใจตอนจบ staff session/logout ว่าต้องเปิดจอล็อกอินกลับมาไหม</summary>
    private bool _requireLogin;
    private string? _loginScreenMessage;

    private string? _currentStudentCode;
    private string? _currentStudentName;
    private int? _currentSessionId;
    private StudentApiClient? _studentApiClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        // --ui-preview=<ชื่อหน้าต่าง> --shot=<path> (WP3-3, 2026-10-31) - โหมดตรวจ UI เท่านั้น
        // (DEBUG build เท่านั้น เหมือน pattern ของ Console/App.xaml.cs) เรนเดอร์หน้าต่างใหม่ที่ทำใน
        // WP นี้เป็น PNG ด้วย RenderTargetBitmap แล้วปิดโปรแกรม - ไม่ต้องมี Agent/Server จริงรันอยู่
        // เพราะ Agent รันจริงไม่ได้บนเครื่อง dev นี้ (ต้องเป็น LocalSystem/HKLM) หน้าต่างพวกนี้ทนต่อการ
        // ที่ Agent local API ไม่ตอบอยู่แล้ว (GetStatusAsync คืน null เงียบๆ - ดู ClientApiClient)
        var previewArg = e.Args.FirstOrDefault(a => a.StartsWith("--ui-preview=", StringComparison.Ordinal));
        if (previewArg is not null)
        {
            var windowName = previewArg["--ui-preview=".Length..];
            var previewApiClient = new ClientApiClient();
            Window? preview = windowName switch
            {
                "student-login" => new StudentLoginWindow(previewApiClient, "ยินดีต้อนรับสู่ห้องปฏิบัติการคอมพิวเตอร์ 1 - กรุณาใช้งานอย่างเหมาะสม"),
                "staff-session-start" => new StaffSessionStartWindow(previewApiClient),
                "staff-session-badge" => new StaffSessionBadgeWindow(previewApiClient),
                "session-toast" => new SessionToastWindow("เข้าสู่ระบบเป็น สมชาย ใจดี"),
                "lock-screen" => new LockScreenWindow(System.Windows.Forms.Screen.PrimaryScreen!, installInputBlocking: false, "กรุณาละสายตาจากหน้าจอและฟังครูผู้สอน (ทดสอบ)"),
                _ => null
            };

            if (preview is null)
            {
                TrayDiagnostics.Log($"--ui-preview: ไม่รู้จักชื่อ '{windowName}' - ปิดโปรแกรม");
                Shutdown(2);
                return;
            }

            // ShowActivated=false ใช้ไม่ได้กับ WindowState=Maximized (WPF throw InvalidOperationException
            // ตรงๆ) - student-login/lock-screen เป็น Maximized เลยปล่อยให้ activate ตามปกติ (ขึ้นจอจริง
            // สั้นๆ ระหว่างถ่ายภาพ - ยอมรับได้ เพราะเครื่อง dev นี้มีจอให้ใช้และรันแค่เพื่อตรวจ UI)
            if (preview.WindowState != WindowState.Maximized)
            {
                preview.ShowActivated = false;
            }
            preview.Show();
            CapturePreview(preview, e.Args);
            return;
        }
#endif

        // กันรันซ้อนกันหลาย instance ใน session เดียวกัน - จำเป็นเพราะ Agent จะสตาร์ท Tray ใหม่
        // อัตโนมัติทุกครั้งที่ส่ง pipe ไม่สำเร็จ ซึ่งบางครั้ง Tray ก็ยังรันอยู่ดีๆ อยู่แล้ว
        // (ดู SingleInstanceGuard) - ต้องเช็คก่อนทำอย่างอื่นทั้งหมด
        if (!_singleInstanceGuard.TryAcquire())
        {
            // ปิดเงียบๆ ไม่เด้ง MessageBox - instance นี้ถูกสตาร์ทโดยอัตโนมัติจาก Agent
            // ไม่ใช่ user กดเอง การเด้ง dialog จะกวนคนที่กำลังใช้เครื่องอยู่โดยไม่จำเป็น
            Shutdown();
            return;
        }

        // ตั้งค่า auto-start ผ่าน Registry Run key (HKCU) ให้อัตโนมัติทุกครั้งที่ Tray เริ่มทำงาน
        // ทำงานแบบ idempotent - เรียกซ้ำได้ไม่มีผลข้างเคียง ถ้าตั้งไว้แล้วและ path ไม่เปลี่ยนจะไม่เขียนซ้ำ
        RegistryStartup.EnsureAutoStartEnabled();

        // เริ่มฟัง message จาก Agent (named pipe) - ทำงานอยู่ตลอดอายุของ Tray
        // ไม่ต้องเปิดหน้า Status ก่อนก็รับ popup/lock command ได้
        _broadcastListener.MessageReceived += OnBroadcastMessageReceived;
        _broadcastListener.ShowLockScreenRequested += OnShowLockScreenRequested;
        _broadcastListener.HideLockScreenRequested += OnHideLockScreenRequested;
        _broadcastListener.StartVncSessionRequested += OnStartVncSessionRequested;
        _broadcastListener.StopVncSessionRequested += OnStopVncSessionRequested;
        _broadcastListener.BlockTargetInputRequested += OnBlockTargetInputRequested;
        _broadcastListener.UnblockTargetInputRequested += OnUnblockTargetInputRequested;
        _broadcastListener.ShutdownNotifyRequested += OnShutdownNotifyRequested;
        _broadcastListener.GetRunningProcessesRequested += OnGetRunningProcessesRequested;
        _broadcastListener.ExamModeStatusChanged += OnExamModeStatusChanged;
        _broadcastListener.SignInStatusChanged += OnSignInStatusChanged;
        _broadcastListener.Start(_broadcastListenerCts.Token);

        // โหมดสอบ (WP2-2, 2026-10-31) - ดึงสถานะเริ่มต้นจาก Agent ทันทีตอน Tray เพิ่งเริ่มทำงาน (เช่น
        // user ล็อกอินใหม่กลางโหมดสอบ หรือ Tray ถูก auto-recover ขึ้นมาใหม่) เพราะ push ผ่าน pipe ส่ง
        // แค่ตอน "สถานะเปลี่ยน" เท่านั้น - ถ้าไม่ pull ตรงนี้ Tray ที่เพิ่งเริ่มจะไม่เห็นป้ายเลยจนกว่า
        // โหมดสอบจะเปลี่ยนสถานะอีกครั้ง (เริ่ม/จบรอบถัดไป) ซึ่งอาจไม่เกิดขึ้นเลยตลอดคาบที่เหลือ
        _ = InitializeExamModeBadgeFromStatusAsync();

        // Sign-in (WP3-3, 2026-10-31) - เหตุผลเดียวกับ InitializeExamModeBadgeFromStatusAsync ด้านบน
        _ = InitializeSignInStateFromStatusAsync();
        _idleMonitor.IdleThresholdReached += OnIdleThresholdReached;
        _idleMonitor.Start();

        // ฟังเหตุการณ์ sleep/resume ของเครื่อง - ดู OnPowerModeChanged ว่าทำไมถึงจำเป็น
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // logout นักเรียนอัตโนมัติตอนปิด/รีสตาร์ทเครื่อง (WP3-3, 2026-10-31 - ดู docs/roadmap.md
        // "logout อัตโนมัติ...เมื่อปิด/รีสตาร์ทเครื่อง") best-effort เท่านั้น: SessionEnding ยิงก่อน
        // Windows ปิด session จริงไม่กี่วินาที ไม่มีการันตีว่า HTTP call ไปจบก่อนเครื่องดับจริง
        // (ยอมรับได้ - ไม่ร้ายแรงเท่า session ค้างเป็น "เปิดอยู่" ผิดๆ ถ้าไม่พยายามเลย)
        SystemEvents.SessionEnding += OnSessionEnding;

        // Failsafe path 1 (ดู PhysicalInputBlocker doc comment): winvnc.exe หยุดไม่ว่าเพราะอะไร ต้องคืน
        // สิทธิ์ physical input ทันที - Stopped ยิงจาก background thread เสมอ (ไม่ใช่ UI thread) ต้อง
        // marshal เองตรงนี้ก่อนเรียก Unblock() (DispatcherTimer ข้างในต้อง stop จาก thread ที่สร้างเท่านั้น)
        _vncProcessManager.Stopped += () => Dispatcher.Invoke(_physicalInputBlocker.Unblock);

        try
        {
            _trayIcon = new TaskbarIcon
            {
                // ดึง icon จาก exe ตัวเองโดยตรง (ฝังไว้แล้วผ่าน <ApplicationIcon> ใน .csproj) แทนไอคอน
                // default ของระบบ - ถ้า extract ไม่สำเร็จด้วยเหตุผลใดก็ตาม fallback กลับไปใช้ Shield
                // เพื่อไม่ให้ทั้ง tray icon หายไปเฉยๆ
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)
                       ?? System.Drawing.SystemIcons.Shield,
                ToolTipText = "Onion ProcOparetor - กำลังทำงาน"
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var statusMenuItem = new System.Windows.Controls.MenuItem { Header = "Open Status..." };
            statusMenuItem.Click += (_, _) => OpenLoginWindow();
            contextMenu.Items.Add(statusMenuItem);

            // Sign-in (WP3-3, 2026-10-31) - "ไม่ใช่ฉัน"/"ออกจากระบบ" โชว์เฉพาะตอนมีนักเรียน login อยู่
            // เท่านั้น (ดู RebuildTrayContextMenu) สร้าง MenuItem ไว้เป็น field ล่วงหน้าแล้วย้าย
            // Visibility เอาเข้า/ออกจาก contextMenu.Items เอง ไม่ Create ใหม่ทุกครั้งที่สถานะเปลี่ยน
            _notMeMenuItem.Click += (_, _) => _ = PerformStudentLogoutAsync(StudentSessionEndReason.NotMe);
            _logoutMenuItem.Click += (_, _) => _ = PerformStudentLogoutAsync(StudentSessionEndReason.ManualLogout);
            // รหัสฉุกเฉิน (WP3-4, 2026-10-31) - เมนูนี้เป็นจุดเข้าถึงชั่วคราวเท่านั้น (ของจริงควรขึ้นอัตโนมัติ
            // แทนจอล็อกอินตอนติดต่อ Server ไม่ได้ - เป็นงานต่อสายที่ WP3-3 ต้องทำทีหลัง ดูรายงาน WP3-4)
            var emergencyMenuItem = new System.Windows.Controls.MenuItem { Header = "รหัสฉุกเฉิน..." };
            emergencyMenuItem.Click += (_, _) => new EmergencyWindow(_apiClient).Show();
            contextMenu.Items.Add(emergencyMenuItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "Exit Tray" };
            exitMenuItem.Click += (_, _) => ConfirmThenExit();
            contextMenu.Items.Add(exitMenuItem);

            _trayIcon.ContextMenu = contextMenu;
            RebuildTrayContextMenu();

            // double-click ที่ icon ก็เปิดหน้า login เหมือนกัน
            _trayIcon.TrayMouseDoubleClick += (_, _) => OpenLoginWindow();

            TrayDiagnostics.Log("[app] AgentTray เริ่มทำงาน - tray icon พร้อมใช้งาน");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error: {ex}", "Onion ProcOparetor Tray Error");
            Shutdown();
        }
    }

    /// <summary>
    /// เครื่องหลับ/ตื่น - จำเป็นเพราะผู้ใช้รายงาน (2026-09-10) ว่าหลังเครื่องตื่นจาก sleep แล้ว Tray
    /// "ค้างไปเลย ไม่ตอบสนอง": tray icon ยังกดได้ แต่คำสั่งจาก Console ไม่เข้าเลย
    ///
    /// ตัวการที่พิสูจน์ได้จากโค้ดคือ accept-loop ของ named pipe ที่ค้างได้ถาวร (ดู
    /// BroadcastPipeListener.ReadTimeout) ซึ่งแก้ไปแล้วด้วย timeout - แต่ยังพิสูจน์ไม่ได้ว่า "การหลับ"
    /// ทำให้มันค้างด้วยกลไกไหนกันแน่ (ที่นี่ไม่มีเครื่องแล็บให้ทำซ้ำ) เลยเพิ่มการเปิด pipe ใหม่ทั้งชุด
    /// ตอนตื่นเป็นชั้นที่สอง: ไม่ว่า handle เดิมจะพังหรือไม่ ตื่นมาแล้วได้ของใหม่เสมอ ราคาของการทำแบบนี้
    /// ตอนที่ไม่มีอะไรพังเลยคือศูนย์ (แค่เปิด server instance ใหม่หนึ่งครั้ง)
    ///
    /// เรียกจาก thread ของ SystemEvents ไม่ใช่ UI thread - RestartAccept() ปลอดภัยข้าม thread อยู่แล้ว
    /// ส่วนอะไรที่แตะ WPF ต้อง marshal เอง
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        TrayDiagnostics.Log($"[power] PowerModeChanged: {e.Mode}");

        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _broadcastListener.RestartAccept();

        // บันทึกสถานะที่ "ค้างข้ามการหลับ" มาได้ไว้ด้วย - ถ้าเครื่องหลับตอนครูกำลังคุมอยู่ ตื่นมาแล้ว
        // นักเรียนอาจยังโดนบล็อกคีย์บอร์ด/เมาส์ค้างอยู่โดยไม่มีใครคุมแล้ว บรรทัดนี้ทำให้รู้ได้จาก log
        // ว่าเกิดขึ้นจริงไหม (ยังไม่ปลดให้อัตโนมัติตรงนี้ - failsafe เดิมสองชั้นควรจัดการได้เอง
        // ดู PhysicalInputBlocker และถ้า log บอกว่าไม่ได้ผลจริงค่อยเพิ่มการปลดตรงนี้)
        Dispatcher.BeginInvoke(() => TrayDiagnostics.Log(
            $"[power] สถานะหลังตื่น: inputBlocked={_physicalInputBlocker.IsBlocking} "
            + $"lockScreens={_lockScreenWindows.Count} windows={Windows.Count}"));
    }

    /// <summary>
    /// "Exit Tray" ต้องยืนยันก่อนเสมอ - เดิมกดปุ่มเดียวปิดทันทีโดยไม่มีคำเตือนใดๆ ทั้งที่มันอยู่
    /// ติดกับ "Open Status..." ในเมนูเดียวกัน กดพลาดง่ายมาก และผลของการกดพลาดคือความสามารถ
    /// ควบคุมเครื่องนี้จากฝั่งครูตายไปทั้งหมดแบบเงียบๆ (lock/shutdown warning/broadcast ใช้ไม่ได้เลย)
    ///
    /// ยังปิดโปรแกรมได้อยู่ตามโจทย์ (ไม่ได้ตัดความสามารถทิ้ง) แค่ไม่ใช่ปุ่มกดพลาดอีกต่อไป -
    /// dialog บอกผลที่จะตามมาชัดเจน และ default เป็น "No" (กด Enter/Esc = ไม่ปิด)
    /// </summary>
    private void ConfirmThenExit()
    {
        var confirmed = MessageBox.Show(
            "ต้องการปิด Onion ProcOparetor Tray ใช่หรือไม่?\n\n" +
            "ถ้าปิด ผู้ดูแลระบบจะควบคุมเครื่องนี้ไม่ได้อีก:\n" +
            "  • สั่งล็อก/ปลดล็อกหน้าจอไม่ได้\n" +
            "  • คำเตือนก่อนปิด/รีสตาร์ทเครื่องจะไม่แสดง (เครื่องจะดับโดยไม่เตือนล่วงหน้า)\n" +
            "  • ข้อความแจ้งเตือนจากครูจะไม่แสดง\n\n" +
            "โดยปกติไม่ควรปิด - ให้ปิดเฉพาะเมื่อผู้ดูแลระบบสั่งเท่านั้น",
            "ยืนยันการปิด Onion ProcOparetor Tray",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        // ปิดหน้าต่างที่กันปิดไว้ให้ "จริงๆ" ก่อน แล้วค่อย Shutdown()
        // WPF ตั้งใจให้ Application.Shutdown() ข้าม e.Cancel ของ Closing อยู่แล้ว แต่การพึ่ง
        // พฤติกรรมภายในตรงนั้นเสี่ยงเกินไปสำหรับจุดที่ "ปิดไม่ลง" แปลว่า Tray ค้างอยู่ตลอด
        //
        // LockScreenWindow/ShutdownNotifyWindow มี ForceClose() เป็น method ของตัวเอง (กันปิด
        // ด้วย _allowClose คนละกลไกกับ hide-on-close) ต้องเรียกของมันเองเท่านั้น - ถ้าปล่อยให้
        // ตกไปที่ extension method ตัว flag _allowClose จะไม่ถูกตั้ง แล้วหน้าต่างจะปิดไม่ลง
        HideAllLockScreens();
        _shutdownNotifyWindow?.ForceClose();
        _shutdownNotifyWindow = null;
        _examModeBadgeWindow?.ForceClose();
        _examModeBadgeWindow = null;
        _jokeShutdownController.Cancel();

        // ToList ก่อนวนลูป เพราะ Windows collection เปลี่ยนระหว่างที่ปิดทีละอัน
        foreach (var window in Windows.Cast<Window>().ToList())
        {
            HideOnCloseWindowBehavior.ForceClose(window);
        }

        Shutdown();
    }

    /// <summary>
    /// เปิดหน้า Status - รองรับหน้าต่างที่ถูก "ซ่อน" ไว้ (Hide()) ไม่ใช่ปิดจริง ด้วย
    /// (ดู HideOnCloseWindowBehavior) เลยต้องเรียก Show() ก่อน Activate() เสมอ ไม่งั้นกด
    /// "Open Status..." หลังจากเคยกดกากบาทปิดไปแล้วจะไม่มีอะไรเกิดขึ้นเลย
    /// </summary>
    private void OpenLoginWindow()
    {
        // ป้องกันเปิดซ้ำหลายหน้าต่างพร้อมกัน - เช็ค StatusWindow ก่อน LoginWindow เสมอ
        // เพราะถ้า login ไปแล้ว user ต้องการเห็นหน้า Status ไม่ใช่หน้า login ที่ค้างอยู่
        var existing = Windows.OfType<StatusWindow>().Cast<Window>().FirstOrDefault()
            ?? Windows.OfType<LoginWindow>().Cast<Window>().FirstOrDefault();

        if (existing is not null)
        {
            TrayDiagnostics.Log($"[ui] เปิดหน้าต่างเดิมที่มีอยู่แล้ว: {existing.GetType().Name}");
            RestoreAndActivate(existing);
            return;
        }

        TrayDiagnostics.Log("[ui] เปิดหน้า login ใหม่");
        var loginWindow = new LoginWindow(_apiClient);
        loginWindow.Show();
    }

    /// <summary>
    /// เอาหน้าต่างที่ถูกซ่อน/ย่อไว้กลับมาให้เห็น - ต้องมีเพราะกากบาทตอนนี้ = Hide() ไม่ใช่ Close()
    /// (ดู HideOnCloseWindowBehavior) ถ้าเรียกแค่ Activate() บนหน้าต่างที่ Hide() ไว้จะไม่มี
    /// อะไรเกิดขึ้นเลย - user จะกด "Open Status..." แล้วรู้สึกว่าโปรแกรมค้าง
    /// </summary>
    private static void RestoreAndActivate(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        EnsureOnScreen(window);
        window.Activate();
    }

    /// <summary>
    /// ลากหน้าต่างกลับเข้ามาในจอถ้ามันไปอยู่นอกพื้นที่ที่มองเห็นได้
    ///
    /// จำเป็นเพราะเครื่องที่ตื่นจาก sleep เปลี่ยน display topology ได้ (จอดับ/ต่อกลับ, ความละเอียด
    /// เปลี่ยน, จอที่สองหายไป) หน้าต่างที่เคยอยู่บนจอที่หายไปจะเหลือพิกัดที่ไม่มีจอรองรับแล้ว - Show()
    /// กับ Activate() จะ "สำเร็จ" ทั้งคู่แต่ผู้ใช้ไม่เห็นอะไรเลย ตรงกับที่รายงานว่า "กด tray ui แล้ว
    /// ไม่มีอะไรเกิดขึ้น" ทั้งที่โปรแกรมไม่ได้ค้างจริง (ยังไม่ยืนยันว่าเป็นสาเหตุจริงของเคสนั้น -
    /// แต่เป็นบั๊กที่มีอยู่จริงในตัวเองไม่ว่าจะใช่หรือไม่)
    ///
    /// VirtualScreen* ครอบคลุมทุกจอรวมกัน (ไม่ใช่แค่จอหลักแบบ WorkArea) - ต้องใช้ตัวนี้ ไม่งั้น
    /// หน้าต่างที่อยู่บนจอที่สองอย่างถูกต้องจะโดนลากกลับมาจอหลักโดยไม่จำเป็น
    /// </summary>
    private static void EnsureOnScreen(Window window)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        if (double.IsNaN(width) || double.IsNaN(height))
        {
            return; // ยังไม่รู้ขนาดจริง - ตัดสินไม่ได้ว่าอยู่นอกจอไหม อย่าไปขยับมั่ว
        }

        var bounds = new Rect(window.Left, window.Top, width, height);
        if (virtualScreen.IntersectsWith(bounds))
        {
            return; // ยังเห็นได้อยู่อย่างน้อยบางส่วน - ไม่ต้องยุ่ง
        }

        var area = SystemParameters.WorkArea;
        window.Left = area.Left + (area.Width - width) / 2;
        window.Top = area.Top + (area.Height - height) / 2;
        TrayDiagnostics.Log(
            $"[ui] {window.GetType().Name} อยู่นอกจอ ({bounds}) - ย้ายกลับมากึ่งกลางจอหลัก");
    }

    private void OnBroadcastMessageReceived(BroadcastMessageDto message)
    {
        // เรียกจาก background thread ของ pipe message consumer - BeginInvoke (ไม่ใช่ Invoke) ตั้งใจ
        // ไม่ block consumer thread รอ UI thread ว่าง (เหมือน pattern เดียวกับ ActionReportWindow ฝั่ง
        // Console) ไม่งั้นถ้า UI thread ไปติดค้างอะไรบางอย่าง (เช่น modal dialog ค้างรอ user) จะทำให้
        // message ถัดไปในคิว (lock/unlock/VNC ฯลฯ) หยุดรอตามไปด้วยทั้งที่ไม่เกี่ยวข้องกันเลย
        Dispatcher.BeginInvoke(() =>
        {
            var popup = new BroadcastPopupWindow(message.Title, message.Message);
            popup.Show();
        });
    }

    /// <summary>เรียกจาก background thread ของ pipe message consumer - idempotent (เรียกซ้ำตอนล็อกอยู่แล้วไม่มีผล) เปิด LockScreenWindow แยกหนึ่งตัวต่อจอที่ต่ออยู่ตอนนี้ (ไม่ใช่แค่จอหลัก)
    /// resolvedMessage (WP3-3, 2026-10-31) - ข้อความที่ Agent ตัดสินแล้ว (ดู CommandProcessor.HandleShowLockScreenAsync) ส่งต่อให้ทุกจอเหมือนกัน</summary>
    private void OnShowLockScreenRequested(string? resolvedMessage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_lockScreenWindows.Count > 0)
            {
                return; // ล็อกอยู่แล้ว - ไม่ต้องเปิดซ้อน
            }

            var screens = System.Windows.Forms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var isOwner = i == 0; // ดู LockScreenWindow doc comment - เจ้าของ hook/watchdog มีแค่จอเดียว
                var window = new LockScreenWindow(screens[i], installInputBlocking: isOwner, resolvedMessage);

                if (isOwner)
                {
                    window.AgentDiedWhileLocked += OnAgentDiedWhileLocked;
                }

                _lockScreenWindows.Add(window);
                window.Show();
            }

            _lockScreenWindows[0].Activate();
        });
    }

    /// <summary>watchdog ของ instance เจ้าของ hook ตรวจพบว่า process ของ Agent หายไปกลางคันขณะล็อกอยู่ (ดู LockScreenWindow.CheckAgentHealth) - เรียกจาก UI thread อยู่แล้ว (DispatcherTimer.Tick) ไม่ต้อง Dispatcher.Invoke ซ้ำ</summary>
    private void OnAgentDiedWhileLocked() => HideAllLockScreens();

    /// <summary>เรียกจาก background thread ของ pipe message consumer - idempotent (เรียกซ้ำตอนไม่ได้ล็อกอยู่ไม่มีผล)</summary>
    private void OnHideLockScreenRequested()
    {
        Dispatcher.BeginInvoke(HideAllLockScreens);
    }

    /// <summary>ปิดทุกจอพร้อมกันเป็นชุดเดียวเสมอ (ไม่มีทางปลดล็อกทีละจอ) - เรียกได้ทั้งจากทาง Console สั่งปกติและจาก watchdog ฉุกเฉิน (OnAgentDiedWhileLocked) ใช้ path เดียวกันทั้งคู่</summary>
    private void HideAllLockScreens()
    {
        foreach (var window in _lockScreenWindows)
        {
            window.AgentDiedWhileLocked -= OnAgentDiedWhileLocked; // no-op ถ้าไม่เคย subscribe (instance ที่ไม่ใช่เจ้าของ hook)
            window.ForceClose();
        }

        _lockScreenWindows.Clear();
    }

    /// <summary>
    /// เรียกจาก background thread ของ pipe listener - start winvnc.exe เอง (ที่นี่เท่านั้นที่ capture
    /// หน้าจอ user session ได้จริง - ดู comment ใน VncProcessManager) แล้วรายงานผลกลับ Agent ผ่าน local
    /// HTTP callback ให้ Agent (มีสิทธิ์ LocalSystem) ไปทำ port discovery/firewall/relay ต่อ ไม่ต้อง
    /// marshal เข้า UI thread เพราะไม่ได้แตะ WPF UI element ใดๆ เลย
    /// </summary>
    private async void OnStartVncSessionRequested(string requestingConsoleSessionId)
    {
        var (success, pid, password, failureReason) = await _vncProcessManager.EnsureStartedAsync(CancellationToken.None);
        await _apiClient.PostVncSessionResultAsync(requestingConsoleSessionId, success, pid, password, failureReason);
    }

    /// <summary>เรียกจาก background thread ของ pipe listener - หยุด winvnc.exe ไม่ต้อง marshal เข้า UI thread เพราะไม่ได้แตะ WPF UI element ใดๆ เลย (เหมือน OnStartVncSessionRequested)</summary>
    private void OnStopVncSessionRequested() => _vncProcessManager.Stop();

    /// <summary>
    /// เรียกจาก background thread ของ pipe listener - สแกนโปรแกรมที่มีหน้าต่างบน desktop จริง (ที่นี่
    /// เท่านั้นที่เห็นหน้าต่างของ user session ได้จริง) แล้วรายงานผลกลับ Agent ผ่าน local HTTP callback
    /// ไม่ต้อง marshal เข้า UI thread เพราะ Process.GetProcesses()/MainWindowHandle ไม่ต้องใช้ WPF dispatcher
    /// </summary>
    private async void OnGetRunningProcessesRequested(string requestingConsoleSessionId)
    {
        List<RunningProcessDto> processes;
        string? error = null;
        try
        {
            processes = RunningProcessScanner.ScanVisibleProcesses();
        }
        catch (Exception ex)
        {
            processes = new List<RunningProcessDto>();
            error = ex.Message;
        }

        await _apiClient.PostRunningProcessesResultAsync(requestingConsoleSessionId, error is null, processes, error);
    }

    /// <summary>
    /// เรียกจาก background thread ของ pipe listener - ต้อง marshal เข้า UI thread เพราะ
    /// PhysicalInputBlocker.Block() สร้าง DispatcherTimer ข้างในซึ่งต้องสร้างจาก UI thread เท่านั้น
    /// idempotent (ครูกดซ้ำตอนบล็อกอยู่แล้วไม่มีผล)
    /// </summary>
    private void OnBlockTargetInputRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _physicalInputBlocker.AgentDiedWhileBlocked -= OnAgentDiedWhileBlocked; // กัน subscribe ซ้ำถ้ากดสลับเร็วๆ
            _physicalInputBlocker.AgentDiedWhileBlocked += OnAgentDiedWhileBlocked;
            _physicalInputBlocker.Block();
        });
    }

    /// <summary>เรียกจาก background thread ของ pipe message consumer - เหตุผลเดียวกับ OnBlockTargetInputRequested (marshal เข้า UI thread) idempotent (ครูกดซ้ำตอนไม่ได้บล็อกอยู่ไม่มีผล)</summary>
    private void OnUnblockTargetInputRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _physicalInputBlocker.AgentDiedWhileBlocked -= OnAgentDiedWhileBlocked;
            _physicalInputBlocker.Unblock();
        });
    }

    /// <summary>
    /// Failsafe path 2 (ดู PhysicalInputBlocker doc comment) - watchdog ของตัวเองตรวจพบว่า Agent หายไป
    /// กลางคันขณะกำลังบล็อกอยู่ - เรียกจาก UI thread อยู่แล้ว (DispatcherTimer.Tick) ไม่ต้อง
    /// Dispatcher.Invoke ซ้ำ (เหมือน OnAgentDiedWhileLocked ของ LockScreenWindow)
    /// </summary>
    private void OnAgentDiedWhileBlocked()
    {
        _physicalInputBlocker.AgentDiedWhileBlocked -= OnAgentDiedWhileBlocked;
        _physicalInputBlocker.Unblock();
    }

    /// <summary>
    /// เรียกจาก StatusWindow ตอนกด toggle Bypass เป็น "เปิดใช้งาน" - ใช้ cleanup sequence เดียวกับ
    /// OnExit เป๊ะๆ (Stop VNC / ปิดหน้าจอล็อกทุกจอ / คืนสิทธิ์ physical input) เพราะ resource ทั้ง 3
    /// ตัวนี้อยู่ฝั่ง AgentTray ล้วนๆ (Agent เอง Session 0 แตะไม่ถึง) - ต้องเก็บกวาดที่นี่ก่อน ไม่ปล่อยค้าง
    /// ถ้ามี session ใดๆ ยัง active อยู่ตอน bypass เปิด เรียกได้ปลอดภัยแม้ไม่มีอะไร active อยู่เลย
    /// (unconditional cleanup เหมือน OnExit)
    /// </summary>
    public void CleanupInteractiveSessionsForBypass()
    {
        HideAllLockScreens();
        _physicalInputBlocker.Unblock();
        _vncProcessManager.Stop();
    }

    /// <summary>
    /// เรียกจาก background thread ของ pipe listener ตอน Agent สั่ง shutdown/restart -
    /// เปิดหน้าต่างนับถอยหลังมุมจอ (ปิดตัวเองเมื่อนับครบ)
    ///
    /// ถ้ามีหน้าต่างเดิมค้างอยู่ (เช่นครูสั่ง Shutdown แล้วเปลี่ยนใจสั่ง Restart ทับ หรือส่งคำสั่ง
    /// ซ้ำด้วยเวลาใหม่) ให้ทิ้งอันเดิมแล้วเปิดใหม่ตามคำสั่งล่าสุดเสมอ - ไม่ใช่ idempotent
    /// แบบ lock screen เพราะเวลาที่เหลือของคำสั่งใหม่ต่างจากของเดิม ถ้าไม่ทับจะโชว์เวลาผิด
    /// </summary>
    private void OnShutdownNotifyRequested(ShutdownNotifyDto notify)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _shutdownNotifyWindow?.ForceClose();
            _jokeShutdownController.Cancel();

            _shutdownNotifyWindow = new ShutdownNotifyWindow(notify);
            _shutdownNotifyWindow.Closed += (_, _) =>
            {
                _shutdownNotifyWindow = null;

                // จอฟ้าปลอม (ถ้ากำลังโชว์อยู่) ต้องปิดพร้อมกันเสมอ ไม่ว่า ShutdownNotifyWindow จะปิด
                // เพราะนับครบ (shutdown จริงกำลังจะเกิด) หรือเพราะมีคำสั่งใหม่มาทับก่อนก็ตาม
                _jokeShutdownController.Cancel();
            };
            _shutdownNotifyWindow.Show();

            // Easter egg - ปิด default เสมอ (JokeShutdown:Enabled ใน appsettings.json) ดู
            // Services/JokeShutdownController.cs ว่าทำไมต้อง >= 10 วิ
            if (JokeShutdownController.ShouldRun(notify.DelaySeconds))
            {
                _jokeShutdownController.Start(notify.DelaySeconds);
            }
        });
    }

    /// <summary>
    /// ดึงสถานะโหมดสอบปัจจุบันจาก Agent (GET /status) ครั้งเดียวตอน startup - ดู comment ที่จุดเรียก
    /// ใน OnStartup ว่าทำไมต้อง pull ด้วย ไม่พึ่ง push อย่างเดียว คืนเงียบๆ ถ้า Agent local API ยังไม่
    /// ตอบ (เช่น service กำลังเริ่มพร้อมกันอยู่) - push message รอบถัดไปจะมาเติมให้เองถ้าพลาดตอนนี้
    /// </summary>
    private async Task InitializeExamModeBadgeFromStatusAsync()
    {
        var status = await _apiClient.GetStatusAsync();
        if (status?.ExamModeExpiresAtUtc is { } expiresAtUtc)
        {
            Dispatcher.BeginInvoke(() => ShowOrUpdateExamModeBadge(expiresAtUtc));
        }
    }

    /// <summary>เรียกจาก background thread ของ pipe listener (WP2-2, 2026-10-31) - expiresAtUtc null = ไม่มีโหมดสอบแล้ว ซ่อนป้าย</summary>
    private void OnExamModeStatusChanged(DateTime? expiresAtUtc)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (expiresAtUtc is { } value)
            {
                ShowOrUpdateExamModeBadge(value);
            }
            else
            {
                _examModeBadgeWindow?.ForceClose();
                _examModeBadgeWindow = null;
            }
        });
    }

    private void ShowOrUpdateExamModeBadge(DateTime expiresAtUtc)
    {
        if (_examModeBadgeWindow is not null)
        {
            _examModeBadgeWindow.UpdateExpiresAt(expiresAtUtc);
            return;
        }

        _examModeBadgeWindow = new ExamModeBadgeWindow(expiresAtUtc);
        _examModeBadgeWindow.Closed += (_, _) => _examModeBadgeWindow = null;
        _examModeBadgeWindow.Show();
    }

    // ==================== Sign-in (WP3-3, 2026-10-31) ====================

    /// <summary>ดึงสถานะ sign-in ปัจจุบันจาก Agent (GET /status) ครั้งเดียวตอน startup - เหตุผลเดียวกับ InitializeExamModeBadgeFromStatusAsync (push ผ่าน pipe ส่งแค่ตอน "สถานะเปลี่ยน" เท่านั้น)</summary>
    private async Task InitializeSignInStateFromStatusAsync()
    {
        var status = await _apiClient.GetStatusAsync();
        if (status is null)
        {
            return; // Agent local API ยังไม่ตอบ (เช่น service กำลังเริ่มพร้อมกันอยู่) - push ครั้งถัดไปมาเติมให้เอง
        }

        _idleMonitor.UpdateThresholdMinutes(status.AutoLogoutIdleMinutes);

        // sync สถานะนักเรียนที่ Agent จำไว้ (เผื่อ Tray ถูก auto-recover ขึ้นมาใหม่กลางเซสชันที่ login ค้างอยู่แล้ว)
        if (!string.IsNullOrWhiteSpace(status.CurrentStudentCode))
        {
            Dispatcher.BeginInvoke(() =>
            {
                _currentStudentCode = status.CurrentStudentCode;
                _currentStudentName = status.CurrentStudentName;
                RebuildTrayContextMenu();
            });
        }

        Dispatcher.BeginInvoke(() => ApplySignInStatus(status.RequireLogin, status.LoginScreenMessage));
    }

    /// <summary>เรียกจาก background thread ของ pipe listener (WP3-3, 2026-10-31) - requireLogin/message เปลี่ยนจริงเท่านั้น (ดู Worker.ScanRunningProcessesAsync ฝั่ง Agent)</summary>
    private void OnSignInStatusChanged(bool requireLogin, string? loginScreenMessage)
    {
        Dispatcher.BeginInvoke(() => ApplySignInStatus(requireLogin, loginScreenMessage));
    }

    /// <summary>จุดเดียวที่ตัดสินใจว่าควรโชว์/ซ่อนจอล็อกอินนักเรียนไหม - เรียกได้ทั้งจาก pull (startup) และ push (เปลี่ยนสถานะ) ต้องอยู่บน UI thread แล้วเท่านั้น</summary>
    private void ApplySignInStatus(bool requireLogin, string? loginScreenMessage)
    {
        _requireLogin = requireLogin;
        _loginScreenMessage = loginScreenMessage;

        var alreadyLoggedIn = _currentStudentCode is not null;
        var staffSessionActive = _staffSessionBadgeWindow is not null;

        if (requireLogin && !alreadyLoggedIn && !staffSessionActive)
        {
            ShowStudentLoginWindow();
        }
        else if ((!requireLogin || alreadyLoggedIn) && !staffSessionActive)
        {
            // เลิกบังคับ login แล้ว หรือมีคน login อยู่แล้ว (เช่น login เสร็จไปพอดีก่อนจะได้ค่านี้มา) -
            // ปิดจอล็อกอินที่ค้างอยู่ (ถ้ามี) ตามที่ roadmap ระบุ "ปิด: เครื่องที่ค้างอยู่หน้าล็อกอินจะ
            // ปลดเองภายใน 1 รอบ poll"
            HideStudentLoginWindowIfPresent();
        }
        else if (_studentLoginWindow is { } existing)
        {
            // staff session กำลังทำงานอยู่ - อัปเดตข้อความไว้เฉยๆ แต่ไม่โชว์ซ้อนทับ (จะโชว์ตอน staff
            // session จบถ้ายังต้องบังคับ login อยู่ - ดู OnStaffSessionEnded)
            existing.Hide();
        }
    }

    private void ShowStudentLoginWindow()
    {
        if (_studentLoginWindow is not null)
        {
            _studentLoginWindow.Show();
            return;
        }

        var window = new StudentLoginWindow(_apiClient, _loginScreenMessage);
        window.LoginSucceeded += OnStudentLoginSucceeded;
        window.StaffSessionRequested += OnStaffSessionRequested;
        window.Closed += (_, _) => _studentLoginWindow = null;
        _studentLoginWindow = window;
        window.Show();
    }

    private void HideStudentLoginWindowIfPresent()
    {
        _studentLoginWindow?.ForceClose();
        _studentLoginWindow = null;
    }

    /// <summary>login (หรือเปลี่ยนรหัสผ่านครั้งแรกแล้ว login) สำเร็จ - นักเรียนคนนี้ "เข้าสู่ระบบ" แล้วอย่างเป็นทางการ</summary>
    private void OnStudentLoginSucceeded(Models.StudentDto student, int? sessionId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _currentStudentCode = student.StudentCode;
            _currentStudentName = student.FullName;
            _currentSessionId = sessionId;
            _idleMonitor.Reset();
            RebuildTrayContextMenu();

            new SessionToastWindow($"เข้าสู่ระบบเป็น {student.FullName}").Show();
            TrayDiagnostics.Log($"[sign-in] นักเรียน '{student.StudentCode}' ({student.FullName}) เข้าสู่ระบบแล้ว");
        });
    }

    /// <summary>กด "สำหรับครู: Staff session" ที่จอล็อกอินนักเรียน - เปิด StaffSessionStartWindow ซ้อนทับ (ไม่ปิดจอล็อกอินนักเรียนทันที เผื่อครูกดยกเลิก)</summary>
    private void OnStaffSessionRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var window = new StaffSessionStartWindow(_apiClient);
            window.StaffSessionStarted += OnStaffSessionStarted;
            window.ShowDialog();
        });
    }

    private void OnStaffSessionStarted(int minutes)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // ครูเพิ่งเริ่ม staff session จากจอล็อกอินนักเรียน (ไม่ได้ผ่าน StatusWindow) - เก็บกวาด
            // resource เดียวกับที่ StatusWindow.ToggleBypassButton_Click ทำ (ดู
            // CleanupInteractiveSessionsForBypass) เผื่อมี VNC/lock screen/physical-input-block ค้างอยู่
            CleanupInteractiveSessionsForBypass();

            _studentLoginWindow?.Hide();

            var badge = new StaffSessionBadgeWindow(_apiClient);
            badge.SessionEnded += OnStaffSessionEnded;
            badge.Closed += (_, _) => _staffSessionBadgeWindow = null;
            _staffSessionBadgeWindow = badge;
            badge.Show();

            TrayDiagnostics.Log($"[sign-in] Staff session เริ่มทำงาน ({minutes} นาที)");
        });
    }

    private void OnStaffSessionEnded()
    {
        Dispatcher.BeginInvoke(() =>
        {
            TrayDiagnostics.Log("[sign-in] Staff session จบแล้ว");

            // กลับไปจอล็อกอินนักเรียนถ้ายังต้องบังคับ login อยู่และยังไม่มีใคร login ค้าง (ดู ApplySignInStatus)
            ApplySignInStatus(_requireLogin, _loginScreenMessage);
        });
    }

    /// <summary>เรียกจาก IdleActivityMonitor (UI thread อยู่แล้ว - DispatcherTimer.Tick) - logout อัตโนมัติตาม SignInSettings.AutoLogoutIdleMinutes (ดู docs/roadmap.md)</summary>
    private void OnIdleThresholdReached()
    {
        if (_currentStudentCode is null)
        {
            return; // ไม่มีใคร login อยู่ - ไม่มีอะไรให้ logout
        }

        TrayDiagnostics.Log($"[sign-in] ไม่ได้ใช้งานเกินเวลาที่ตั้งไว้ - logout นักเรียน '{_currentStudentCode}' อัตโนมัติ");
        _ = PerformStudentLogoutAsync(Models.StudentSessionEndReason.Idle);
    }

    /// <summary>เครื่องกำลังจะปิด/รีสตาร์ท/ออกจาก session Windows - best-effort logout (ดู doc comment ที่จุด subscribe ใน OnStartup)</summary>
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
    {
        if (_currentStudentCode is not null)
        {
            _ = PerformStudentLogoutAsync(Models.StudentSessionEndReason.Shutdown);
        }
    }

    /// <summary>จุดเดียวที่ logout นักเรียนจริง - ใช้ร่วมกันทั้ง "ไม่ใช่ฉัน"/"ออกจากระบบ" (tray menu), idle timeout, และตอนเครื่องปิด/รีสตาร์ท</summary>
    private async Task PerformStudentLogoutAsync(Models.StudentSessionEndReason reason)
    {
        var clientGuid = (await _apiClient.GetStatusAsync())?.ClientGuid;
        if (string.IsNullOrWhiteSpace(clientGuid))
        {
            return;
        }

        await GetOrCreateStudentApiClient().LogoutAsync(clientGuid, _currentSessionId, reason);
        await _apiClient.NotifyStudentSessionAsync(false);

        Dispatcher.BeginInvoke(() =>
        {
            _currentStudentCode = null;
            _currentStudentName = null;
            _currentSessionId = null;
            _idleMonitor.Reset();
            RebuildTrayContextMenu();

            // กลับไปจอล็อกอินถ้ายังต้องบังคับ login อยู่ (เช่น logout เองโดยสมัครใจระหว่างที่ยังบังคับ login)
            ApplySignInStatus(_requireLogin, _loginScreenMessage);
        });
    }

    private StudentApiClient GetOrCreateStudentApiClient()
    {
        if (_studentApiClient is not null)
        {
            return _studentApiClient;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var baseUrl = config["ServerSettings:BaseUrl"] ?? "http://localhost:8787";
        var fallbackUrl = config["ServerSettings:FallbackBaseUrl"];
        _studentApiClient = new StudentApiClient(baseUrl, fallbackUrl);
        return _studentApiClient;
    }

    /// <summary>เพิ่ม/ลบรายการ "ไม่ใช่ฉัน"/"ออกจากระบบ" ใน tray context menu ตามว่ามีนักเรียน login อยู่ไหม (WP3-3, 2026-10-31)</summary>
    private void RebuildTrayContextMenu()
    {
        if (_trayIcon?.ContextMenu is not { } menu)
        {
            return;
        }

        var isLoggedIn = _currentStudentCode is not null;
        var alreadyPresent = menu.Items.Contains(_notMeMenuItem);

        if (isLoggedIn && !alreadyPresent)
        {
            var insertAt = Math.Min(1, menu.Items.Count);
            menu.Items.Insert(insertAt, _studentMenuSeparator);
            menu.Items.Insert(insertAt + 1, _notMeMenuItem);
            menu.Items.Insert(insertAt + 2, _logoutMenuItem);
        }
        else if (!isLoggedIn && alreadyPresent)
        {
            menu.Items.Remove(_studentMenuSeparator);
            menu.Items.Remove(_notMeMenuItem);
            menu.Items.Remove(_logoutMenuItem);
        }
    }

#if DEBUG
    /// <summary>--shot=&lt;path&gt; เรนเดอร์หน้าต่างเป็น PNG แล้วปิดโปรแกรม (โหมดตรวจ UI เท่านั้น) - ก๊อปแนวทางเดียวกับ Console/App.xaml.cs CapturePreview เป๊ะๆ (ดู comment ที่นั่นสำหรับเหตุผลที่ต้องเรนเดอร์เองในแอป ไม่จับภาพจาก desktop)</summary>
    private void CapturePreview(Window window, string[] args)
    {
        var shot = args.FirstOrDefault(a => a.StartsWith("--shot=", StringComparison.Ordinal));
        if (shot is null)
        {
            return;
        }

        var path = shot["--shot=".Length..];
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            Shutdown();
        });
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _idleMonitor.Stop();
        _broadcastListenerCts.Cancel();
        HideAllLockScreens();
        _physicalInputBlocker.Unblock();
        _vncProcessManager.Stop();
        _shutdownNotifyWindow?.ForceClose();
        _examModeBadgeWindow?.ForceClose();
        _studentLoginWindow?.ForceClose();
        _staffSessionBadgeWindow?.ForceClose();
        _jokeShutdownController.Cancel();
        _trayIcon?.Dispose();
        _singleInstanceGuard.Dispose();
        base.OnExit(e);
    }
}
