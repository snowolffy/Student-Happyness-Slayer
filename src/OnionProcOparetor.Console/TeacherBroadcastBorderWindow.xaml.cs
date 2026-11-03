using System.ComponentModel;
using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// คำขอเพิ่ม 3 พ.ย. 2569 - กรอบแดงรอบจอครูที่ค้างอยู่ตลอดเวลาที่ "กำลังส่งจอ" แม้แถบสถานะจะถูกหุบเหลือ
/// แค่ปุ่มกลมไปแล้วก็ตาม (ตัดสินแล้ว: "ระหว่างส่งให้มีกรอบแดงรอบจอครูตลอด เพื่อให้รู้ว่ายังส่งอยู่แม้หุบ
/// แถบไปแล้ว") เป็นหน้าต่างแยกจาก BroadcastWarningBarWindow เพราะอายุไม่เท่ากัน (กรอบอยู่ตลอด ไม่หุบ/ขยาย
/// ตามแถบ) และ exclude-from-capture ต้องแยก HWND อยู่แล้ว
///
/// เจ้าของอายุ: BroadcastWarningBarWindow (สร้างตอนเริ่มส่ง ปิดตอนหยุดส่ง - ดู _borderWindow)
/// </summary>
public partial class TeacherBroadcastBorderWindow : Window
{
    private bool _allowClose;

    public TeacherBroadcastBorderWindow()
    {
        InitializeComponent();

        // ครอบแค่จอหลัก (จอเดียวกับที่ ScreenCaptureService capture) - ไม่รองรับหลายจอรอบนี้
        // เหมือน TeacherScreenViewWindow ฝั่งนักเรียน (ดู comment ที่นั่น)
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        SourceInitialized += (_, _) =>
        {
            // ต้องมี HWND จริงแล้วก่อนเรียก Win32 call นี้ - ดู TeacherOnlyWindowHelper
            TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: true);
        };

        Loaded += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine(
                $"[teacher-broadcast-border] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");
        };
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กรอบนี้เป็นของ BroadcastWarningBarWindow เท่านั้น - ไม่มีทางที่ user กด Alt-F4/ปิดกรอบ
            // เองได้ (ไม่มี chrome ให้กด X อยู่แล้ว แต่กัน Alt-F4 ไว้เผื่อ user รู้ทริค)
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
