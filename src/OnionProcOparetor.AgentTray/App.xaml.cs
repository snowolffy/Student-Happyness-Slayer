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

    // instance ต่อจอ (System.Windows.Forms.Screen.AllScreens) - เฉพาะตัวแรก (index 0) เท่านั้นที่ติดตั้ง
    // keyboard/mouse hook + Agent watchdog จริง (ดู LockScreenWindow doc comment) รายการนี้ว่างเปล่า =
    // ไม่ได้ล็อกอยู่ (ใช้แทนการเช็ค field เดี่ยวเดิมที่รองรับแค่จอเดียว)
    private readonly List<LockScreenWindow> _lockScreenWindows = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            exitMenuItem.Click += (_, _) => Shutdown();
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

    private void OpenLoginWindow()
    {
        // ป้องกันเปิดซ้ำหลายหน้าต่างพร้อมกัน
        foreach (Window window in Windows)
        {
            if (window is LoginWindow or StatusWindow)
            {
                window.Activate();
                return;
            }
        }

        var loginWindow = new LoginWindow(_apiClient);
        loginWindow.Show();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _broadcastListenerCts.Cancel();
        HideAllLockScreens();
        _physicalInputBlocker.Unblock();
        _vncProcessManager.Stop();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
