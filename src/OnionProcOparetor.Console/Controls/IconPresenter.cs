using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

using Path = System.Windows.Shapes.Path;

namespace OnionProcOparetor.Console.Controls;

/// <summary>
/// วาดไอคอนเส้นหนึ่งตัวจากชุดใน Theme/Icons.xaml (ซึ่ง port มาจาก Icon.razor ของ OnlineConsole)
/// ใช้แทน emoji / ตัวอักษร Segoe MDL2 ที่เคยใช้ - เพื่อให้ Console กับ OnlineConsole หน้าตาเหมือนกัน
///
/// ใช้งาน: <c>&lt;controls:IconPresenter Icon="file-text" IconSize="20"/&gt;</c>
/// - <see cref="Icon"/> รับ "ชื่อแบบเดียวกับใน Icon.razor" (kebab-case เช่น monitor, file-text,
///   alert-triangle) แล้วแปลงเป็น resource key เองเป็น Icon.Monitor / Icon.FileText /
///   Icon.AlertTriangle (ถ้าส่ง key เต็มที่ขึ้นต้นด้วย "Icon." มาเลยก็รับ ใช้ตรง ๆ ไม่แปลง)
/// - สีเส้นตาม Foreground ของตัว control เอง ซึ่ง inherit ลงมาจาก parent เหมือน currentColor ใน SVG
///   เอาไปวางใน Button/TextBlock ที่ไหนก็ได้สีตามข้อความตรงนั้น ไม่ต้องตั้งสีซ้ำ
/// - หา key ไม่เจอ = ไม่วาดอะไร (ไม่ throw) แต่ยิง Debug.WriteLine ไว้ให้เห็นตอน debug
/// </summary>
public class IconPresenter : Control
{
    /// <summary>ขนาด canvas ที่ geometry ทุกตัวใน Icons.xaml ถูกวาดไว้ - ตรงกับ viewBox="0 0 24 24" ของ SVG</summary>
    private const double IconCanvasSize = 24d;

    private readonly Path _path;
    private readonly Canvas _canvas;
    private readonly Viewbox _viewbox;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(string),
        typeof(IconPresenter),
        new FrameworkPropertyMetadata(string.Empty, OnIconChanged));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(IconPresenter),
        new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnIconSizeChanged));

    /// <summary>ชื่อไอคอนแบบเดียวกับ <c>Name</c> ของ Icon.razor เช่น "monitor", "file-text", "log-out"</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>ความกว้าง/สูงของไอคอนเป็น device-independent pixel (ค่าเริ่มต้น 20)</summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IconPresenter()
    {
        _path = new Path
        {
            // Stretch=None สำคัญ: ต้องวาด geometry ที่พิกัดจริงบน canvas 24 หน่วย ไม่ใช่ให้ WPF ยืดให้พอดี
            // bounding box ของตัวมันเอง (ไอคอนแต่ละตัวกินพื้นที่ไม่เท่ากัน เช่น sun กิน 1..23 แต่ monitor
            // กิน 2..22 - ถ้าปล่อยให้ยืด แต่ละตัวจะโดนสเกลคนละอัตรา แล้วชุดไอคอนจะขนาดไม่เท่ากันทันที)
            Stretch = Stretch.None,
            Fill = null,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        // ผูกสีเส้นกับ Foreground ของ control (ซึ่ง inherit มาจาก parent) = currentColor ของ SVG
        _path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });

        // Canvas ขนาดคงที่ 24x24 คั่นระหว่าง Path กับ Viewbox: Viewbox สเกลจาก DesiredSize ของลูก
        // ถ้าเอา Path ใส่ Viewbox ตรง ๆ DesiredSize จะเป็น bounding box ของไอคอนตัวนั้น ๆ ไม่ใช่ 24x24
        // ผลคือไอคอนแต่ละตัวถูกซูมคนละอัตราจนดูขนาดไม่เท่ากัน - Canvas ตัวนี้กันปัญหานั้น
        _canvas = new Canvas
        {
            Width = IconCanvasSize,
            Height = IconCanvasSize,
            Background = null,
        };
        _canvas.Children.Add(_path);

        _viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _canvas,
            // เส้นที่หนาขึ้นตอนไอคอนเล็กอาจล้นกรอบ 24 หน่วยไปเสี้ยวพิกเซล - อย่าเผลอ clip ทิ้ง
            ClipToBounds = false,
        };

        AddVisualChild(_viewbox);
        AddLogicalChild(_viewbox);

        // ไม่ใช้ ControlTemplate เลย (ประกอบ visual tree เองด้านบน) - จะได้ไม่ต้องพึ่ง default style
        // จาก generic.xaml หรือ implicit style ใน App.xaml ซึ่งถ้าลืม merge เข้าไปไอคอนจะหายทั้งแอปเงียบ ๆ
        OverridesDefaultStyle = true;
        Template = null;

        Focusable = false;
        IsTabStop = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        UpdateStrokeThickness();
        Loaded += OnLoaded;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _viewbox : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _viewbox.Measure(availableSize);
        return _viewbox.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewbox.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        UpdateGeometry();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ตอน constructor/Initialized ยังหา resource ไม่เจอได้ (ตัว control ยังไม่ถูกต่อเข้า tree
        // หรือ Application.Resources ยัง merge ไม่เสร็จ) - ลองใหม่อีกรอบตอน Loaded
        if (_path.Data is null)
        {
            UpdateGeometry();
        }
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((IconPresenter)d).UpdateGeometry();

    private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((IconPresenter)d).UpdateStrokeThickness();

    /// <summary>
    /// จุดสำคัญที่สุดของ control ตัวนี้: geometry ถูกวาดบน canvas 24 หน่วย แล้วถูก Viewbox ย่อ/ขยาย
    /// ไปเป็น <see cref="IconSize"/> ซึ่ง "เส้นก็โดนสเกลไปด้วย" - ปล่อยไว้แบบนั้น ไอคอน 20px จะได้เส้น
    /// 1.67px ส่วนไอคอน 32px จะได้เส้น 2.67px หนาเทอะทะ (นี่คือสาเหตุที่ชุดไอคอนที่ขยายมาแล้วดูเงอะงะ)
    ///
    /// เลยชดเชยกลับ: อยากได้เส้นหนาจริงบนจอ = base ก็ตั้ง StrokeThickness = base * 24 / IconSize
    /// เพราะสุดท้ายมันจะถูกคูณด้วยอัตราส่วน IconSize/24 ของ Viewbox แล้วหักล้างกันพอดี
    /// base = 2.0 ปกติ และ 1.75 เมื่อ IconSize >= 28 (กติกาเดียวกับที่ Icon.razor ใช้ - เส้น 2px ที่คม
    /// กำลังดีตอน 20px จะดูหนาเหมือนปากกาเมจิกตอน 32px) ต้องคำนวณใหม่ทุกครั้งที่ IconSize เปลี่ยน
    /// </summary>
    private void UpdateStrokeThickness()
    {
        var size = IconSize > 0d ? IconSize : 20d;
        var baseThickness = size >= 28d ? 1.75d : 2.0d;

        _path.StrokeThickness = baseThickness * IconCanvasSize / size;
        _viewbox.Width = size;
        _viewbox.Height = size;
    }

    private void UpdateGeometry()
    {
        var key = ResolveResourceKey(Icon);
        if (key.Length == 0)
        {
            _path.Data = null;
            return;
        }

        var resource = TryFindResource(key) ?? Application.Current?.TryFindResource(key);
        if (resource is Geometry geometry)
        {
            // freeze ไว้ - geometry ใน Icons.xaml เป็นของ static ที่ถูกใช้ร่วมกันหลายที่ วาดเร็วขึ้นและ
            // กันไม่ให้ใครเผลอไปแก้ instance ที่ทุก IconPresenter ใช้ร่วมกันอยู่
            if (geometry.CanFreeze && !geometry.IsFrozen)
            {
                geometry.Freeze();
            }

            _path.Data = geometry;
            return;
        }

        // หาไม่เจอ = ไม่วาดอะไร ไม่ throw (ไอคอนหายหนึ่งตัวไม่ควรทำให้ทั้งหน้าต่างพัง) แต่ต้องเห็นตอน debug
        _path.Data = null;
        Debug.WriteLine(
            $"[IconPresenter] หา geometry ไม่เจอ: Icon=\"{Icon}\" -> resource key \"{key}\" " +
            "(merge Theme/Icons.xaml เข้า App.xaml แล้วหรือยัง / สะกดชื่อตรงกับใน Icon.razor หรือเปล่า)");
    }

    /// <summary>
    /// "file-text" -> "Icon.FileText", "monitor" -> "Icon.Monitor", "x" -> "Icon.X"
    /// ถ้าส่งมาเป็น key เต็ม ("Icon.FileText") อยู่แล้วก็คืนค่าเดิม
    /// </summary>
    private static string ResolveResourceKey(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return string.Empty;
        }

        var name = icon.Trim();
        if (name.StartsWith("Icon.", StringComparison.Ordinal))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 5);
        builder.Append("Icon.");

        var upperNext = true;
        foreach (var ch in name)
        {
            if (ch is '-' or '_' or ' ')
            {
                upperNext = true;
                continue;
            }

            builder.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }

        return builder.ToString();
    }
}
