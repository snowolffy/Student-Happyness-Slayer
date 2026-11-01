using System.ComponentModel;
using System.Windows;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>ป้ายมุมขวาล่าง "ยกมืออยู่ · คิวที่ N" (WP4-2, 2026-11-01) - ดู doc comment ใน .xaml สำหรับ known limitation</summary>
public partial class RaiseHandBadgeWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly ClientApiClient _apiClient;
    private bool _allowClose;

    /// <summary>ยิงตอนกด "ยกเลิก" แล้ว Agent ยืนยันยกเลิกสำเร็จจริง - App.xaml.cs ฟังไว้ปิดหน้าต่างนี้</summary>
    public event Action? Cancelled;

    public RaiseHandBadgeWindow(ClientApiClient apiClient, int? queuePosition)
    {
        InitializeComponent();
        _apiClient = apiClient;
        InfoText.Text = queuePosition is { } position ? $"คิวที่ {position}" : "กำลังรอครู";

        Loaded += (_, _) => MoveToBottomRight();
    }

    private void MoveToBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    private async void CancelRaiseHandButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRaiseHandButton.IsEnabled = false;
        await _apiClient.CancelRaiseHandAsync();
        ForceClose();
        Cancelled?.Invoke();
    }

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (กด "ยกเลิก" เอง หรือ cleanup ตอนปิด Tray/logout นักเรียน) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง (ดู OnClosing)</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
