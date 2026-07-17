using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private readonly ClientApiClient _apiClient = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ตั้งค่า auto-start ผ่าน Registry Run key (HKCU) ให้อัตโนมัติทุกครั้งที่ Tray เริ่มทำงาน
        // ทำงานแบบ idempotent - เรียกซ้ำได้ไม่มีผลข้างเคียง ถ้าตั้งไว้แล้วและ path ไม่เปลี่ยนจะไม่เขียนซ้ำ
        RegistryStartup.EnsureAutoStartEnabled();

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

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
