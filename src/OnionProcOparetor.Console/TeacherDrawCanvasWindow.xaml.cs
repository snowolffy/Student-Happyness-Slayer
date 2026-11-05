using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Ink;
using System.Windows.Interop;
using System.Windows.Media;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>UI-9 (2026-11-04): เครื่องมือในกล่องเครื่องมือเขียนบนจอครู - ใช้ระบุว่า slider "ปรับขนาดเส้น" กำลังแก้เครื่องมือไหน</summary>
public enum DrawTool
{
    Pen,
    Mouse,
    Highlighter,
    Eraser
}

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
            // UI-11: ไม่ให้แผ่นใสถูก activate ตอนคลิกวาด - ไม่งั้นมันลอยเหนือ toolbox/แถบ แล้วพื้น #01000000
            // กลืนคลิกที่ตั้งใจจะไปปุ่ม "เมาส์" (สลับกลับไม่ได้หลังเขียน)
            TeacherOnlyWindowHelper.SetNoActivate(this);
            TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: true);

            ((HwndSource)PresentationSource.FromVisual(this)!).AddHook(HitTestHook);
        };
    }

    // ==================== UI-12: แผ่นใสต้องไม่รับ input เหนือหน้าต่างควบคุมของครู ====================

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    // หน้าต่างควบคุมที่แผ่นใสต้อง "ปล่อยทะลุ" (กล่องเครื่องมือ/แถบแดง/minimap) - เก็บเป็น Window แล้วอ่านตำแหน่งจริงทุกครั้งที่ hit-test
    // เพราะลากได้/หุบกางได้/แสดงซ่อนได้ ทุกหน้าต่างอยู่เทรด UI เดียวกัน HTTRANSPARENT จึงส่งต่อให้หน้าต่างข้างใต้ในเทรดเดียวกันได้
    private readonly List<Window> _passThroughWindows = new();

    /// <summary>
    /// UI-12: ลงทะเบียนหน้าต่างที่แผ่นใสห้ามรับ input ทับ (คืน HTTRANSPARENT ให้ WM_NCHITTEST ในกรอบหน้าต่างนั้น) - ไม่พึ่ง z-order
    /// ดังนั้นต่อให้แผ่นใสถูกยกขึ้นบนสุดด้วยเหตุใด ปุ่มบนกล่องเครื่องมือก็ยังกดได้
    /// </summary>
    public void ExcludeInputFrom(Window window)
    {
        if (!_passThroughWindows.Contains(window))
        {
            _passThroughWindows.Add(window);
        }
    }

    private IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            // คลิกบนแผ่นใสไม่ต้อง activate (กัน foreground/z-order เปลี่ยน)
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        if (msg != WM_NCHITTEST)
        {
            return IntPtr.Zero;
        }

        // lParam = พิกัดจอจริง (พิกเซล) signed 16 บิตต่อแกน
        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        foreach (var window in _passThroughWindows)
        {
            if (!window.IsVisible)
            {
                continue;
            }

            var other = new WindowInteropHelper(window).Handle;
            if (other != IntPtr.Zero && GetWindowRect(other, out var r)
                && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>true = กำลังอยู่ในโหมดเขียน (รับ mouse/pen input วาดเส้นได้) - BroadcastWarningBarWindow อ่านค่านี้ไปอัปเดตปุ่มบนแถบ</summary>
    public bool IsDrawMode => _isDrawMode;

    /// <summary>UI-11: ยิงตอนเข้าโหมดเขียน - toolbox/แถบ/minimap ฟังแล้วยกตัวเองเหนือแผ่นใสอีกรอบ</summary>
    public event Action? DrawModeEntered;

    /// <summary>สลับโหมดเขียน/เมาส์ปกติ - เรียกจากปุ่มบนแถบหรือ Ctrl+Shift+D (ดู BroadcastWarningBarWindow)</summary>
    public void SetDrawMode(bool drawMode)
    {
        var entering = drawMode && !_isDrawMode;
        _isDrawMode = drawMode;
        TeacherOnlyWindowHelper.SetClickThrough(this, clickThrough: !drawMode);
        if (entering)
        {
            DrawModeEntered?.Invoke();
        }
    }

    /// <summary>UI-9 (2026-11-04): ขนาดเส้นปากกา/ไฮไลต์ล่าสุดที่ตั้งไว้ (DIP) - toolbox อ่าน/เขียนผ่าน slider "ปรับขนาดเส้นได้" จำค่าไว้คนละชุดกับไฮไลต์เพราะขนาดปกติต่างกันมาก (ปากกาเล็ก/ไฮไลต์หนา)</summary>
    public double PenWidth { get; private set; } = 4;

    /// <summary>UI-9 (2026-11-04): ขนาดไฮไลต์ล่าสุด แยกจาก PenWidth - ดูเหตุผลที่ PenWidth</summary>
    public double HighlighterWidth { get; private set; } = 24;

    /// <summary>UI-9 (2026-11-04): ขนาดยางลบล่าสุด - WPF InkCanvas ไม่มี property ปรับขนาด EraseByPoint ตรงๆ จึงจำลองด้วยการสลับไป EraseByStroke (ลบทั้งเส้น) เมื่อตั้งขนาดใหญ่เกิน EraseByStrokeThreshold (ดู ApplyEraserTool)</summary>
    public double EraserWidth { get; private set; } = 10;

    private const double EraseByStrokeThreshold = 24;

    private bool _isHighlighterTool;
    private Color _currentColor;

    public void ApplyPenTool(Color color, bool isHighlighter)
    {
        _isHighlighterTool = isHighlighter;
        _currentColor = color;

        var width = isHighlighter ? HighlighterWidth : PenWidth;
        Canvas.EditingMode = InkCanvasEditingMode.Ink;
        Canvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = width,
            Height = width,
            IsHighlighter = isHighlighter,
            StylusTip = isHighlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = true
        };
    }

    /// <summary>
    /// UI-9 (2026-11-04): ปรับขนาดเส้นของเครื่องมือที่กำลังเลือกอยู่ (ปากกา/ไฮไลต์/ยางลบ) จาก slider
    /// ในกล่องเครื่องมือ - "ปรับขนาดเส้นได้ (ปากกา/ไฮไลต์/ยางลบ)" ตามที่ตัดสินไว้ใน roadmap UI-9
    /// </summary>
    public void SetToolWidth(DrawTool tool, double width)
    {
        switch (tool)
        {
            case DrawTool.Pen:
                PenWidth = width;
                if (!_isHighlighterTool && Canvas.EditingMode == InkCanvasEditingMode.Ink)
                {
                    ApplyPenTool(_currentColor, isHighlighter: false);
                }
                break;
            case DrawTool.Highlighter:
                HighlighterWidth = width;
                if (_isHighlighterTool && Canvas.EditingMode == InkCanvasEditingMode.Ink)
                {
                    ApplyPenTool(_currentColor, isHighlighter: true);
                }
                break;
            case DrawTool.Eraser:
                EraserWidth = width;
                if (Canvas.EditingMode is InkCanvasEditingMode.EraseByPoint or InkCanvasEditingMode.EraseByStroke)
                {
                    ApplyEraserTool();
                }
                break;
        }
    }

    /// <summary>
    /// UI-9 (2026-11-04) - WPF InkCanvas ไม่เปิด public API ให้ปรับขนาดพื้นที่ลบของ EraseByPoint ตรงๆ
    /// (ขนาดคงที่ตาม default ของ Windows) จึงจำลอง "ยางลบใหญ่ขึ้น" ด้วยการสลับไปโหมด EraseByStroke
    /// (คลิก/ลากทับเส้นไหนก็ลบทั้งเส้นนั้นเลย) เมื่อตั้งขนาดเกิน EraseByStrokeThreshold - ขนาดเล็กกว่านั้น
    /// ยังใช้ EraseByPoint (ลบเฉพาะจุดที่ทับ) ตามเดิม
    /// </summary>
    public void ApplyEraserTool()
    {
        Canvas.EditingMode = EraserWidth >= EraseByStrokeThreshold
            ? InkCanvasEditingMode.EraseByStroke
            : InkCanvasEditingMode.EraseByPoint;
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
