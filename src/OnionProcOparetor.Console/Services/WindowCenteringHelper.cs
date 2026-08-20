using System.Windows;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// WindowStartupLocation="CenterOwner" ไม่แม่นยำเมื่อใช้ร่วมกับ SizeToContent - WPF คำนวณตำแหน่งจาก
/// ขนาดหน้าต่างตอนที่ยังไม่ resolve เสร็จ (ก่อน SizeToContent วัดเนื้อหาจริงเสร็จ) ทำให้หน้าต่างเลื่อนออก
/// จากจุดกึ่งกลางจริงไปทางขวา/ล่าง (ยืนยันจาก screenshot จริง - ทุกหน้าต่างที่เพิ่ง converted เป็น
/// SizeToContent เพื่อแก้บั๊ก title-bar-height ก่อนหน้านี้ เกิดปัญหานี้ตามมาทันที)
///
/// แก้โดยคำนวณตำแหน่งเองใน Loaded event แทน (ตอนนั้น ActualWidth/ActualHeight เป็นค่าจริงที่ resolve
/// เสร็จแล้วแน่นอน) หน้าต่างที่เรียกใช้ตัวนี้ต้องตั้ง WindowStartupLocation="Manual" ใน XAML ไว้ด้วย
/// ไม่งั้นจะเห็นหน้าต่าง "กระตุก" จากตำแหน่งผิดไปตำแหน่งถูกตอน Loaded ยิง
/// </summary>
public static class WindowCenteringHelper
{
    /// <summary>เรียกจาก constructor: <c>Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);</c></summary>
    public static void CenterOverOwnerOrScreen(Window window)
    {
        if (window.Owner is not null)
        {
            window.Left = window.Owner.Left + (window.Owner.ActualWidth - window.ActualWidth) / 2;
            window.Top = window.Owner.Top + (window.Owner.ActualHeight - window.ActualHeight) / 2;
        }
        else
        {
            window.Left = (SystemParameters.PrimaryScreenWidth - window.ActualWidth) / 2;
            window.Top = (SystemParameters.PrimaryScreenHeight - window.ActualHeight) / 2;
        }
    }
}
