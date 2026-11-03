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
    /// <summary>ความหนากรอบ (DIP) - ตัดสินแล้ว 3 พ.ย.: 3-4px</summary>
    private const double Thickness = 4;

    private readonly List<TeacherBroadcastBorderWindow> _otherEdges = new();
    private bool _allowClose;

    /// <summary>
    /// สร้างกรอบทั้งชุด: หน้าต่างนี้เป็นแถบบน แล้วสร้างแถบล่าง/ซ้าย/ขวาเป็นหน้าต่างพี่น้องอีก 3 อัน
    /// (แสดงพร้อมกันตอน Loaded ปิดพร้อมกันตอน ForceClose) - ผู้เรียกยังใช้ new + Show() + ForceClose()
    /// เหมือนตอนเป็นหน้าต่างเดียว ดูเหตุผลที่ต้องแยก 4 หน้าต่างใน .xaml
    /// </summary>
    public TeacherBroadcastBorderWindow()
        : this(GetEdges()[0])
    {
        foreach (var edge in GetEdges().Skip(1))
        {
            _otherEdges.Add(new TeacherBroadcastBorderWindow(edge));
        }

        Loaded += (_, _) =>
        {
            foreach (var edge in _otherEdges)
            {
                edge.Show();
            }
        };
    }

    private TeacherBroadcastBorderWindow(Rect bounds)
    {
        InitializeComponent();

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        SourceInitialized += (_, _) =>
        {
            // ต้องมี HWND จริงแล้วก่อนเรียก Win32 call นี้ - ดู TeacherOnlyWindowHelper
            TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: true);
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine(
                $"[teacher-broadcast-border] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");
        };
    }

    /// <summary>
    /// แถบ 4 เส้นชิดขอบจอหลัก (จอเดียวกับที่ ScreenCaptureService จับ - ยังไม่รองรับหลายจอ เหมือน
    /// TeacherScreenViewWindow ฝั่งนักเรียน) ลำดับ: บน, ล่าง, ซ้าย, ขวา
    /// </summary>
    private static Rect[] GetEdges()
    {
        var width = SystemParameters.PrimaryScreenWidth;
        var height = SystemParameters.PrimaryScreenHeight;
        return
        [
            new Rect(0, 0, width, Thickness),
            new Rect(0, height - Thickness, width, Thickness),
            new Rect(0, Thickness, Thickness, height - 2 * Thickness),
            new Rect(width - Thickness, Thickness, Thickness, height - 2 * Thickness)
        ];
    }

    public void ForceClose()
    {
        foreach (var edge in _otherEdges)
        {
            edge.ForceClose();
        }
        _otherEdges.Clear();

        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // กรอบนี้เป็นของ BroadcastWarningBarWindow เท่านั้น - กัน Alt-F4 ไว้เผื่อ user รู้ทริค
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
