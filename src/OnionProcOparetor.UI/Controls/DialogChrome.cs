using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace OnionProcOparetor.UI.Controls;

/// <summary>
/// ตัวช่วยเล็ก ๆ ของ AppDialogWindow (แถบหัวหน้าต่างที่เราวาดเอง)
///
/// ทำไมต้องวาดแถบหัวเอง ไม่ใช่แค่เรื่องความสวย: บน Windows 10 build นี้ แถบหัวหน้าต่างของ Windows
/// **ไม่ยอมเป็นสีเข้ม** แม้จะตั้ง DWMWA_USE_IMMERSIVE_DARK_MODE สำเร็จแล้วก็ตาม (DwmGetWindowAttribute
/// อ่านกลับมาได้ค่า 1 แต่ยังวาดขาวอยู่ - ของนี้เริ่มทำงานจริงบน Windows 11) ผลคือโหมดมืดจะมีแถบขาว
/// พาดหัวทุกหน้าต่างซึ่งดูเหมือนของเสีย พอวาดเองก็จบปัญหา และได้หน้าตาตรงกับ ModalShell ของ
/// OnlineConsole ไปด้วย
/// </summary>
public static class DialogChrome
{
    /// <summary>
    /// ติดกับปุ่มปิดในแถบหัว - กดแล้วปิดหน้าต่างที่ปุ่มนั้นอยู่
    ///
    /// ที่ต้องเป็น attached property ไม่ใช่ Click handler ธรรมดา: ปุ่มนี้อยู่ใน ControlTemplate ที่
    /// ใช้ร่วมกันทุกหน้าต่าง จึงไม่มี code-behind ตัวไหนเป็นเจ้าของมันโดยเฉพาะ และการใช้
    /// SystemCommands.CloseWindowCommand ก็ไม่ช่วย เพราะ RoutedCommand ตัวนั้นไม่มี CommandBinding
    /// ติดมาให้เมื่อหน้าต่างใช้ Template ของตัวเอง (ปกติ template มาตรฐานของ WPF เป็นคนผูกให้)
    /// </summary>
    public static readonly DependencyProperty IsCloseButtonProperty = DependencyProperty.RegisterAttached(
        "IsCloseButton", typeof(bool), typeof(DialogChrome),
        new PropertyMetadata(false, OnIsCloseButtonChanged));

    public static bool GetIsCloseButton(DependencyObject d) => (bool)d.GetValue(IsCloseButtonProperty);
    public static void SetIsCloseButton(DependencyObject d, bool value) => d.SetValue(IsCloseButtonProperty, value);

    private static void OnIsCloseButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        button.Click -= CloseOwnerWindow;
        if (e.NewValue is true)
        {
            button.Click += CloseOwnerWindow;
        }
    }

    private static void CloseOwnerWindow(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject start)
        {
            FindWindow(start)?.Close();
        }
    }

    /// <summary>ปุ่มย่อหน้าต่าง - มีเฉพาะหน้าต่างที่ ResizeMode อนุญาต (ดู trigger ใน AppDialogWindow)</summary>
    public static readonly DependencyProperty IsMinimizeButtonProperty = DependencyProperty.RegisterAttached(
        "IsMinimizeButton", typeof(bool), typeof(DialogChrome),
        new PropertyMetadata(false, OnIsMinimizeButtonChanged));

    public static bool GetIsMinimizeButton(DependencyObject d) => (bool)d.GetValue(IsMinimizeButtonProperty);
    public static void SetIsMinimizeButton(DependencyObject d, bool value) => d.SetValue(IsMinimizeButtonProperty, value);

    private static void OnIsMinimizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        button.Click -= MinimizeOwnerWindow;
        if (e.NewValue is true)
        {
            button.Click += MinimizeOwnerWindow;
        }
    }

    private static void MinimizeOwnerWindow(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject start && FindWindow(start) is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    /// <summary>ปุ่มขยาย/คืนขนาด - สลับไปมาระหว่าง Maximized กับ Normal</summary>
    public static readonly DependencyProperty IsMaximizeButtonProperty = DependencyProperty.RegisterAttached(
        "IsMaximizeButton", typeof(bool), typeof(DialogChrome),
        new PropertyMetadata(false, OnIsMaximizeButtonChanged));

    public static bool GetIsMaximizeButton(DependencyObject d) => (bool)d.GetValue(IsMaximizeButtonProperty);
    public static void SetIsMaximizeButton(DependencyObject d, bool value) => d.SetValue(IsMaximizeButtonProperty, value);

    private static void OnIsMaximizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        button.Click -= ToggleOwnerWindowMaximized;
        if (e.NewValue is true)
        {
            button.Click += ToggleOwnerWindowMaximized;
        }
    }

    private static void ToggleOwnerWindowMaximized(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject start && FindWindow(start) is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    // ==================== UI-12: กันแถบหัวของ Windows โผล่ทับแถบหัวที่เราวาดเอง ====================

    /// <summary>
    /// ตั้งใน style AppDialogWindow - ติดตั้ง hook ซ่อมอาการ "ปุ่ม X คลาสสิกของ Windows + เส้นใต้แถบหัวโผล่ทับแถบหัวเรา"
    /// (เจอเป็นครั้งคราวบน Console ภาพจากผู้ใช้: เห็นแค่ X มุมขวาบนกับเส้นหนึ่งเส้น พอ hover/ย่อขยายก็หายไปเอง)
    ///
    /// สาเหตุที่เชื่อว่าใช่ (อ่านจากกลไก ไม่ได้จำลองซ้ำได้จริง): หน้าต่างนี้ยังมี WS_CAPTION (WindowStyle=SingleBorderWindow เพื่อให้ snap/เงา/ย่อ-ขยาย
    /// ทำงาน) โดย WindowChrome ตัดพื้นที่ non-client ออกด้วย WM_NCCALCSIZE ทำให้ client = ทั้งหน้าต่าง ถ้ามีการวาด non-client ของ
    /// Windows เองหลุดมาจังหวะไหนก็ตาม (WM_NCPAINT / WM_NCACTIVATE / WM_SETTEXT / WM_SETICON / เปลี่ยนสไตล์เพราะสลับ ResizeMode
    /// ตอน login->dashboard) มันจะวาดแถบหัวคลาสสิกลงบนพื้นที่ที่ตอนนี้เป็น client - และ WPF จะไม่วาดทับให้จนกว่าบริเวณนั้นจะถูก
    /// invalidate (hover บนปุ่ม/resize) ซึ่งตรงกับอาการที่เห็น
    ///
    /// วิธีแก้ (ไม่แตะ WindowChrome จึงยังคง snap/resize/maximize/taskbar): (1) กลืน WM_NCPAINT - พื้นที่ non-client ของเราเป็นศูนย์
    /// ไม่มีอะไรต้องวาด (2) หลังข้อความที่ทำให้ Windows วาดแถบหัวได้ ให้ invalidate พื้นที่ client ทั้งหมดเพื่อให้ WPF วาดทับ
    /// (รวมเป็นรอบเดียวต่อช่วง ด้วย Dispatcher) - ทดสอบด้วยการวาดแถบหัวปลอมลง window DC จากภายนอกแล้วดูว่าหายหลังส่งข้อความเหล่านี้
    /// </summary>
    public static readonly DependencyProperty RepairSystemCaptionPaintProperty = DependencyProperty.RegisterAttached(
        "RepairSystemCaptionPaint", typeof(bool), typeof(DialogChrome),
        new PropertyMetadata(false, OnRepairSystemCaptionPaintChanged));

    public static bool GetRepairSystemCaptionPaint(DependencyObject d) => (bool)d.GetValue(RepairSystemCaptionPaintProperty);
    public static void SetRepairSystemCaptionPaint(DependencyObject d, bool value) => d.SetValue(RepairSystemCaptionPaintProperty, value);

    private static void OnRepairSystemCaptionPaintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            AttachRepairHook(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => AttachRepairHook(window);
        }
    }

    private const int WM_SETTEXT = 0x000C;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_SETICON = 0x0080;
    private const int WM_STYLECHANGED = 0x007D;
    private const int WM_THEMECHANGED = 0x031A;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ALLCHILDREN = 0x0080;

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    private static void AttachRepairHook(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            return;
        }

        var repaintQueued = false;

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCPAINT:
                    handled = true; // ไม่มีพื้นที่ non-client ให้วาดอยู่แล้ว (WindowChrome คืน client = ทั้งหน้าต่าง)
                    return IntPtr.Zero;

                case WM_NCACTIVATE:
                case WM_SETTEXT:
                case WM_SETICON:
                case WM_STYLECHANGED:
                case WM_THEMECHANGED:
                case WM_DWMCOMPOSITIONCHANGED:
                    if (!repaintQueued)
                    {
                        repaintQueued = true;
                        // รอให้ข้อความนั้นประมวลผลเสร็จ (Windows อาจวาดแถบหัวหลังจากนี้) แล้วค่อย invalidate ให้ WPF วาดทับ
                        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                        {
                            repaintQueued = false;
                            if (source.Handle != IntPtr.Zero)
                            {
                                RedrawWindow(source.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN);
                            }
                        });
                    }

                    break;
            }

            return IntPtr.Zero;
        }

        source.AddHook(Hook);
    }

    /// <summary>
    /// เดินขึ้น visual tree หา Window - ใช้ Window.GetWindow ไม่ได้ตรง ๆ ในทุกกรณีเพราะปุ่มอยู่ใน
    /// template ของหน้าต่างเอง แต่ VisualTreeHelper เดินผ่าน template ได้ปกติ
    /// </summary>
    private static Window? FindWindow(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Window window)
            {
                return window;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
