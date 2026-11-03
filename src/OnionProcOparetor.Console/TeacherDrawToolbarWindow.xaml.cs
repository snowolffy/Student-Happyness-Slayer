using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// คำขอเพิ่ม 3 พ.ย. 2569 - แผงเครื่องมือเขียน (ดู doc comment ใน .xaml) ลอยมุมขวาบนของจอ ลากย้ายได้
/// เหมือนปุ่มกลมตอนหุบของ BroadcastWarningBarWindow เจ้าของอายุ: BroadcastWarningBarWindow
/// </summary>
public partial class TeacherDrawToolbarWindow : Window
{
    private readonly TeacherDrawCanvasWindow _canvas;
    private bool _allowClose;

    public TeacherDrawToolbarWindow(TeacherDrawCanvasWindow canvas)
    {
        InitializeComponent();
        _canvas = canvas;

        var insertIndex = 0;
        foreach (var color in TeacherDrawCanvasWindow.PenColors)
        {
            var button = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) => _canvas.ApplyPenTool(color, isHighlighter: false);
            ButtonsPanel.Children.Insert(insertIndex++, button);
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 420;
        Top = workArea.Top + 16;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button) // ลากได้แค่จากพื้นหลัง ไม่ใช่จากปุ่ม (ปุ่มต้องกดได้ปกติ)
            {
                DragMove();
                // กันลากแผงออกนอกขอบจอทำงานจนหาไม่เจอ/กดไม่ถึง เหมือนบั๊กที่เจอกับปุ่มกลมของ
                // BroadcastWarningBarWindow (ดู ClampToWorkArea ที่นั่น) - แผงนี้มีความเสี่ยงเดียวกัน
                // เพราะลากได้อิสระและ DragMove ไม่เช็คขอบจอเองเลย
                ClampToWorkArea();
            }
        };

        SourceInitialized += (_, _) =>
        {
            var excluded = TeacherOnlyWindowHelper.ExcludeFromCapture(this);
            ConsoleDiagnostics.WriteLine($"[teacher-draw-toolbar] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) = {excluded}");

            // มุมโค้งจาก window region แทนความโปร่งใส - ดู TeacherOnlyWindowHelper.ApplyRoundedRegion
            SizeChanged += (_, _) => TeacherOnlyWindowHelper.ApplyRoundedRegion(this, 10);
        };
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

    private void HighlighterButton_Click(object sender, RoutedEventArgs e) =>
        _canvas.ApplyPenTool(TeacherDrawCanvasWindow.HighlightColor, isHighlighter: true);

    private void EraserButton_Click(object sender, RoutedEventArgs e) => _canvas.ApplyEraserTool();

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _canvas.ClearAll();

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
