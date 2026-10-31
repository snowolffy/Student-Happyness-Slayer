using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager.Controls;

/// <summary>
/// ตัวแสดง toast ของ <see cref="ToastService"/> - วางไว้ชั้นบนสุดของหน้าต่างหลักจุดเดียว
/// (DashboardWindow) ผูกกับ <see cref="ToastService.Items"/> ตรง ๆ จึงไม่ต้องมี code ส่งต่ออะไรเลย
///
/// ตำแหน่งขยับหลบแผงข้างได้ด้วยการตั้ง <see cref="FrameworkElement.Margin"/> จากหน้าต่างเจ้าของ
/// (ดู DashboardWindow.UpdateToastOffset) - ไม่ได้ทำเป็น property ของตัวเอง เพราะคนที่รู้ว่า
/// "ตอนนี้มีอะไรกินที่ฝั่งขวาอยู่บ้าง" คือหน้าต่าง ไม่ใช่ตัวแสดง toast
/// </summary>
public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
        ToastList.ItemsSource = ToastService.Items;
    }

    private static ToastItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ToastItem;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ToastService.Dismiss(item);
        }
    }

    private void ActionLink_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ToastService.InvokeAction(item);
        }
    }

    private void Toast_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            ToastService.Hold(item);
        }
    }

    private void Toast_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // toast อาจถูกปิดไปแล้วระหว่างที่เมาส์ยังชี้อยู่ (กด ✕) - Release จะตั้ง timer ให้ของที่ไม่อยู่
        // ในรายการแล้วเปล่า ๆ แม้จะไม่มีผลเสียแต่ก็ไม่ควรทำ
        if (ItemOf(sender) is { } item && ToastService.Items.Contains(item))
        {
            ToastService.Release(item);
        }
    }
}
