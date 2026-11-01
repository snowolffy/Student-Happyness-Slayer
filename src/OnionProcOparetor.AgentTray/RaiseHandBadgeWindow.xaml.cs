using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>ป้ายมุมขวาล่าง "ยกมืออยู่ · คิวที่ N" (WP4-2, 2026-11-01) - ดู doc comment ใน .xaml</summary>
public partial class RaiseHandBadgeWindow : Window
{
    private const double ScreenMargin = 16;

    private readonly ClientApiClient _apiClient;
    private readonly DispatcherTimer _pollTimer;
    private bool _allowClose;

    /// <summary>ยิงตอนกด "ยกเลิก" แล้ว Agent ยืนยันยกเลิกสำเร็จจริง - App.xaml.cs ฟังไว้ปิดหน้าต่างนี้</summary>
    public event Action? Cancelled;

    public RaiseHandBadgeWindow(ClientApiClient apiClient, int? queuePosition)
    {
        InitializeComponent();
        _apiClient = apiClient;
        SetQueuePositionText(queuePosition);

        Loaded += (_, _) => MoveToBottomRight();

        // แก้ตาม feedback ก่อน merge (2026-11-01): เดิมป้ายนี้ปิดได้ทางเดียวคือกด "ยกเลิก" เอง ไม่รู้เลย
        // ว่าครูกด "ไปช่วยแล้ว"/ล้างคิวจาก Console ไปแล้ว ค้างอยู่ตลอด - แก้โดย poll Agent /status เองทุก 5
        // วิ (pattern เดียวกับ StudentLoginWindow.RefreshEmergencyAndConnectivityStateAsync) ไม่ใช่ช่องทาง
        // push ใหม่ - Server แนบ raisedHand/raiseHandQueuePosition ไปกับ poll ปกติของ Worker อยู่แล้ว (ดู
        // RaiseHandQueue.GetStatusForClient ฝั่ง Server) Agent cache ไว้ใน ClientState แล้วแจกต่อที่ /status
        // ป้ายนี้แค่มาถามถี่กว่ารอบ poll ของ Worker (30 วิ) เพื่อปิดตัวเองได้เร็วขึ้น
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) => await RefreshFromAgentStatusAsync();
        _pollTimer.Start();
    }

    /// <summary>
    /// ถาม Agent local API ว่าเครื่องนี้ยังอยู่ในคิวยกมืออยู่ไหม - คืนเงียบๆ ถ้า Agent ไม่ตอบ (เหตุผล
    /// เดียวกับ StudentLoginWindow: ไม่อยากให้ป้ายกระพริบหาย/ปิดผิดจังหวะเพราะ Agent local API สะดุดชั่วคราว
    /// ไม่ใช่เพราะครูกด "ไปช่วยแล้ว" จริงๆ) ปิดป้ายเองเมื่อ RaisedHand กลายเป็น false เท่านั้น
    /// </summary>
    private async Task RefreshFromAgentStatusAsync()
    {
        var status = await _apiClient.GetStatusAsync();
        if (status is null)
        {
            return;
        }

        if (!status.RaisedHand)
        {
            // ครูกด "ไปช่วยแล้ว"/ล้างคิวจาก Console ไปแล้ว - ไม่ต้องเรียก CancelRaiseHandAsync ซ้ำ
            // (ฝั่ง Server ปิดคิวไปเรียบร้อยแล้ว การเรียกซ้ำจะได้แค่ success=false เฉยๆ ไม่มีผลเสียแต่ไม่จำเป็น)
            ForceClose();
            return;
        }

        SetQueuePositionText(status.RaiseHandQueuePosition);
    }

    private void SetQueuePositionText(int? queuePosition) =>
        InfoText.Text = queuePosition is { } position ? $"คิวที่ {position}" : "กำลังรอครู";

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

    /// <summary>เรียกจาก App.xaml.cs เท่านั้น (กด "ยกเลิก" เอง, poll เจอว่าครูปิดคิวให้แล้ว, หรือ cleanup ตอนปิด Tray/logout นักเรียน) - ทางเดียวที่ปิดหน้าต่างนี้ได้จริง (ดู OnClosing)</summary>
    public void ForceClose()
    {
        _allowClose = true;
        _pollTimer.Stop();
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
