using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// คำขอเพิ่ม 3 พ.ย. 2569 - แผ่นใสเขียนบนจอครู (ดู doc comment ใน .xaml) เจ้าของอายุ:
/// BroadcastWarningBarWindow (สร้างพร้อมกรอบแดงตอนเริ่มส่ง ปิดตอนหยุดส่ง)
/// </summary>
public partial class TeacherDrawCanvasWindow : Window
{
    /// <summary>ปากกา 4 สี (ตัดสินแล้ว "ปากกา 3-4 สี") - แดง/น้ำเงิน/เขียว/เหลือง อ่านชัดบนพื้นหลังโปรแกรมส่วนใหญ่</summary>
    public static readonly Color[] PenColors =
    {
        Color.FromRgb(0xE5, 0x39, 0x35), // แดง
        Color.FromRgb(0x19, 0x76, 0xD2), // น้ำเงิน
        Color.FromRgb(0x43, 0xA0, 0x47), // เขียว
        Color.FromRgb(0xFB, 0xC0, 0x2D), // เหลือง
    };

    /// <summary>สีไฮไลต์ - เหลืองโปร่งแสงหนา ทับข้อความได้โดยยังอ่านออก</summary>
    public static readonly Color HighlightColor = Color.FromArgb(0x80, 0xFF, 0xEB, 0x3B);

    private bool _allowClose;
    private bool _isDrawMode;

    public TeacherDrawCanvasWindow()
    {
        InitializeComponent();

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        Canvas.EditingMode = InkCanvasEditingMode.Ink;
        ApplyPenTool(PenColors[0], isHighlighter: false);

        SourceInitialized += (_, _) =>
        {
            // เริ่มที่โหมด "เมาส์ปกติ" (คลิกทะลุ) เสมอ - ครูต้องกดเปิดโหมดเขียนเองก่อนถึงจะวาดได้
            TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: true);
        };
    }

    /// <summary>true = กำลังอยู่ในโหมดเขียน (รับ mouse/pen input วาดเส้นได้) - BroadcastWarningBarWindow อ่านค่านี้ไปอัปเดตปุ่มบนแถบ</summary>
    public bool IsDrawMode => _isDrawMode;

    /// <summary>สลับโหมดเขียน/เมาส์ปกติ - เรียกจากปุ่มบนแถบหรือ Ctrl+Shift+D (ดู BroadcastWarningBarWindow)</summary>
    public void SetDrawMode(bool drawMode)
    {
        _isDrawMode = drawMode;
        TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: !drawMode);
    }

    public void ApplyPenTool(Color color, bool isHighlighter)
    {
        Canvas.EditingMode = InkCanvasEditingMode.Ink;
        Canvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = isHighlighter ? 24 : 4,
            Height = isHighlighter ? 24 : 4,
            IsHighlighter = isHighlighter,
            StylusTip = isHighlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = true
        };
    }

    public void ApplyEraserTool()
    {
        Canvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
    }

    public void ClearAll()
    {
        Canvas.Strokes.Clear();
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
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
