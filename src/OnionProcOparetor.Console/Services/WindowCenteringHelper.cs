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
    public static void CenterOverOwnerOrScreen(Window window) => Center(window, allowSettlePass: true);

    /// <summary>
    /// <paramref name="allowSettlePass"/> = จัดกึ่งกลางซ้ำอีกหนึ่งรอบหลัง layout นิ่งแล้ว
    ///
    /// จำเป็นเฉพาะหน้าต่างที่ใช้ SizeToContent: ตอน Loaded ยิง ความสูงยังไม่ใช่ค่าสุดท้าย (วัดจริงแล้ว
    /// ConfirmWindow สูง 192 ตอน Loaded แล้วหดเหลือ 180 หลังจากนั้น) ทำให้วางเยื้องขึ้นไป 6px
    /// รอบแรกยังต้องมีอยู่เพื่อไม่ให้เห็นหน้าต่างกระโดดจากมุมจอ ส่วนรอบสองขยับแค่ไม่กี่พิกเซลจนมองไม่ทัน
    /// </summary>
    private static void Center(Window window, bool allowSettlePass)
    {
        // เจ้าของหน้าต่างถูกย่อลงแถบงานอยู่ = Left/Top เป็นค่าลบมหาศาล (-32000) การจัดกึ่งกลางกับมัน
        // จะเหวี่ยงหน้าต่างลูกออกนอกจอไปเลย - กรณีนั้นถือเสมือนไม่มีเจ้าของ
        var owner = window.Owner;
        var hasUsableOwner = owner is not null
            && owner.WindowState != WindowState.Minimized
            && owner.ActualWidth > 0
            && owner.ActualHeight > 0;

        double left, top;
        if (hasUsableOwner)
        {
            left = owner!.Left + (owner.ActualWidth - window.ActualWidth) / 2;
            top = owner.Top + (owner.ActualHeight - window.ActualHeight) / 2;
        }
        else
        {
            left = (SystemParameters.PrimaryScreenWidth - window.ActualWidth) / 2;
            top = (SystemParameters.PrimaryScreenHeight - window.ActualHeight) / 2;
        }

        // หนีบให้อยู่ในพื้นที่ทำงานเสมอ (ไม่ทับแถบงาน) - หน้าต่างลูกที่กว้างกว่าเจ้าของ หรือเจ้าของที่
        // ถูกลากไปชิดขอบจอ ทำให้ค่ากึ่งกลางติดลบได้ แล้วแถบหัวจะไปโผล่นอกจอ = ปิดหรือลากกลับไม่ได้เลย
        var area = SystemParameters.WorkArea;
        window.Left = Math.Max(area.Left, Math.Min(left, area.Right - window.ActualWidth));
        window.Top = Math.Max(area.Top, Math.Min(top, area.Bottom - window.ActualHeight));

        if (allowSettlePass && window.SizeToContent != SizeToContent.Manual)
        {
            window.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () => Center(window, allowSettlePass: false));
        }
    }
}
