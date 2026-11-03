using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OnionProcOparetor.UI.Controls;

/// <summary>
/// UI-3a (2026-11-03): โฮสต์แผงสไลด์ขวาแบบทั่วไป (generic) ต่อยอดจากลาย "ClientInspector" เดิมใน
/// DashboardWindow.xaml (Border + TranslateTransform ชื่อ InspectorSlide + ปุ่มกาง/ย่อ/ปิด) - ยกลอจิก
/// สไลด์/เปิด-ปิดเดิมนั้นออกมาเป็น control แยกที่ใช้ซ้ำได้ ไม่ต้องลอกโค้ด transform/animation ใหม่ทุก
/// ครั้งที่ UI-3c เพิ่มแผงใหม่ (ClientSettings, RunningProcesses, PendingCommands, RuleOverrides,
/// ActionPanel)
///
/// ต่างจาก <see cref="DialogHost"/>: ไม่มีพื้นมืดทับเต็มจอ (inspector เดิมก็ไม่มี - ครูยังเห็น/คลิก
/// รายการเครื่องข้างหลังได้ระหว่างแผงเปิดอยู่) แค่สไลด์เข้าจากขวาสุดของพื้นที่ที่ host ถูกวาง (ปกติคือ
/// ทับอยู่บน DashboardHost - วาง SidePanelHost เป็นลูกของ root Grid เดียวกับ DialogHost แต่ประกาศ
/// ก่อน DialogHost เพื่อให้ dialog ลอยทับแผงนี้ได้เสมอถ้าเปิดจากแผงอีกที)
///
/// วิธีใช้: <see cref="Show"/> ด้วย FrameworkElement เนื้อหา (มักเป็น Border ธีม Card ของตัวเอง) แล้ว
/// <see cref="Close"/> ตอนผู้ใช้กดปุ่มปิดของ content เอง (เหมือน DialogHost เนื้อหาไม่ผูกติดกับ host
/// ตรงๆ - content raise event ของตัวเอง แล้ว caller เรียก Close) Esc ปิดแผงให้เองเสมอ
/// </summary>
public sealed class SidePanelHost : Grid
{
    private readonly ContentControl _panelSlot;
    private readonly TranslateTransform _slide = new();

    /// <summary>ความกว้างของแผง (ปรับได้ต่อ instance - ของเดิม ClientInspector ใช้ 284)</summary>
    public double PanelWidth
    {
        get => _panelSlot.Width;
        set => _panelSlot.Width = value;
    }

    /// <summary>true = แผงกางอยู่ตอนนี้</summary>
    public bool IsOpen { get; private set; }

    /// <summary>เรียกทุกครั้งที่แผงถูกปิด ไม่ว่าจะปิดด้วย <see cref="Close"/> ตรงๆ หรือ Esc</summary>
    public event System.Action? Closed;

    public SidePanelHost()
    {
        IsHitTestVisible = false;
        Focusable = false;

        _panelSlot = new ContentControl
        {
            Width = 284,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false,
            Visibility = Visibility.Collapsed,
            RenderTransform = _slide,
        };
        KeyboardNavigation.SetTabNavigation(_panelSlot, KeyboardNavigationMode.Cycle);
        Children.Add(_panelSlot);

        // UI3A-FIX (ตรวจด้วย input จริงแล้วพบจริง 2026-11-03): PreviewKeyDown เป็น tunneling event -
        // วิ่งจาก root ลงไปหา element ที่ "โฟกัสอยู่ตอนนั้น" เท่านั้น ถ้าไม่มีอะไรในแผงนี้ถูกโฟกัสเลย (เช่น
        // เปิดแผงจากปุ่มในตารางเครื่อง ซึ่งโฟกัสยังค้างอยู่ที่ปุ่มนั้น ไม่ใช่ใน subtree ของ SidePanelHost)
        // Esc จะไม่มีทางวิ่งผ่าน handler นี้เลย เพราะ route ไม่ได้พาดผ่าน SidePanelHost - ย้ายโฟกัสเข้า
        // ธาตุแรกของเนื้อหาทุกครั้งที่ Show() (เหมือน DialogHost.FocusFirstFocusable) แก้ปัญหานี้ที่ต้นเหตุ
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// กางแผง - ถ้ามีเนื้อหาเดิมอยู่แล้ว (เปลี่ยนจากแผงหนึ่งไปอีกแผงโดยไม่ปิดก่อน) จะสลับเนื้อหาทันทีโดย
    /// ไม่เล่น slide-out/slide-in ซ้ำ (ของเดิม ClientInspector ก็ทำแบบนี้ตอนคลิกเครื่องอื่นระหว่างแผงกาง
    /// อยู่ - สลับข้อมูลในแผงเดิม ไม่ปิด-เปิดใหม่)
    /// </summary>
    public void Show(FrameworkElement content)
    {
        var alreadyOpen = IsOpen;
        _panelSlot.Content = content;
        IsHitTestVisible = true;
        _panelSlot.Visibility = Visibility.Visible;
        IsOpen = true;

        if (content.IsLoaded)
        {
            FocusFirstFocusable(content);
        }
        else
        {
            content.Loaded += OnContentLoaded;
        }

        if (alreadyOpen)
        {
            return;
        }

        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _slide.X = _panelSlot.Width;
        var animation = new DoubleAnimation(_panelSlot.Width, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>ย่อแผงกลับออกจากจอ แล้วยิง <see cref="Closed"/> ให้ caller เคลียร์สถานะของตัวเอง (เช่น
    /// เลิกเลือกแถวที่กำลังแสดงอยู่) - ไม่มีผลถ้าแผงปิดอยู่แล้ว</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        IsHitTestVisible = false;

        var animation = new DoubleAnimation(0, _panelSlot.Width, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        animation.Completed += (_, _) =>
        {
            if (!IsOpen)
            {
                _panelSlot.Visibility = Visibility.Collapsed;
                _panelSlot.Content = null;
            }
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, animation);

        Closed?.Invoke();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsOpen && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnContentLoaded;
        FocusFirstFocusable(element);
    }

    /// <summary>เหมือน DialogHost.FocusFirstFocusable ทุกประการ (จงใจไม่แชร์โค้ดข้ามสองไฟล์สำหรับ
    /// private helper เล็กขนาดนี้) - ดู comment เต็มที่ DialogHost.cs</summary>
    private static void FocusFirstFocusable(DependencyObject root) => TryFocus(root);

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
