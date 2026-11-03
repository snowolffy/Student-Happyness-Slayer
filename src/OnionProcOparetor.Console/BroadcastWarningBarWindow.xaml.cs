using System.ComponentModel;
using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01) - แถบแดงเตือนที่ค้างอยู่บนจอครูตลอดเวลาที่กำลังส่ง เป็นเจ้าของอายุของ
/// ScreenCaptureService ตัวจริง (เริ่ม capture ตอน Loaded, หยุดตอนกด "หยุดส่ง" หรือถูกปิดด้วยเหตุผลใดก็ตาม)
///
/// ต่างจาก TeacherScreenViewWindow ฝั่งนักเรียน (ที่ปิดไม่ได้เลยจนกว่าครูสั่ง) - หน้าต่างนี้เป็นของครูเอง
/// ปิดได้ทุกทาง (ปุ่ม "หยุดส่ง" หรือ Alt-F4/ปิดโปรแกรม) เพราะถ้าเกิดอะไรผิดพลาดครูต้องหยุดส่งได้เสมอ
/// ไม่ถูกขังเหมือนจอนักเรียน - ทุกทางที่ปิดเดินผ่าน StopBroadcastingAsync เดียวกันเพื่อให้แน่ใจว่า
/// ส่ง StopScreenBroadcast ไปหาทุกเครื่องเป้าหมายเสมอ ไม่ปล่อยให้ Agent/AgentTray ค้างรอภาพที่ไม่มีอีกแล้ว
/// </summary>
public partial class BroadcastWarningBarWindow : Window
{
    /// <summary>fps เป้าหมาย - อยู่ในช่วง 5-10 ตาม roadmap ("ลดเฟรมเหลือ 5-10 fps") เลือกจุดกลางบนสุดเพื่อภาพลื่นขึ้นโดยไม่เปลืองแบนด์วิดท์เกินไป (ดูตัวเลขจริงที่วัดได้ใน WP4-4 report)</summary>
    private const int TargetFps = 8;

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ScreenBroadcastSession _session;
    private readonly ScreenCaptureService _captureService = new();
    private bool _stopped;

    public BroadcastWarningBarWindow(ApiClient apiClient, LabHubClient labHubClient, ScreenBroadcastSession session)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _session = session;

        StatusText.Text = session.FailCount > 0
            ? $"กำลังส่งจอ · เริ่มสำเร็จ {session.SuccessCount}/{session.Targets.Count} เครื่อง"
            : $"กำลังส่งจอ · {session.SuccessCount} เครื่อง";

        Loaded += (_, _) =>
        {
            // จัดกึ่งกลางด้านบนจอหลัก - ต้องรอ Loaded เพราะ SizeToContent ทำให้ ActualWidth รู้ค่าจริง
            // ก็ตอนนี้เท่านั้น (ก่อนหน้านี้เป็น 0 เสมอ)
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 16;

            _captureService.FrameCaptured += OnFrameCaptured;
            _captureService.Start(TargetFps);
        };
    }

    /// <summary>เรียกจาก timer thread ของ ScreenCaptureService (ไม่ใช่ UI thread) - SignalR InvokeAsync เรียกข้าม thread ได้ปลอดภัยอยู่แล้ว (HubConnection เป็น thread-safe ตาม SignalR docs) ไม่ต้อง Dispatcher</summary>
    private void OnFrameCaptured(byte[] jpegBytes)
    {
        _ = _labHubClient.SendScreenFrameAsync(jpegBytes);
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopBroadcastingAsync();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // ปิดด้วยเหตุผลอื่น (Alt-F4/ปิด Console) - ยังต้องเก็บกวาดเหมือนกด "หยุดส่ง" เสมอ (ดู doc comment
        // บนคลาส) fire-and-forget เพราะ OnClosing เป็น synchronous ไม่รอ async ได้ - ยอมรับได้ที่นี่
        // เพราะ StopScreenBroadcast เป็น best-effort อยู่แล้ว (ไม่ critical ถ้าช้าไปนิดหน่อย)
        if (!_stopped)
        {
            _ = StopBroadcastingAsync();
        }
    }

    private async Task StopBroadcastingAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _captureService.FrameCaptured -= OnFrameCaptured;
        _captureService.Stop();
        _captureService.Dispose();

        foreach (var client in _session.Targets)
        {
            await _apiClient.SendCommandAsync(client.ClientGuid, "StopScreenBroadcast", null);
        }
    }
}
