using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using OnionProcOparetor.RemoteBridge.Services;

namespace OnionProcOparetor.RemoteBridge;

/// <summary>
/// Entry point ของ RemoteBridge - tray app แยกเดี่ยว ไม่แตะ Server/Console/Agent เลย
/// (ดู project memory เรื่อง remote lock bridge) ล็อกอินด้วยบัญชี admin ธรรมดาแล้วเป็นอีกหนึ่ง
/// "ผู้เรียก" ของ API เดิมที่มีอยู่แล้วเท่านั้น
/// </summary>
public partial class App : System.Windows.Application
{
    private TaskbarIcon? _trayIcon;
    private readonly ApiClient _apiClient = new();
    private readonly SingleInstanceGuard _singleInstanceGuard = new();
    private readonly RemoteTriggerListener _triggerListener;

    public App()
    {
        _triggerListener = new RemoteTriggerListener(_apiClient);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Safety net ทั้งโปรแกรม (พบจริง 2026-09-13: exception ที่ไม่ได้ดักใน event handler ตัวเดียว
        // ล้มทั้ง process ทันทีโดย default ของ WPF) - RemoteBridge ต้องรันค้างข้ามวันโดยไม่มีคนเฝ้า
        // จุดพังจุดเดียวไม่ควรทำให้ทั้ง tray หายไปเงียบๆ ดีกว่าโชว์ error แล้วให้ผู้ใช้ลองใหม่ได้
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!_singleInstanceGuard.TryAcquire())
        {
            MessageBox.Show("OnionProcOparetor RemoteBridge กำลังทำงานอยู่แล้ว", "RemoteBridge");
            Shutdown();
            return;
        }

        try
        {
            _trayIcon = new TaskbarIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)
                       ?? System.Drawing.SystemIcons.Application,
                ToolTipText = "Onion ProcOparetor - Remote Bridge"
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var openItem = new System.Windows.Controls.MenuItem { Header = "Open..." };
            openItem.Click += (_, _) => OpenMainWindow();
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitItem.Click += (_, _) => Shutdown();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.TrayMouseDoubleClick += (_, _) => OpenMainWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error: {ex}", "RemoteBridge Error");
            Shutdown();
            return;
        }

        OpenMainWindow();

        // เริ่มฟัง local HTTP ทันทีไม่ว่าจะ login แล้วหรือยัง (OpenMainWindow ข้างบนโหลด config มา
        // ให้ ConfigStore.Current แล้วถ้ามี) - RemoteTriggerListener ตอบ 503 เองถ้ายังไม่ login แทนที่
        // จะปฏิเสธการเชื่อมต่อไปเลย ผู้ใช้จะได้ตั้งค่า Tailscale Funnel รอไว้ล่วงหน้าได้ทันทีตั้งแต่
        // ครั้งแรกที่เปิดโปรแกรม ไม่ต้อง login สำเร็จก่อนถึงจะเริ่ม test connectivity ได้
        _ = _triggerListener.StartAsync(ConfigStore.Current?.LocalListenerPort ?? new RemoteBridgeConfig().LocalListenerPort);
    }

    /// <summary>เปิด StatusWindow ถ้ามี config ที่บันทึกไว้แล้ว ไม่งั้นเปิด LoginWindow ก่อน - รองรับหน้าต่างที่ถูกซ่อนไว้ (Hide) ด้วย เรียก Show() ซ้ำได้เสมอ</summary>
    private void OpenMainWindow()
    {
        var existing = Windows.OfType<StatusWindow>().Cast<Window>().FirstOrDefault()
            ?? Windows.OfType<LoginWindow>().Cast<Window>().FirstOrDefault();

        if (existing is not null)
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var config = ConfigStore.Load();
        if (config is not null && !string.IsNullOrEmpty(config.EncryptedPassword))
        {
            new StatusWindow(_apiClient, config).Show();
        }
        else
        {
            new LoginWindow(_apiClient).Show();
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"เกิดข้อผิดพลาดที่ไม่คาดคิด:\n{e.Exception.Message}",
            "RemoteBridge - Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _triggerListener.StopAsync().GetAwaiter().GetResult();
        _trayIcon?.Dispose();
        _singleInstanceGuard.Dispose();
        base.OnExit(e);
    }
}
