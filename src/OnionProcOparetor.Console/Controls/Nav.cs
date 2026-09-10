using System.Windows;

namespace OnionProcOparetor.Console.Controls;

/// <summary>
/// Attached property เล็ก ๆ สำหรับ sidebar ของ DashboardWindow
///
/// ทำไมต้องเป็น attached property ไม่ใช่ควบคุมจาก code-behind: sidebar สร้างจาก
/// <c>TabControl</c> เดิมทั้งดุ้น (panel ที่ <c>IsItemsHost="True"</c> ย้ายไปอยู่คอลัมน์ซ้าย
/// แล้วเปลี่ยน ItemContainerStyle เป็นแถวเมนู) ข้อดีคือ **ไม่ต้องแตะ event wiring หรือโค้ดเดิมเลย**
/// - <c>MainTabControl_SelectionChanged</c>, <c>ScreenViewTabItem</c>, <c>AuditLogTabItem</c>
/// ทุกอย่างทำงานเหมือนเดิมทุกประการ แต่แลกมาด้วยข้อจำกัดว่า TabItem แต่ละตัวเป็นแค่ "ItemContainer"
/// ที่เราส่งข้อมูลเพิ่มเข้าไปทาง property ปกติไม่ได้ (มีแค่ Header กับ Tag) - attached property
/// จึงเป็นช่องทางที่ถูกต้องสำหรับ "ไอคอน" กับ "หัวข้อกลุ่ม" ของแต่ละแถว
/// </summary>
public static class Nav
{
    /// <summary>
    /// ชื่อไอคอนแบบเดียวกับ Icon.razor (kebab-case) ที่จะโชว์หน้าข้อความในแถวเมนู
    /// ส่งต่อให้ <see cref="IconPresenter.Icon"/> ตรง ๆ
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Nav), new FrameworkPropertyMetadata(string.Empty));

    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    /// <summary>
    /// หัวข้อกลุ่มที่จะแสดง "เหนือ" แถวนี้ - ตั้งเฉพาะแถวแรกของแต่ละกลุ่ม ที่เหลือปล่อยว่าง
    ///
    /// วิธีนี้แทนการทำ 3 panel แยกกัน เพราะ TabControl มี items host ได้ panel เดียวเท่านั้น
    /// (<c>IsItemsHost</c>) จะแทรก TextBlock หัวข้อคั่นกลางระหว่าง item ไม่ได้ - ให้แต่ละแถววาด
    /// หัวข้อของตัวเองแทน แล้วซ่อนเมื่อค่าว่าง ผลลัพธ์บนจอเหมือนกันทุกประการ
    /// </summary>
    public static readonly DependencyProperty GroupProperty = DependencyProperty.RegisterAttached(
        "Group", typeof(string), typeof(Nav), new FrameworkPropertyMetadata(string.Empty));

    public static string GetGroup(DependencyObject d) => (string)d.GetValue(GroupProperty);
    public static void SetGroup(DependencyObject d, string value) => d.SetValue(GroupProperty, value);

    /// <summary>
    /// sidebar หดอยู่หรือไม่ - ตั้งไว้ที่ TabControl ตัวเดียว แล้ว **ไหลลงไปเองทั้งต้นไม้** ด้วย
    /// <see cref="FrameworkPropertyMetadataOptions.Inherits"/> ทำให้ทั้งแถวเมนูทุกแถว หัวข้อกลุ่ม
    /// แบรนด์ และปุ่มท้าย sidebar รู้สถานะพร้อมกันโดยไม่ต้องเดินหา element ทีละตัวจาก code-behind
    /// (ซึ่งจะเปราะมากเพราะทุกอย่างอยู่ใน ControlTemplate คนละ namescope กัน)
    /// </summary>
    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.RegisterAttached(
        "IsCollapsed", typeof(bool), typeof(Nav),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCollapsed(DependencyObject d) => (bool)d.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(DependencyObject d, bool value) => d.SetValue(IsCollapsedProperty, value);

    /// <summary>
    /// แถบบน (โลโก้สถานะ / แบนเนอร์เตือน / ข้อความสถานะ / ปุ่ม refresh) ที่จะถูกวางในคอลัมน์ขวา
    /// เหนือเนื้อหาแท็บ และ <see cref="StatusBarProperty"/> คือแถบล่างสุด
    ///
    /// ทำไมต้องส่งเข้ามาทาง attached property แทนที่จะเขียนไว้ใน ControlTemplate ตรง ๆ:
    /// **element ที่ประกาศอยู่ใน ControlTemplate จะไม่มี field ให้ code-behind เรียกถึงด้วย x:Name**
    /// (คนละ namescope) แต่ DashboardWindow.xaml.cs อ้างถึง StatusText อย่างเดียว 22 จุด บวก
    /// ServerLabel / GlobalOffBanner / FooterServerText / FooterUserText อีก - ย้ายเข้า template
    /// ตรง ๆ = ต้องแก้ code-behind ทุกจุดนั้น
    ///
    /// พอส่งเป็น "ค่า" ของ attached property แทน UI ก้อนนั้นยังถูกประกาศอยู่ใน DashboardWindow.xaml
    /// เหมือนเดิม จึงยังอยู่ใน namescope ของหน้าต่างและยังมี field ครบทุกตัว ส่วน template แค่เอา
    /// ContentPresenter ไปวางให้ถูกที่ - โครงหน้าจอเปลี่ยนทั้งหมดโดยไม่แตะ code-behind แม้แต่บรรทัดเดียว
    /// </summary>
    public static readonly DependencyProperty TopBarProperty = DependencyProperty.RegisterAttached(
        "TopBar", typeof(object), typeof(Nav), new FrameworkPropertyMetadata(null));

    public static object? GetTopBar(DependencyObject d) => d.GetValue(TopBarProperty);
    public static void SetTopBar(DependencyObject d, object? value) => d.SetValue(TopBarProperty, value);

    /// <summary>แถบสถานะล่างสุดของหน้าต่าง - เหตุผลเดียวกับ <see cref="TopBarProperty"/></summary>
    public static readonly DependencyProperty StatusBarProperty = DependencyProperty.RegisterAttached(
        "StatusBar", typeof(object), typeof(Nav), new FrameworkPropertyMetadata(null));

    public static object? GetStatusBar(DependencyObject d) => d.GetValue(StatusBarProperty);
    public static void SetStatusBar(DependencyObject d, object? value) => d.SetValue(StatusBarProperty, value);
}
