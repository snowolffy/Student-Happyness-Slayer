using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// ป้ายมุมขวาล่าง "Staff session · ครู · เวลาที่เหลือ · [จบ]" (WP3-3, 2026-10-31) - ดู doc comment ใน .xaml
///
/// นับถอยหลังด้วยนาฬิกาตัวเอง (เหมือน ExamModeBadgeWindow) จาก expiresAtUtc ที่ ClientApiClient ขอมา
/// ตอน login (ClientApiClient.StaffSessionExpiresAtUtc) - เป็นแค่ "ตัวเลขที่โชว์" เท่านั้น ไม่ใช่ตัวตัดสิน
/// ว่า bypass หมดอายุจริงไหม (Agent/BypassCoordinator เป็นคนตัดสินจริงฝั่งนั้น - ScheduleAutoRevert)
/// </summary>
public partial class StaffSessionBadgeWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly ClientApiClient _apiClient;
    private readonly DispatcherTimer _tickTimer;
    private readonly DateTime? _expiresAtUtc;
    private bool _allowClose;

    /// <summary>ยิงตอนกด "จบ" แล้ว bypass ปิดสำเร็จจริง - App.xaml.cs ฟังไว้เปิดจอล็อกอินนักเรียนกลับมา (ถ้า requireLogin ยังเปิดอยู่)</summary>
    public event Action? SessionEnded;

    public StaffSessionBadgeWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _expiresAtUtc = apiClient.StaffSessionExpiresAtUtc;

        RefreshInfoText();
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _tickTimer.Tick += (_, _) => RefreshInfoText();
        _tickTimer.Start();

        Loaded += (_, _) => MoveToBottomRight();
    }

    private void RefreshInfoText()
    {
        var username = _apiClient.StaffSessionUsername ?? "ครู";
        if (_expiresAtUtc is { } expiry)
        {
            var remaining = expiry - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                InfoText.Text = $"{username} · หมดเวลาแล้ว";
                return;
            }

            var minutes = Math.Max(0, (int)Math.Floor(remaining.TotalMinutes));
            var seconds = Math.Max(0, remaining.Seconds);
            InfoText.Text = $"{username} · {minutes}:{seconds:D2}";
        }
        else
        {
            InfoText.Text = username;
        }

        if (IsLoaded)
        {
            MoveToBottomRight();
        }
    }

    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    private async void EndButton_Click(object sender, RoutedEventArgs e)
    {
        EndButton.IsEnabled = false;
        await _apiClient.SetBypassAsync(false);
        _apiClient.ClearStaffSessionInfo();
        ForceClose();
        SessionEnded?.Invoke();
    }

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (กด "จบ" เอง หรือ Exit Tray/cleanup ตอน bypass ถูกปิดจากที่อื่น เช่น หมดอายุอัตโนมัติฝั่ง Agent) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง</summary>
    public void ForceClose()
    {
        _allowClose = true;
        _tickTimer.Stop();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // "ซ่อนไม่ได้" ตาม wireframe - ต้องกด "จบ" จริง (ดู doc comment บน .xaml)
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
