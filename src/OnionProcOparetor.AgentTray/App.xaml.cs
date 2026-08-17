using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        _broadcastListener.Start(_broadcastListenerCts.Token);

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

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "Exit Tray" };
            exitMenuItem.Click += (_, _) => ConfirmThenExit();
            contextMenu.Items.Add(exitMenuItem);

            _trayIcon.ContextMenu = contextMenu;

            // double-click ที่ icon ก็เปิดหน้า login เหมือนกัน
            _trayIcon.TrayMouseDoubleClick += (_, _) => OpenLoginWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error: {ex}", "Onion ProcOparetor Tray Error");
            Shutdown();
        }
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
            RestoreAndActivate(existing);
            return;
        }

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

        window.Activate();
    }

    private void OnBroadcastMessageReceived(BroadcastMessageDto message)
    {
        // เรียกจาก background thread ของ pipe listener - ต้อง marshal เข้า UI thread ก่อนสร้าง Window
        Dispatcher.Invoke(() =>
        {
            var popup = new BroadcastPopupWindow(message.Title, message.Message);
            popup.Show();
        });
    }

    /// <summary>เรียกจาก background thread ของ pipe listener - idempotent (เรียกซ้ำตอนล็อกอยู่แล้วไม่มีผล) เปิด LockScreenWindow แยกหนึ่งตัวต่อจอที่ต่ออยู่ตอนนี้ (ไม่ใช่แค่จอหลัก)</summary>
    private void OnShowLockScreenRequested()
    {
        Dispatcher.Invoke(() =>
        {
            if (_lockScreenWindows.Count > 0)
            {
                return; // ล็อกอยู่แล้ว - ไม่ต้องเปิดซ้อน
            }

            var screens = System.Windows.Forms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var isOwner = i == 0; // ดู LockScreenWindow doc comment - เจ้าของ hook/watchdog มีแค่จอเดียว
                var window = new LockScreenWindow(screens[i], installInputBlocking: isOwner);

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

    /// <summary>เรียกจาก background thread ของ pipe listener - idempotent (เรียกซ้ำตอนไม่ได้ล็อกอยู่ไม่มีผล)</summary>
    private void OnHideLockScreenRequested()
    {
        Dispatcher.Invoke(HideAllLockScreens);
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
    /// เรียกจาก background thread ของ pipe listener - ต้อง marshal เข้า UI thread เพราะ
    /// PhysicalInputBlocker.Block() สร้าง DispatcherTimer ข้างในซึ่งต้องสร้างจาก UI thread เท่านั้น
    /// idempotent (ครูกดซ้ำตอนบล็อกอยู่แล้วไม่มีผล)
    /// </summary>
    private void OnBlockTargetInputRequested()
    {
        Dispatcher.Invoke(() =>
        {
            _physicalInputBlocker.AgentDiedWhileBlocked -= OnAgentDiedWhileBlocked; // กัน subscribe ซ้ำถ้ากดสลับเร็วๆ
            _physicalInputBlocker.AgentDiedWhileBlocked += OnAgentDiedWhileBlocked;
            _physicalInputBlocker.Block();
        });
    }

    /// <summary>เรียกจาก background thread ของ pipe listener - เหตุผลเดียวกับ OnBlockTargetInputRequested (marshal เข้า UI thread) idempotent (ครูกดซ้ำตอนไม่ได้บล็อกอยู่ไม่มีผล)</summary>
    private void OnUnblockTargetInputRequested()
    {
        Dispatcher.Invoke(() =>
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
        Dispatcher.Invoke(() =>
        {
            _shutdownNotifyWindow?.ForceClose();

            _shutdownNotifyWindow = new ShutdownNotifyWindow(notify);
            _shutdownNotifyWindow.Closed += (_, _) => _shutdownNotifyWindow = null;
            _shutdownNotifyWindow.Show();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _broadcastListenerCts.Cancel();
        HideAllLockScreens();
        _physicalInputBlocker.Unblock();
        _vncProcessManager.Stop();
        _shutdownNotifyWindow?.ForceClose();
        _trayIcon?.Dispose();
        _singleInstanceGuard.Dispose();
        base.OnExit(e);
    }
}
