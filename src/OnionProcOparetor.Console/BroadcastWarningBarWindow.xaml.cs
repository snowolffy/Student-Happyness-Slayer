using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01 / แก้ตามคำขอเพิ่ม 3 พ.ย. 2569) - แถบแดงเตือนที่ค้างอยู่บนจอครูตลอดเวลาที่
/// กำลังส่ง เป็นเจ้าของอายุของ ScreenCaptureService ตัวจริง (เริ่ม capture ตอน Loaded, หยุดตอนกด "หยุดส่ง"
/// หรือถูกปิดด้วยเหตุผลใดก็ตาม) และเป็นเจ้าของอายุของหน้าต่างควบคุมอื่นๆ ที่เพิ่มมาตามคำขอ 3 พ.ย.: กรอบแดง
/// รอบจอ (TeacherBroadcastBorderWindow), แผ่นใสเขียนบนจอ (TeacherDrawCanvasWindow + TeacherDrawToolbarWindow)
///
/// ต่างจาก TeacherScreenViewWindow ฝั่งนักเรียน (ที่ปิดไม่ได้เลยจนกว่าครูสั่ง) - หน้าต่างนี้เป็นของครูเอง
/// ปิดได้ทุกทาง (ปุ่ม "หยุดส่ง" หรือ Alt-F4/ปิดโปรแกรม) เพราะถ้าเกิดอะไรผิดพลาดครูต้องหยุดส่งได้เสมอ
/// ไม่ถูกขังเหมือนจอนักเรียน - ทุกทางที่ปิดเดินผ่าน StopBroadcastingAsync เดียวกันเพื่อให้แน่ใจว่า
/// ส่ง StopScreenBroadcast ไปหาทุกเครื่องเป้าหมายเสมอ ไม่ปล่อยให้ Agent/AgentTray ค้างรอภาพที่ไม่มีอีกแล้ว
///
/// ตัวเองก็ต้อง SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) เหมือนกรอบแดง (ตัดสินแล้ว: "ใช้
/// SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) กับหน้าต่างกรอบและแถบ") ทั้งสองสถานะ (เต็ม/หุบ) เพราะ
/// เป็น HWND เดียวกันตลอด (สลับ Visibility ภายใน ไม่สร้างหน้าต่างใหม่)
/// </summary>
public partial class BroadcastWarningBarWindow : Window
{
    /// <summary>fps เป้าหมาย - อยู่ในช่วง 5-10 ตาม roadmap ("ลดเฟรมเหลือ 5-10 fps") เลือกจุดกลางบนสุดเพื่อภาพลื่นขึ้นโดยไม่เปลืองแบนด์วิดท์เกินไป (ดูตัวเลขจริงที่วัดได้ใน WP4-4 report)</summary>
    private const int TargetFps = 8;

    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_D = 0x44;
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xBF44; // ค่าเฉพาะโปรแกรมนี้ กันชนกับ hotkey อื่นที่ Console อาจลงทะเบียนในอนาคต

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ScreenBroadcastSession _session;
    private readonly ScreenCaptureService _captureService = new();
    private bool _stopped;

    /// <summary>ใช้เฉพาะโหมดถ่ายภาพ UI (--dialog=broadcast-warning-bar-collapsed) - เริ่มที่สถานะหุบแทนแถบเต็ม</summary>
    public bool PreviewStartCollapsed { get; set; }

    private TeacherBroadcastBorderWindow? _borderWindow;
    private TeacherDrawCanvasWindow? _drawCanvasWindow;
    private TeacherDrawToolbarWindow? _drawToolbarWindow;
    private HwndSource? _hwndSource;

    public BroadcastWarningBarWindow(ApiClient apiClient, LabHubClient labHubClient, ScreenBroadcastSession session)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _session = session;

        Left = 16;
        Top = 16;

        StatusText.Text = session.FailCount > 0
            ? $"กำลังส่งจอ · เริ่มสำเร็จ {session.SuccessCount}/{session.Targets.Count} เครื่อง"
            : $"กำลังส่งจอ · {session.SuccessCount} เครื่อง";

        SourceInitialized += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine($"[broadcast-warning-bar] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");

            _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
            _hwndSource.AddHook(WndProc);
            RegisterHotKey(_hwndSource.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT, VK_D);
        };

        Loaded += (_, _) =>
        {
            // เริ่มที่สถานะเต็ม (เหมือนพฤติกรรมเดิมก่อนมีปุ่มหุบ) - จัดกึ่งกลางด้านบนจอหลัก ต้องรอ Loaded
            // เพราะ SizeToContent ทำให้ ActualWidth รู้ค่าจริงก็ตอนนี้เท่านั้น (ก่อนหน้านี้เป็น 0 เสมอ)
            SetExpanded(!PreviewStartCollapsed);

            _borderWindow = new TeacherBroadcastBorderWindow();
            _borderWindow.Show();

            _captureService.FrameCaptured += OnFrameCaptured;
            _captureService.Start(TargetFps);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleDrawMode();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>เรียกจาก timer thread ของ ScreenCaptureService (ไม่ใช่ UI thread) - SignalR InvokeAsync เรียกข้ามเทรดได้ปลอดภัยอยู่แล้ว (HubConnection เป็น thread-safe ตาม SignalR docs) ไม่ต้อง Dispatcher</summary>
    private void OnFrameCaptured(byte[] jpegBytes)
    {
        _ = _labHubClient.SendScreenFrameAsync(jpegBytes);
    }

    // ==================== คำขอเพิ่ม 3 พ.ย. 2569: หุบ/กาง ====================

    /// <summary>
    /// กดปุ่มกลม - ต้องรองรับทั้ง "ลากย้ายตำแหน่ง" และ "กดครั้งเดียวกางกลับเป็นแถบเต็ม" (ตัดสินแล้วทั้งคู่
    /// 3 พ.ย.) DragMove() ของ WPF บล็อกจนกว่าจะปล่อยปุ่มเมาส์ - เทียบตำแหน่งหน้าต่างก่อน/หลัง DragMove
    /// คืนค่า เพื่อแยกว่า "ลากจริง" (ขยับเกิน threshold) หรือ "กดเฉยๆไม่ขยับ" (= ตั้งใจกาง ไม่ใช่ลาก)
    /// </summary>
    private void CollapsedPill_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var startLeft = Left;
        var startTop = Top;

        DragMove();

        var moved = Math.Abs(Left - startLeft) > 3 || Math.Abs(Top - startTop) > 3;
        if (!moved)
        {
            SetExpanded(true);
        }
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    /// <summary>กดปุ่มกลมครั้งเดียว (ไม่ลาก) ก็กางกลับเป็นแถบเต็ม - ตัดสินแล้ว "กดแล้วกางกลับเป็นแถบเต็ม" (ไม่บังคับต้อง double-click)</summary>
    private void SetExpanded(bool expanded)
    {
        CollapsedPill.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        FullBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        if (expanded && !_hasPositionedOnce)
        {
            _hasPositionedOnce = true;
            Left = (SystemParameters.PrimaryScreenWidth - 420) / 2; // ประมาณความกว้างแถบเต็ม - ปรับเข้าที่จริงหลัง layout รอบแรก
            Top = 16;
        }
    }

    private bool _hasPositionedOnce;

    // ==================== คำขอเพิ่ม 3 พ.ย. 2569: เครื่องมือเขียน ====================

    private void DrawToolButton_Click(object sender, RoutedEventArgs e) => ToggleDrawMode();

    private void ToggleDrawMode()
    {
        if (_drawCanvasWindow is null)
        {
            _drawCanvasWindow = new TeacherDrawCanvasWindow();
            _drawCanvasWindow.Show();

            _drawToolbarWindow = new TeacherDrawToolbarWindow(_drawCanvasWindow);
            _drawToolbarWindow.Show();
        }

        var newDrawMode = !_drawCanvasWindow.IsDrawMode;
        _drawCanvasWindow.SetDrawMode(newDrawMode);
        DrawToolButton.IsChecked = newDrawMode;
    }

    // ==================== คำขอเพิ่ม 3 พ.ย. 2569: Zoom แว่นขยาย ====================

    private void ZoomButton_Click(object sender, RoutedEventArgs e)
    {
        _captureService.ZoomEnabled = ZoomButton.IsChecked == true;
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

        if (_hwndSource is not null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(WndProc);
        }

        _captureService.FrameCaptured -= OnFrameCaptured;
        _captureService.Stop();
        _captureService.Dispose();

        _borderWindow?.ForceClose();
        _borderWindow = null;
        _drawToolbarWindow?.ForceClose();
        _drawToolbarWindow = null;
        _drawCanvasWindow?.ForceClose();
        _drawCanvasWindow = null;

        foreach (var client in _session.Targets)
        {
            await _apiClient.SendCommandAsync(client.ClientGuid, "StopScreenBroadcast", null);
        }
    }
}
