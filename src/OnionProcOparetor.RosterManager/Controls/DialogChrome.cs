using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OnionProcOparetor.RosterManager.Controls;

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
