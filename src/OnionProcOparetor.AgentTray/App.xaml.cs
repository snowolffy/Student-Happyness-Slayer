using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using OnionProcOparetor.AgentTray.Models;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private readonly ClientApiClient _apiClient = new();
    private readonly BroadcastPipeListener _broadcastListener = new();
    private readonly CancellationTokenSource _broadcastListenerCts = new();
    private LockScreenWindow? _lockScreenWindow;

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
        _broadcastListener.Start(_broadcastListenerCts.Token);

        try
        {
            _trayIcon = new TaskbarIcon
            {
                // ใช้ไอคอน default ของระบบไปก่อน - ทีหลังเปลี่ยนเป็นไอคอนของแอปเองได้
                Icon = System.Drawing.SystemIcons.Shield,
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

    /// <summary>เรียกจาก background thread ของ pipe listener - idempotent (เรียกซ้ำตอนล็อกอยู่แล้วไม่มีผล)</summary>
    private void OnShowLockScreenRequested()
    {
        Dispatcher.Invoke(() =>
        {
            if (_lockScreenWindow is not null)
            {
                return; // ล็อกอยู่แล้ว - ไม่ต้องเปิดซ้อน
            }

            _lockScreenWindow = new LockScreenWindow();
            _lockScreenWindow.Show();
            _lockScreenWindow.Activate();
        });
    }

    /// <summary>เรียกจาก background thread ของ pipe listener - idempotent (เรียกซ้ำตอนไม่ได้ล็อกอยู่ไม่มีผล)</summary>
    private void OnHideLockScreenRequested()
    {
        Dispatcher.Invoke(() =>
        {
            _lockScreenWindow?.ForceClose();
            _lockScreenWindow = null;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _broadcastListenerCts.Cancel();
        _lockScreenWindow?.ForceClose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
