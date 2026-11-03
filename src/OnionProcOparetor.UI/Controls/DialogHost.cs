using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OnionProcOparetor.UI.Controls;

/// <summary>
/// UI-3a (2026-11-03): โฮสต์ dialog ลอยในหน้าต่างหลัก (MainWindow/Dashboard) ตามที่ roadmap WP UI-3a
/// สั่ง - แทนที่การเปิด <c>Window</c> ลูกใหม่ทุกครั้ง (ConfirmWindow/ForceChangePasswordWindow เดิม)
///
/// วิธีใช้ (ดูตัวอย่างจริงใน ConfirmDialogContent/ForceChangePasswordWindow ของ Console):
/// 1. วาง &lt;controls:DialogHost x:Name="..."/&gt; เป็นลูกตัวสุดท้ายของ Grid รากของ Window (ให้ทับ
///    ทุกอย่างอื่นเสมอ - ลำดับการประกาศใน XAML คือลำดับ z-order ของ WPF)
/// 2. เนื้อหาของ dialog เป็น FrameworkElement ธรรมดา (มักเป็น UserControl ที่มี Border Style="Card"
///    ของตัวเองเป็นเหมือนหน้าการ์ด เพราะ DialogHost ไม่วาด chrome ให้) ที่ raise ผลลัพธ์กลับผ่าน event
///    ของตัวเอง (เช่น <c>event Action&lt;bool&gt;? ResultChosen</c>) - ตัว caller เป็นคนเรียก
///    <see cref="CloseTop"/> ตอนได้ผลนั้น ไม่ใช่ content เรียก DialogHost ตรงๆ (กัน content ผูกติดกับ
///    DialogHost จนใช้ที่อื่นไม่ได้)
/// 3. เรียก <see cref="ShowAsync{TResult}"/> (ใน async method อยู่แล้ว) หรือ <see cref="Push"/> +
///    callback เอง (เช่น bridge แบบ synchronous ผ่าน DispatcherFrame สำหรับ call site เดิมที่ไม่ได้
///    เป็น async - ดู ConfirmWindow.ShowViaHost ของ Console)
///
/// สิ่งที่ทำให้: พื้นมืดทับ (กันคลิกทะลุไปโดนของข้างล่าง เพราะ DialogHost เป็นลูกตัวสุดท้าย = อยู่บนสุด
/// เสมอ) · Esc = ปิดแบบยกเลิก (ส่งผลเป็น null/default) · Enter = กดปุ่มที่ติด
/// <see cref="IsDefaultButtonProperty"/>="True" · โฟกัสไม่หลุดออกนอก dialog (ผ่าน
/// KeyboardNavigation.TabNavigation="Cycle" บน slot เนื้อหา - มาตรฐานของ WPF สำหรับ trap focus ใน
/// subtree เดียว ไม่ต้องเขียน handler ดัก Tab เอง) · stack ได้ถ้า dialog หนึ่งเปิด dialog อีกใบซ้อน
/// (ของเดิมไม่หายไป แค่ไม่เห็นจนกว่าใบบนปิด)
/// </summary>
public sealed class DialogHost : Grid
{
    /// <summary>
    /// ติดกับปุ่มที่ต้องการให้ Enter กดแทน (เหมือน Button.IsDefault ของ WPF ปกติ แต่ implement เองตรงนี้
    /// เพราะ IsDefault/IsCancel ของจริงผูกกับกลไกภายในของ <c>Window</c> ซึ่งอาจมีผลข้างเคียงไม่คาดคิด
    /// (เช่นพยายามปิด Window ที่ DialogHost อาศัยอยู่) ถ้าใช้กับ content ที่ไม่ใช่ Window เอง
    /// </summary>
    public static readonly DependencyProperty IsDefaultButtonProperty = DependencyProperty.RegisterAttached(
        "IsDefaultButton", typeof(bool), typeof(DialogHost), new PropertyMetadata(false));

    public static bool GetIsDefaultButton(DependencyObject d) => (bool)d.GetValue(IsDefaultButtonProperty);
    public static void SetIsDefaultButton(DependencyObject d, bool value) => d.SetValue(IsDefaultButtonProperty, value);

    private sealed class Entry
    {
        public required FrameworkElement Content;
        public required System.Action<object?> OnClosed;
        public IInputElement? PreviousFocus;
    }

    private readonly Border _overlay;
    private readonly ContentControl _cardSlot;
    private readonly Stack<Entry> _stack = new();

    /// <summary>true = มี dialog เปิดอยู่ (อย่างน้อยหนึ่งใบในสแต็ก)</summary>
    public bool IsOpen => _stack.Count > 0;

    public DialogHost()
    {
        // ว่างเปล่า/กดคลิกทะลุผ่านได้จนกว่าจะมี dialog แรกเปิด - สำคัญเพราะ DialogHost นี้ครอบคลุม
        // ทั้งหน้าต่าง (ลูกตัวสุดท้ายของ root Grid) ถ้าทิ้งให้ Visible/hit-test ตลอดเวลาจะบังคลิกทุก
        // อย่างด้านล่างแม้ไม่มี dialog เปิดอยู่เลย
        Visibility = Visibility.Collapsed;
        Focusable = false;
        IsHitTestVisible = false;

        _overlay = new Border
        {
            // สีพื้นมืดทับ - ใช้ DynamicResource ถ้าแอปมี token นี้แล้ว ไม่งั้น fallback เป็นดำโปร่ง 60%
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            Focusable = false,
        };
        TrySetOverlayBrushFromTheme();
        Children.Add(_overlay);

        _cardSlot = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            // ดักขอบของ dialog สำหรับ Esc/Enter ผ่าน PreviewKeyDown ของ DialogHost เอง (ดูด้านล่าง) -
            // TabNavigation="Cycle" คือกลไกมาตรฐานของ WPF ที่ทำให้ Tab/Shift+Tab วนอยู่แค่ใน subtree
            // นี้ ไม่หลุดออกไปโฟกัสธาตุของ LoginRoot/DashboardHost ที่ซ่อนอยู่ข้างล่าง
        };
        KeyboardNavigation.SetTabNavigation(_cardSlot, KeyboardNavigationMode.Cycle);
        Children.Add(_cardSlot);

        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// สีพื้นมืดทับจาก resource ของแอป ถ้ามี key "Brush.Dialog.Overlay" ที่ไหนใน resource tree (ยังไม่มี
    /// ในปัจจุบัน - ทุกแอปใช้ fallback ดำ 60% ข้างบน) เผื่อ UI-3b/3c อยากปรับสีทีหลังโดยไม่ต้องแก้โค้ดนี้
    /// </summary>
    private void TrySetOverlayBrushFromTheme()
    {
        if (TryFindResource("Brush.Dialog.Overlay") is Brush brush)
        {
            _overlay.Background = brush;
        }
    }

    /// <summary>
    /// เปิด dialog ใหม่ (ถ้ามีใบเดิมเปิดอยู่ จะถูกซ้อนไว้ ไม่ถูกปิด - ดู <see cref="CloseTop"/>) แล้วย้าย
    /// โฟกัสเข้าธาตุแรกที่โฟกัสได้ใน content เอง <paramref name="onClosed"/> ถูกเรียกครั้งเดียวตอน
    /// dialog ใบนี้ถูกปิด (ผ่าน CloseTop) ไม่ว่าจะปิดด้วยปุ่มของ content เอง หรือ Esc จาก DialogHost
    /// (ผลเป็น null ถ้าปิดด้วย Esc)
    /// </summary>
    public void Push(FrameworkElement content, System.Action<object?> onClosed)
    {
        var entry = new Entry
        {
            Content = content,
            OnClosed = onClosed,
            PreviousFocus = Keyboard.FocusedElement,
        };
        _stack.Push(entry);

        _cardSlot.Content = content;
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;

        if (content.IsLoaded)
        {
            FocusFirstFocusable(content);
        }
        else
        {
            content.Loaded += OnContentLoaded;
        }
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnContentLoaded;
        FocusFirstFocusable(element);
    }

    /// <summary>
    /// เวอร์ชัน async ของ <see cref="Push"/> - ใช้จาก method ที่เป็น async อยู่แล้ว (เช่น
    /// MainWindow.ConnectButton_Click ตอนเปิด ForceChangePassword) TResult เป็น nullable เสมอ: ปิดด้วย
    /// Esc (ไม่มีผลลัพธ์จาก content) คืนค่า default(TResult) - สำหรับ bool แปลว่า false (= ยกเลิก) ซึ่ง
    /// ตรงกับความหมายที่ต้องการอยู่แล้วโดยไม่ต้องเขียนเช็คเพิ่ม
    /// </summary>
    public Task<TResult?> ShowAsync<TResult>(FrameworkElement content)
    {
        var tcs = new TaskCompletionSource<TResult?>();
        Push(content, raw => tcs.TrySetResult(raw is null ? default : (TResult)raw));
        return tcs.Task;
    }

    /// <summary>
    /// ปิด dialog ใบบนสุดของสแต็ก ส่งผล <paramref name="result"/> กลับไปให้ผู้เรียก (ผ่าน onClosed ที่
    /// ให้ไว้ตอน Push/ShowAsync) แล้วคืนโฟกัสให้ธาตุที่โฟกัสอยู่ก่อนเปิด dialog ใบนี้ - ถ้ายังมีใบอื่อยู่
    /// ข้างใต้ในสแต็ก (ถูกเปิดซ้อนไว้) จะโชว์ใบนั้นกลับขึ้นมาแทนที่จะปิด DialogHost ไปเลย
    /// </summary>
    public void CloseTop(object? result)
    {
        if (_stack.Count == 0)
        {
            return;
        }

        var entry = _stack.Pop();

        if (_stack.Count > 0)
        {
            _cardSlot.Content = _stack.Peek().Content;
        }
        else
        {
            _cardSlot.Content = null;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
        }

        try
        {
            entry.PreviousFocus?.Focus();
        }
        catch
        {
            // ธาตุเดิมอาจถูกถอดออกจาก visual tree ไปแล้ว (เช่น หน้าที่เปิด dialog ถูกเปลี่ยนไปแล้ว) -
            // ไม่ critical พอที่จะทำให้การปิด dialog ล้มเหลว
        }

        entry.OnClosed(result);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_stack.Count == 0)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseTop(null);
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (FindMarkedButton(_cardSlot.Content as DependencyObject) is { IsEnabled: true } button)
            {
                e.Handled = true;
                // RaiseEvent ของ ClickEvent ตรงๆ แทนการเรียก Command - ปุ่มส่วนใหญ่ในแอปนี้ใช้
                // event Click ใน code-behind ไม่ใช่ ICommand ทำแบบนี้จึงครอบคลุมทั้งสองแบบ
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            }
        }
    }

    private static Button? FindMarkedButton(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is Button { } button && GetIsDefaultButton(button))
        {
            return button;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var found = FindMarkedButton(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>ย้ายโฟกัสเข้าธาตุที่โฟกัสได้ตัวแรกของ content (เดินตามลำดับ visual tree) - ถ้าไม่เจอเลย
    /// (dialog ไม่มีธาตุรับ input ตัวไหนเลย เช่น About ที่มีแค่ปุ่มปิด) ก็ไม่เป็นไร ไม่ throw</summary>
    private static void FocusFirstFocusable(DependencyObject root)
    {
        if (TryFocus(root))
        {
            return;
        }
    }

    private static bool TryFocus(DependencyObject node)
    {
        if (node is UIElement { Focusable: true, IsVisible: true, IsEnabled: true } element && element.Focus())
        {
            return true;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            if (TryFocus(VisualTreeHelper.GetChild(node, i)))
            {
                return true;
            }
        }

        return false;
    }
}
