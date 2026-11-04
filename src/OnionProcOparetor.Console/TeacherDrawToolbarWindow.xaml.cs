using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// UI-9 (2026-11-04) redesign "กล่องเครื่องมือ" (ดู doc comment เต็มใน .xaml) - รวมเครื่องมือเขียน/สี/
/// ขนาดเส้น/ซูมไว้ที่เดียว (เดิมแยกเป็นปุ่มบน BroadcastWarningBarWindow + แผงนี้คนละที่) เจ้าของอายุ:
/// BroadcastWarningBarWindow เหมือนเดิม
/// </summary>
public partial class TeacherDrawToolbarWindow : Window
{
    private readonly TeacherDrawCanvasWindow _canvas;
    private readonly ScreenCaptureService _captureService;
    private readonly List<ToggleButton> _toolButtons;
    private bool _allowClose;
    private bool _suppressSliderEvent;
    private TeacherZoomMinimapWindow? _minimapWindow;

    /// <summary>สีปากกาที่เลือกไว้ล่าสุด (ใช้กับปุ่มปากกาเท่านั้น - ไฮไลต์คงสีเหลืองโปร่งแสงตามที่ตัดสินไว้)</summary>
    private Color _selectedPenColor = TeacherDrawCanvasWindow.PenColors[0];

    public TeacherDrawToolbarWindow(TeacherDrawCanvasWindow canvas, ScreenCaptureService captureService)
    {
        // ต้องตั้งค่า field ก่อนเรียก InitializeComponent() เสมอ - บั๊กจริงที่เจอตอนทดสอบด้วย harness
        // input จริง 4 พ.ย.: XAML ตั้ง ZoomSlider Value="3" ตรงๆ ซึ่งทำให้ ZoomSlider_ValueChanged ถูกยิง
        // แบบ synchronous ขณะ parse XAML (ก่อน InitializeComponent() คืนค่าด้วยซ้ำ) ถ้า _captureService
        // ยังเป็น null อยู่ตอนนั้นจะได้ NullReferenceException ทันทีที่เปิดกล่องเครื่องมือ
        _canvas = canvas;
        _captureService = captureService;
        InitializeComponent();
        _toolButtons = new List<ToggleButton> { PencilButton, MouseButton, HighlighterButton, EraserButton };

        var insertIndex = 0;
        foreach (var color in TeacherDrawCanvasWindow.PenColors)
        {
            var button = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(color == _selectedPenColor ? 2 : 1)
            };
            button.Click += (_, _) => ColorButton_Click(button, color);
            ColorButtonsPanel.Children.Insert(insertIndex++, button);
        }

        SizeSlider.Value = _canvas.PenWidth;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 340;
        Top = workArea.Top + 16;

        SourceInitialized += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine($"[teacher-draw-toolbar] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");

            // มุมโค้งจาก window region แทนความโปร่งใส - ดู TeacherOnlyWindowHelper.ApplyRoundedRegion
            SizeChanged += (_, _) => TeacherOnlyWindowHelper.ApplyRoundedRegion(this, 10);
        };
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        // กันลากแผงออกนอกขอบจอทำงานจนหาไม่เจอ/กดไม่ถึง เหมือนบั๊กที่เจอกับปุ่มกลมของ
        // BroadcastWarningBarWindow (ดู ClampToWorkArea ที่นั่น) - แผงนี้มีความเสี่ยงเดียวกัน
        // เพราะลากได้อิสระและ DragMove ไม่เช็คขอบจอเองเลย
        ClampToWorkArea();
    }

    /// <summary>ง้างตำแหน่งแผงให้อยู่ในขอบเขต WorkArea ของจอหลักเสมอ - ดู comment เต็มที่จุดเรียกใน ctor</summary>
    private void ClampToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;

        if (Left + ActualWidth > workArea.Right)
        {
            Left = Math.Max(workArea.Left, workArea.Right - ActualWidth);
        }
        if (Top + ActualHeight > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
        }
        if (Left < workArea.Left)
        {
            Left = workArea.Left;
        }
        if (Top < workArea.Top)
        {
            Top = workArea.Top;
        }
    }

    // ==================== เครื่องมือ (ปากกา/เมาส์ปกติ/ไฮไลต์/ยางลบ) ====================

    /// <summary>เลือกปุ่มเครื่องมือเดียว (เหมือน RadioButton แต่ใช้ ToggleButton เพื่อให้วาง IconPresenter ได้ง่ายกว่า) ปุ่มอื่นถูกปล่อยทั้งหมด</summary>
    private void SelectToolButton(ToggleButton selected)
    {
        foreach (var button in _toolButtons)
        {
            button.IsChecked = button == selected;
        }
    }

    private void PencilButton_Click(object sender, RoutedEventArgs e)
    {
        SelectToolButton(PencilButton);
        _canvas.SetDrawMode(true);
        _canvas.ApplyPenTool(_selectedPenColor, isHighlighter: false);
        SizeSlider.Value = _canvas.PenWidth;
    }

    private void MouseButton_Click(object sender, RoutedEventArgs e)
    {
        SelectToolButton(MouseButton);
        _canvas.SetDrawMode(false);
    }

    private void HighlighterButton_Click(object sender, RoutedEventArgs e)
    {
        SelectToolButton(HighlighterButton);
        _canvas.SetDrawMode(true);
        _canvas.ApplyPenTool(TeacherDrawCanvasWindow.HighlightColor, isHighlighter: true);
        SizeSlider.Value = _canvas.HighlighterWidth;
    }

    private void EraserButton_Click(object sender, RoutedEventArgs e)
    {
        SelectToolButton(EraserButton);
        _canvas.SetDrawMode(true);
        _canvas.ApplyEraserTool();
        SizeSlider.Value = _canvas.EraserWidth;
    }

    private void ColorButton_Click(Button button, Color color)
    {
        _selectedPenColor = color;
        foreach (var child in ColorButtonsPanel.Children)
        {
            if (child is Button colorButton)
            {
                colorButton.BorderThickness = new Thickness(colorButton == button ? 2 : 1);
            }
        }

        // เลือกสีถือว่าตั้งใจจะใช้ปากกา - สลับไปเครื่องมือปากกาให้เลยถ้ายังไม่ได้เลือกไว้
        SelectToolButton(PencilButton);
        _canvas.SetDrawMode(true);
        _canvas.ApplyPenTool(_selectedPenColor, isHighlighter: false);
    }

    /// <summary>"ปรับขนาดเส้นได้ (ปากกา/ไฮไลต์/ยางลบ)" ตามที่ตัดสินไว้ - ขนาดจะถูกจำแยกคนละชุดต่อเครื่องมือใน TeacherDrawCanvasWindow</summary>
    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvent)
        {
            return;
        }

        var tool = PencilButton.IsChecked == true ? DrawTool.Pen
            : HighlighterButton.IsChecked == true ? DrawTool.Highlighter
            : EraserButton.IsChecked == true ? DrawTool.Eraser
            : (DrawTool?)null;

        if (tool is { } activeTool)
        {
            _canvas.SetToolWidth(activeTool, e.NewValue);
        }
    }

    // ==================== ล้างทั้งหมด ====================

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _canvas.ClearAll();

    // ==================== ซูม ====================

    private void ZoomButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = ZoomButton.IsChecked == true;
        _captureService.ZoomEnabled = enabled;
        ZoomSlider.IsEnabled = enabled;
        ZoomNumberBox.IsEnabled = enabled;

        if (enabled)
        {
            _minimapWindow ??= new TeacherZoomMinimapWindow(_captureService);
            _minimapWindow.Show();
        }
        else
        {
            _captureService.ClearManualPan();
            _minimapWindow?.ForceClose();
            _minimapWindow = null;
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _captureService.ZoomLevel = e.NewValue;

        // บั๊กจริงที่เจอตอนทดสอบด้วย harness input จริง 4 พ.ย.: XAML ตั้ง ZoomSlider Value="3" ตรงๆ ซึ่ง
        // ยิง ValueChanged นี้แบบ synchronous ขณะ parse XAML - ตอนนั้น ZoomNumberBox (อยู่ถัดไปใน markup)
        // ยังไม่ถูกเชื่อม field เลย (เป็น null) ต้องเช็คก่อนใช้
        if (ZoomNumberBox is null)
        {
            return;
        }

        if (!_suppressSliderEvent)
        {
            _suppressSliderEvent = true;
            ZoomNumberBox.Text = e.NewValue.ToString("0.0", CultureInfo.InvariantCulture);
            _suppressSliderEvent = false;
        }
    }

    private void ZoomNumberBox_LostFocus(object sender, RoutedEventArgs e) => ApplyZoomNumberBox();

    private void ZoomNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyZoomNumberBox();
        }
    }

    /// <summary>ช่องกรอกตัวเลขซูม (1-5 เท่า) ตามที่ตัดสินไว้ "slider + ช่องใส่ค่า" - ค่านอกช่วงถูก clamp แทนปฏิเสธ เพื่อไม่ให้ครูต้องกรอกซ้ำ</summary>
    private void ApplyZoomNumberBox()
    {
        if (!double.TryParse(ZoomNumberBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            value = ZoomSlider.Value;
        }

        value = Math.Clamp(value, ZoomSlider.Minimum, ZoomSlider.Maximum);

        _suppressSliderEvent = true;
        ZoomSlider.Value = value;
        _suppressSliderEvent = false;

        ZoomNumberBox.Text = value.ToString("0.0", CultureInfo.InvariantCulture);
        _captureService.ZoomLevel = value;
    }

    public void ForceClose()
    {
        _minimapWindow?.ForceClose();
        _minimapWindow = null;

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
