using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01 / แก้ตามคำขอเพิ่ม 3 พ.ย. 2569 / redesign UI-9 4 พ.ย. 2569) - แถบแดงเตือน
/// ที่ค้างอยู่บนจอครูตลอดเวลาที่กำลังส่ง เป็นเจ้าของอายุของ ScreenCaptureService ตัวจริง (เริ่ม capture ตอน
/// Loaded, หยุดตอนกด "หยุดส่ง" หรือถูกปิดด้วยเหตุผลใดก็ตาม) และเป็นเจ้าของอายุของหน้าต่างควบคุมอื่นๆ ที่
/// เพิ่มมาตามคำขอ 3 พ.ย.: กรอบแดงรอบจอ (TeacherBroadcastBorderWindow), กล่องเครื่องมือเขียน+ซูม
/// (TeacherDrawCanvasWindow + TeacherDrawToolbarWindow ซึ่งเป็นเจ้าของ TeacherZoomMinimapWindow ต่ออีกชั้น)
///
/// ต่างจาก TeacherScreenViewWindow ฝั่งนักเรียน (ที่ปิดไม่ได้เลยจนกว่าครูสั่ง) - หน้าต่างนี้เป็นของครูเอง
/// ปิดได้ทุกทาง (ปุ่ม "หยุด" หรือ Alt-F4/ปิดโปรแกรม) เพราะถ้าเกิดอะไรผิดพลาดครูต้องหยุดส่งได้เสมอ
/// ไม่ถูกขังเหมือนจอนักเรียน - ทุกทางที่ปิดเดินผ่าน StopBroadcastingAsync เดียวกันเพื่อให้แน่ใจว่า
/// ส่ง StopScreenBroadcast ไปหาทุกเครื่องเป้าหมายเสมอ ไม่ปล่อยให้ Agent/AgentTray ค้างรอภาพที่ไม่มีอีกแล้ว
///
/// ตัวเองก็ต้อง SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) เหมือนกรอบแดง ทั้งสองสถานะ (เต็ม/หุบ)
/// เพราะเป็น HWND เดียวกันตลอด (สลับ Visibility ภายใน ไม่สร้างหน้าต่างใหม่)
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

    /// <summary>ความสูงคงที่ของแต่ละสถานะ (DIP) - UI-9: ต้องเป็นค่าคงที่ (ไม่ใช่ SizeToContent="WidthAndHeight" แบบเดิม) เพื่อให้ animate Window.Top ระหว่างสลับสถานะได้แบบลื่น (ดู AnimateTopTo)</summary>
    private const double CollapsedHeight = 44;
    private const double FullHeight = 56;

    /// <summary>Top ของแต่ละสถานะ (DIP) - ตอนหุบชิดขอบบนจอจริงๆ (ตัดสินแล้ว "เลื่อนขึ้นไปชิดขอบบนจอ") ตอนกางเว้นระยะจากขอบ 16px เหมือนเดิม</summary>
    private const double CollapsedTop = 0;
    private const double FullTop = 16;

    private const int SlideAnimationMs = 220;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ScreenBroadcastSession _session;
    private readonly ScreenCaptureService _captureService = new();
    private bool _stopped;
    private bool _isExpanded;

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
        Top = FullTop;
        Height = FullHeight;

        var roomPart = string.IsNullOrWhiteSpace(session.RoomName) ? string.Empty : $" · {session.RoomName}";
        var statusMessage = session.FailCount > 0
            ? $"กำลังส่งจอ{roomPart} · เริ่มสำเร็จ {session.SuccessCount}/{session.Targets.Count} เครื่อง"
            : $"กำลังส่งจอ{roomPart} · {session.SuccessCount} เครื่อง";
        StatusText.Text = statusMessage;
        CollapsedStatusText.Text = statusMessage;

        SourceInitialized += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine($"[broadcast-warning-bar] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");

            // หน้าต่างทึบ (ไม่ใช้ AllowsTransparency เพราะทำให้ exclude ล้ม) มุมโค้งจึงมาจาก window region
            // (ดู TeacherOnlyWindowHelper.ApplyRoundedRegion) - ตอนหุบมุมโค้งแค่ล่าง (ดู CornerRadius ใน .xaml
            // ของ CollapsedBar) แต่ window region เป็นสี่เหลี่ยมมุมโค้งทุกมุมเหมือนกันทั้งคู่ (ความต่างเล็กน้อย
            // ที่มุมบนของแถบหุบมองไม่เห็นอยู่แล้วเพราะชิดขอบจอพอดี)
            SizeChanged += (_, _) =>
            {
                TeacherOnlyWindowHelper.ApplyRoundedRegion(this, _isExpanded ? 10 : 10);
                RecenterHorizontally();
            };

            _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
            _hwndSource.AddHook(WndProc);
            RegisterHotKey(_hwndSource.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT, VK_D);
        };

        Loaded += (_, _) =>
        {
            // เริ่มที่สถานะเต็ม (เหมือนพฤติกรรมเดิมก่อนมีปุ่มหุบ) จัดกึ่งกลางด้านบนจอหลัก
            SetExpandedImmediate(!PreviewStartCollapsed);

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

    // ==================== UI-9 (2026-11-04): หุบ/กาง + สไลด์ ====================

    /// <summary>จัดกลางแนวนอนตามความกว้างปัจจุบันของเนื้อหา (เรียกทุกครั้งที่ขนาดเปลี่ยนเพราะข้อความห้อง/จำนวนเครื่องยาวไม่เท่ากันในแต่ละครั้งที่ส่ง)</summary>
    private void RecenterHorizontally()
    {
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
    }

    /// <summary>ตั้งสถานะแรกเริ่มโดยไม่มี animation (ตอน Loaded ยังไม่มีอะไรให้สไลด์จาก)</summary>
    private void SetExpandedImmediate(bool expanded)
    {
        _isExpanded = expanded;
        CollapsedBar.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        FullBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Height = expanded ? FullHeight : CollapsedHeight;
        Top = expanded ? FullTop : CollapsedTop;
    }

    /// <summary>
    /// สลับสถานะหุบ/กาง พร้อม animation เลื่อนขึ้น/ลง (ตัดสินแล้วใน roadmap UI-9: "มี animation เลื่อนขึ้น/ลง")
    /// สลับเนื้อหา (Visibility/Height) ก่อนเริ่ม animate แล้วค่อยเลื่อน Top จากตำแหน่งเดิมไปตำแหน่งใหม่ - ทำให้
    /// เห็นแถบ "โต/หด" ที่ตำแหน่งเดิมก่อน แล้วไหลขึ้น/ลงไปตำแหน่งสุดท้าย
    /// </summary>
    private void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded)
        {
            return;
        }

        var fromTop = Top;
        _isExpanded = expanded;

        CollapsedBar.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        FullBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Height = expanded ? FullHeight : CollapsedHeight;

        RecenterHorizontally();

        var toTop = expanded ? FullTop : CollapsedTop;
        var animation = new DoubleAnimation(fromTop, toTop, TimeSpan.FromMilliseconds(SlideAnimationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, animation);
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e) => SetExpanded(true);

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => SetExpanded(false);

    // ==================== UI-9 (2026-11-04): กล่องเครื่องมือเขียน+ซูม (รวมเป็นที่เดียว) ====================

    private void ToolboxButton_Click(object sender, RoutedEventArgs e) => ToggleToolbox();

    /// <summary>Ctrl+Shift+D ("ยังสลับโหมด" ตามที่ตัดสินไว้) - เปิด/ปิดกล่องเครื่องมือทั้งชุดเหมือนปุ่ม square-pen บนแถบ</summary>
    private void ToggleDrawMode() => ToggleToolbox();

    private void ToggleToolbox()
    {
        if (_drawCanvasWindow is null)
        {
            _drawCanvasWindow = new TeacherDrawCanvasWindow();
            _drawCanvasWindow.Show();

            _drawToolbarWindow = new TeacherDrawToolbarWindow(_drawCanvasWindow, _captureService);
            _drawToolbarWindow.Show();

            ToolboxButton.IsChecked = true;
            return;
        }

        // กล่องเครื่องมือเปิดอยู่แล้ว - ปิดทั้งชุด (แผ่นใสเขียน + แผงเครื่องมือ + แผนที่ซูมถ้าเปิดอยู่)
        _drawToolbarWindow?.ForceClose();
        _drawToolbarWindow = null;
        _drawCanvasWindow.ForceClose();
        _drawCanvasWindow = null;
        ToolboxButton.IsChecked = false;
    }

    /// <summary>
    /// ปุ่ม "ขยาย" (maximize-2) บนแถบตอนกาง - ต่างจากปุ่มขยายตอนหุบ (ซึ่งกางแถบคืนจากหุบ) เพราะแถบกางอยู่แล้ว
    /// จึงใช้ทำหน้าที่เรียกกล่องเครื่องมือ/แผนที่ซูมที่อาจถูกหน้าต่างอื่นบังอยู่คืนมาข้างหน้า (roadmap UI-9
    /// ไม่ได้ระบุไว้ชัดว่าปุ่มนี้ในสถานะกางควรทำอะไร - นี่เป็นการตัดสินใจเองที่ควรให้ผู้ใช้ตรวจอีกรอบ)
    /// </summary>
    private void RestoreToolsButton_Click(object sender, RoutedEventArgs e)
    {
        _drawToolbarWindow?.Activate();
        _drawCanvasWindow?.Activate();
        _borderWindow?.Activate();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopBroadcastingAsync();
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // ปิดด้วยเหตุผลอื่น (Alt-F4/ปิด Console) - ยังต้องเก็บกวาดเหมือนกด "หยุด" เสมอ (ดู doc comment
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
