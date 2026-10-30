using System.Windows;
using System.Windows.Media;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

public partial class StatusWindow : Window
{
    private readonly ClientApiClient _apiClient;

    // ค่าล่าสุดที่ยืนยันแล้วจาก Agent จริง (ไม่ใช่แค่ค่าที่ user เพิ่งกด) - ใช้คำนวณว่าจะส่ง toggle
    // ไปเป็นค่าอะไรตอนกดปุ่มครั้งถัดไป กัน UI ค้างผิดสถานะถ้า toggle ครั้งก่อนล้มเหลวกลางทาง
    private bool _lastKnownBypassed;

    public StatusWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        // กากบาท = ซ่อน ไม่ใช่ปิด + ไม่โผล่บน taskbar (ดู HideOnCloseWindowBehavior)
        // เปิดกลับมาได้จาก tray icon เสมอ และไม่ต้อง login ใหม่ทุกครั้งที่กดกากบาท
        this.EnableHideOnClose();

        Loaded += async (_, _) => await RefreshDataAsync();
    }

    private async Task RefreshDataAsync()
    {
        var status = await _apiClient.GetStatusAsync();

        if (status is null)
        {
            MachineNameText.Text = "ไม่สามารถเชื่อมต่อ Onion ProcOparetor Agent Service ได้";
            EnabledDot.Fill = (Brush)FindResource("AccentNeutralBrush");
            EnabledText.Text = "Unknown";
            ConnectionDot.Fill = (Brush)FindResource("AccentNeutralBrush");
            ConnectionText.Text = "Unknown";
            ToggleBypassButton.IsEnabled = false;
            return;
        }

        MachineNameText.Text = status.MachineName;
        RulesCountText.Text = status.RulesCount.ToString();
        LastPollText.Text = status.LastPollAt.HasValue
            ? status.LastPollAt.Value.ToLocalTime().ToString("HH:mm:ss")
            : "-";

        if (status.IsEnabled)
        {
            EnabledDot.Fill = (Brush)FindResource("AccentGreenBrush");
            EnabledText.Text = "Enabled";
        }
        else
        {
            EnabledDot.Fill = (Brush)FindResource("AccentRedBrush");
            EnabledText.Text = "Disabled";
        }

        ApplyBypassState(status.Bypassed);
        ToggleBypassButton.IsEnabled = true;

        var logs = await _apiClient.GetRecentLogsAsync();
        LogsListView.ItemsSource = logs;
    }

    /// <summary>อัพเดต Connection dot/text/ปุ่ม toggle ให้ตรงกับ bypass state ที่ยืนยันแล้ว (จาก GET /status หรือผลลัพธ์ toggle เอง)</summary>
    private void ApplyBypassState(bool bypassed)
    {
        _lastKnownBypassed = bypassed;

        if (bypassed)
        {
            ConnectionDot.Fill = (Brush)FindResource("AccentRedBrush");
            ConnectionText.Text = "Bypassed";
            ToggleBypassButton.Content = "RECONNECT";
        }
        else
        {
            ConnectionDot.Fill = (Brush)FindResource("AccentGreenBrush");
            ConnectionText.Text = "Connected";
            ToggleBypassButton.Content = "BYPASS";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDataAsync();
    }

    private void OpenTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        // ใช้หน้าต่างเดิมถ้ามีอยู่แล้ว - ตอนนี้กากบาทของ LogTerminalWindow = ซ่อน ไม่ใช่ปิด
        // ถ้ายัง new ใหม่ทุกครั้งเหมือนเดิม การกดปุ่มนี้ซ้ำๆ จะสะสมหน้าต่างที่ซ่อนอยู่ไปเรื่อยๆ
        var existing = Application.Current.Windows.OfType<LogTerminalWindow>().FirstOrDefault();

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

        var terminalWindow = new LogTerminalWindow(_apiClient);
        terminalWindow.Show();
    }

    /// <summary>
    /// สั่ง Agent ตัด/ต่อการเชื่อมต่อกับ Server (ดู ClientApiClient.SetBypassAsync/BypassCoordinator)
    /// ถ้าเปลี่ยนเป็น Bypassed สำเร็จ ต้องเก็บกวาด VNC/lock screen/physical-input-block ที่อาจ active
    /// อยู่ฝั่ง AgentTray ด้วย (resource พวกนี้อยู่ฝั่งนี้ล้วนๆ Agent เอง Session 0 แตะไม่ถึง) ถ้า Agent
    /// ปฏิเสธไม่ว่าด้วยเหตุผลไหน (คืน Bypassed: null) ไม่ถือว่าสำเร็จ - ไม่แตะ session ใดๆ และโชว์
    /// error ให้เห็นชัด
    ///
    /// WP2 (2026-10-30): เปิด bypass ตอนนี้ต้องมี staff session token ที่ขอไว้ตอน login (ดู
    /// ClientApiClient) ถ้าไม่มี/หมดอายุ Agent จะปฏิเสธ (AgentReachable=true แต่ Bypassed=null) ต่าง
    /// จากกรณี Agent Service ไม่ตอบเลย (AgentReachable=false) - แยกข้อความสองกรณีนี้ให้ครูเข้าใจถูก
    /// </summary>
    private async void ToggleBypassButton_Click(object sender, RoutedEventArgs e)
    {
        var requestedBypassed = !_lastKnownBypassed;

        ToggleBypassButton.IsEnabled = false;
        ConnectionText.Text = "กำลังเปลี่ยน...";

        var (confirmed, agentReachable) = await _apiClient.SetBypassAsync(requestedBypassed);

        if (confirmed is null)
        {
            ConnectionDot.Fill = (Brush)FindResource("AccentNeutralBrush");
            ConnectionText.Text = agentReachable
                ? "เปลี่ยนไม่สำเร็จ - ต้อง login ใหม่ก่อนใช้ Bypass (token ไม่มีหรือหมดอายุ)"
                : "เปลี่ยนไม่สำเร็จ - ติดต่อ Agent Service ไม่ได้";
            ToggleBypassButton.IsEnabled = true;
            return;
        }

        if (confirmed.Value)
        {
            ((App)System.Windows.Application.Current).CleanupInteractiveSessionsForBypass();
        }

        ApplyBypassState(confirmed.Value);
        ToggleBypassButton.IsEnabled = true;
    }
}
