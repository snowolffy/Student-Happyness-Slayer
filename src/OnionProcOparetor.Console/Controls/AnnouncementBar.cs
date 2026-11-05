using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using OnionProcOparetor.Console.Services;
using OnionProcOparetor.UI.Controls;
using OnionProcOparetor.UI.Services;

namespace OnionProcOparetor.Console.Controls;

/// <summary>
/// แถบหัวของหน้า + ประกาศสถานะของทั้งระบบ (WP6)
///
/// กติกาที่ห้ามแหก: **สูง 60px เท่าเดิมทุกกรณี** ไม่ว่าจะมีประกาศ 0 / 1 / 5 เรื่อง สิ่งที่เปลี่ยนมีแค่
/// สีพื้น ข้อความ และปุ่มแก้ - ของเดิม (GlobalOffBanner) เป็นแถบแยกที่ Collapsed ไว้ใต้แถบหัว พอ gate
/// ถูกปิด แถบนั้นแทรกเข้ามาแล้วดันตารางทั้งหน้าลง ~60px คลิกที่เล็งไว้จึงไปโดนแถวอื่น (บั๊กตระกูลเดียวกับ
/// แถบเลือกเครื่องที่แก้ไปเมื่อ 2026-09-10 - console-ui-notes §5)
///
/// หลายเรื่องพร้อมกัน = โชว์เรื่องที่ร้ายแรงที่สุด + ปุ่ม "+N" ที่กดดูที่เหลือได้ (แถบสูงคงที่ จะเรียง
/// ทุกเรื่องลงมาไม่ได้ และไม่ควรได้ด้วย)
///
/// **ทำไมสร้าง visual tree ในโค้ด ไม่ใช่ UserControl + XAML:** UserControl ที่มี XAML ของตัวเองมี
/// namescope ของตัวเอง ทำให้ element ที่หน้าต่างเจ้าของส่งเข้ามาทาง <see cref="NormalContent"/> /
/// <see cref="TrailingContent"/> ตั้ง x:Name ไม่ได้ (MC3093) แต่ DashboardWindow ต้องเรียก
/// ServerLabel / StatusText / RefreshButton ด้วยชื่อ - คลาสที่ไม่มี XAML ไม่สร้าง namescope ชื่อเหล่านั้น
/// จึงไปลงทะเบียนกับหน้าต่างตามปกติ (เหตุผลเดียวกับ Nav.TopBar - ดู Controls/Nav.cs)
///
/// สีทั้งหมดตั้งด้วย <see cref="FrameworkElement.SetResourceReference"/> ไม่ใช่ FindResource เพราะต้อง
/// ตามธีมสว่าง/มืดได้ตอนสลับธีมกลางคัน (console-ui-notes §1.1)
/// </summary>
public sealed class AnnouncementBar : UserControl
{
    public const double BarHeight = 60;

    public static readonly DependencyProperty NormalContentProperty = DependencyProperty.Register(
        nameof(NormalContent), typeof(object), typeof(AnnouncementBar), new PropertyMetadata(null));

    /// <summary>สิ่งที่โชว์ตอนไม่มีประกาศ (ปลายทาง Server, เวลาอัพเดตล่าสุด)</summary>
    public object? NormalContent
    {
        get => GetValue(NormalContentProperty);
        set => SetValue(NormalContentProperty, value);
    }

    public static readonly DependencyProperty TrailingContentProperty = DependencyProperty.Register(
        nameof(TrailingContent), typeof(object), typeof(AnnouncementBar), new PropertyMetadata(null));

    /// <summary>สิ่งที่อยู่ขวาสุดตลอดเวลาไม่ว่าจะมีประกาศหรือไม่ (ปุ่มรีเฟรช)</summary>
    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    private readonly Border _root;
    private readonly ContentPresenter _normalPresenter;
    private readonly Grid _announcePanel;
    private readonly IconPresenter _announceIcon;
    private readonly TextBlock _announceTitle;
    private readonly Button _fixButton;
    private readonly Button _moreButton;
    private readonly Popup _morePopup;
    private readonly StackPanel _moreList;

    private AnnouncementCenter? _center;
    private Announcement? _shownTop;

    public AnnouncementBar()
    {
        _normalPresenter = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
        _normalPresenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(NormalContent)) { Source = this });

        // ---- สภาพมีประกาศ: ไอคอน + หัวเรื่องบรรทัดเดียว + ปุ่มแก้ + ปุ่ม +N ----
        _announceIcon = new IconPresenter { Icon = "alert-triangle", IconSize = 20, VerticalAlignment = VerticalAlignment.Center };

        // หัวเรื่องตัดด้วย … ไม่ห่อบรรทัด (แถบสูงคงที่) ข้อความเต็ม + รายละเอียดอยู่ใน ToolTip
        _announceTitle = new TextBlock
        {
            Margin = new Thickness(10, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 13,
            FontWeight = FontWeights.Bold
        };
        _announceTitle.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Primary");

        _fixButton = new Button { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _fixButton.Click += (_, _) => _shownTop?.Fix?.Invoke();

        _moreButton = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "ดูประกาศเรื่องอื่นที่เกิดอยู่พร้อมกัน"
        };
        _moreButton.Click += MoreButton_Click;

        _announcePanel = new Grid { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent };
        _announcePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _announcePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _announcePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _announcePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToGrid(_announcePanel, _announceIcon, 0);
        AddToGrid(_announcePanel, _announceTitle, 1);
        AddToGrid(_announcePanel, _fixButton, 2);
        AddToGrid(_announcePanel, _moreButton, 3);

        // ---- ของที่อยู่ขวาสุดตลอดเวลา (ปุ่มรีเฟรช) - ประกาศจะไม่ไปแย่งที่มัน ----
        var trailing = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
        trailing.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(TrailingContent)) { Source = this });

        // ---- popup "+N" ----
        _moreList = new StackPanel();
        var popupTitle = new TextBlock { Text = "ประกาศที่เกิดอยู่ตอนนี้", Margin = new Thickness(0, 0, 0, 8) };
        popupTitle.SetResourceReference(StyleProperty, "Label");
        var popupStack = new StackPanel();
        popupStack.Children.Add(popupTitle);
        popupStack.Children.Add(_moreList);

        var popupCard = new Border
        {
            Width = 420,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 12, 12),
            BorderThickness = new Thickness(1),
            Child = popupStack,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.22, Color = System.Windows.Media.Colors.Black }
        };
        popupCard.SetResourceReference(Border.BackgroundProperty, "Brush.Background.CardRaised");
        popupCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Border.Strong");

        _morePopup = new Popup
        {
            PlacementTarget = _moreButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            VerticalOffset = 6,
            Child = popupCard
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToGrid(layout, _normalPresenter, 0);
        AddToGrid(layout, _announcePanel, 0);
        AddToGrid(layout, trailing, 1);
        layout.Children.Add(_morePopup);

        // UI-8 (2026-11-04): เดิมมีมุมโค้ง (12) + กรอบรอบด้าน ทำให้ดูเป็น "การ์ดลอย" เหนือเนื้อหา - roadmap
        // สั่งให้แถบนี้ชิดขอบบน/ซ้าย/ขวาของหน้าต่างเต็มแทน ไม่มีมุมโค้ง ไม่มีเงา คั่นจากเนื้อหาด้วยเส้น 1px
        // อย่างเดียว จึงเหลือ CornerRadius เป็น 0 และ BorderThickness เป็นเฉพาะขอบล่าง (ไม่มี Effect อยู่แล้ว
        // จึงไม่ต้องแก้เรื่องเงา) - สีของ BorderBrush ตอนปกติ (Render() ด้านล่าง: Brush.Border.Default) คือ
        // เส้นคั่นที่เห็น ส่วนตอนมีประกาศ (สีพื้นทึบ) BorderBrush ถูกตั้งให้เท่ากับ Background เสมอ เส้นจึง
        // กลืนไปกับพื้นสีนั้นพอดี ไม่โผล่เป็นเส้นแปลกปลอม
        _root = new Border
        {
            Height = BarHeight,
            Padding = new Thickness(16, 0, 16, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = layout
        };

        Content = _root;
        Render();
    }

    private static void AddToGrid(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>ศูนย์ประกาศที่แถบนี้แสดง - ตั้งครั้งเดียวจากหน้าต่างเจ้าของ</summary>
    public AnnouncementCenter? Center
    {
        get => _center;
        set
        {
            if (_center is not null)
            {
                _center.Changed -= Render;
            }

            _center = value;

            if (_center is not null)
            {
                _center.Changed += Render;
            }

            Render();
        }
    }

    /// <summary>ชื่อ resource ของสีแต่ละส่วนตามความร้ายแรง</summary>
    private static (string Background, string Border, string Foreground, string ButtonStyle) PaletteOf(AnnouncementSeverity severity) =>
        severity switch
        {
            // แดงทึบ = "ระบบใช้งานไม่ได้อยู่" ใช้ Danger.Fill ไม่ใช่ Danger.Default เพราะตัวหลังในธีมมืด
            // สว่างเกินกว่าจะรองรับตัวหนังสือขาว (console-ui-notes §5)
            AnnouncementSeverity.Danger => ("Brush.Danger.Fill", "Brush.Danger.Fill", "Brush.Text.OnAccent", "ButtonOnStatus"),

            // เหลือง = สีเดียวกับแถบเตือน gate ปิดเดิม ตัวหนังสือต้องเป็น Text.OnStatusFill ห้าม Text.Primary
            // (ในธีมมืด Text.Primary คือขาว = ขาวบนเหลือง อ่านไม่ออก - บั๊กที่เคยเกิดมาแล้วสองครั้ง)
            AnnouncementSeverity.Warning => ("Brush.Status.Warning", "Brush.Status.Warning", "Brush.Text.OnStatusFill", "ButtonOnStatus"),

            // ข้อมูล = พื้นส้มโปร่งแบบเดียวกับแถบเลือกเครื่อง ไม่ใช่สีทึบ เพราะไม่ใช่เรื่องที่ต้องรีบแก้
            _ => ("Brush.Badge.Accent.Background", "Brush.Accent.Default", "Brush.Accent.Text", "ButtonSecondary")
        };

    private void Render()
    {
        var list = _center?.Ordered ?? [];

        // UI-13: ไม่มีประกาศ = ทั้งแถบ Collapsed (สูง 0 ไม่โชว์อะไร) - ข้อความ Server/เวลา/รีเฟรชย้ายไปแถบสถานะล่างแล้ว
        // แถบโผล่เฉพาะตอนมีประกาศจริง (เนื้อหาด้านล่างเลื่อนลงตอนโผล่ - ผู้ใช้ยอมรับแล้ว)
        // UI-15: โผล่/หายด้วย height + opacity (Motion.NormalMs) - ระหว่างหายยังคงหน้าตาประกาศเดิมไว้จนจบ ไม่ให้
        // เห็นแถบขาวว่างวาบก่อนหด · เล่นเฉพาะตอน "สถานะโผล่/หาย" เปลี่ยนจริง การสลับประกาศกันเองไม่เล่นอะไร
        if (list.Count == 0)
        {
            _shownTop = null;
            _morePopup.IsOpen = false;

            // Render ถูกเรียกซ้ำได้ระหว่างกำลังหาย - ปล่อยให้ fade เดิมเล่นต่อจนจบ ไม่ตัดกลางคัน
            if (_hiding)
            {
                return;
            }

            if (_barShown && Motion.Enabled && IsLoaded)
            {
                _barShown = false;
                HideAnimated();
                return;
            }

            _barShown = false;
            ApplyIdleLook();
            Visibility = Visibility.Collapsed;
            ResetBarAnimation();
            return;
        }

        var wasShown = _barShown;
        _barShown = true;
        _hiding = false;
        _barToken++;
        Visibility = Visibility.Visible;
        if (!wasShown && Motion.Enabled && IsLoaded)
        {
            ShowAnimated();
        }
        else
        {
            ResetBarAnimation();
        }

        var top = list[0];
        _shownTop = top;
        var palette = PaletteOf(top.Severity);

        // สลับแบบ Collapsed ได้เพราะทั้งสองอยู่ใน cell เดียวกัน และ _root สูงคงที่ - ไม่มีอะไรขยับ
        _normalPresenter.Visibility = Visibility.Collapsed;
        _announcePanel.Visibility = Visibility.Visible;

        _root.SetResourceReference(Border.BackgroundProperty, palette.Background);
        _root.SetResourceReference(Border.BorderBrushProperty, palette.Border);
        _announceTitle.SetResourceReference(TextBlock.ForegroundProperty, palette.Foreground);
        _announceIcon.SetResourceReference(Control.ForegroundProperty, palette.Foreground);
        _announceIcon.Icon = top.Severity == AnnouncementSeverity.Info ? "info" : "alert-triangle";

        _announceTitle.Text = top.Title;
        _announcePanel.ToolTip = string.IsNullOrWhiteSpace(top.Detail) ? top.Title : $"{top.Title}\n{top.Detail}";

        _fixButton.Visibility = top.HasFix ? Visibility.Visible : Visibility.Collapsed;
        _fixButton.Content = top.FixText;
        _fixButton.SetResourceReference(StyleProperty, palette.ButtonStyle);
        _fixButton.Padding = new Thickness(14, 7, 14, 7);

        var others = list.Count - 1;
        _moreButton.Visibility = others > 0 ? Visibility.Visible : Visibility.Collapsed;
        _moreButton.Content = $"+{others}";
        _moreButton.SetResourceReference(StyleProperty, palette.ButtonStyle);
        _moreButton.Padding = new Thickness(10, 7, 10, 7);

        if (others == 0)
        {
            _morePopup.IsOpen = false;
        }
        else if (_morePopup.IsOpen)
        {
            BuildMoreList(list);
        }
    }

    private bool _barShown;
    private bool _hiding;
    private int _barToken;

    private void ApplyIdleLook()
    {
        _normalPresenter.Visibility = Visibility.Visible;
        _announcePanel.Visibility = Visibility.Collapsed;
        _root.SetResourceReference(Border.BackgroundProperty, "Brush.Background.Card");
        _root.SetResourceReference(Border.BorderBrushProperty, "Brush.Border.Default");
    }

    /// <summary>ยกเลิก animation ค้าง - Height กลับเป็น BarHeight และ Opacity 1 (ค่าจริงของแถบ)</summary>
    private void ResetBarAnimation()
    {
        _root.BeginAnimation(HeightProperty, null);
        _root.Height = BarHeight;
        Motion.ResetOpacity(_root);
    }

    private void ShowAnimated()
    {
        _root.BeginAnimation(HeightProperty, null);
        _root.Height = BarHeight;
        _root.BeginAnimation(HeightProperty, Motion.Animation(0, BarHeight));
        Motion.FadeIn(_root);
    }

    private void HideAnimated()
    {
        var token = ++_barToken;
        _hiding = true;
        _root.BeginAnimation(HeightProperty, Motion.Animation(BarHeight, 0, hold: true));
        Motion.FadeOut(_root, () =>
        {
            // ถ้ามีประกาศกลับมาระหว่างหาย (token เปลี่ยน) ห้ามซ่อน/รีเซ็ตทับแถบที่เพิ่งโผล่อีกรอบ
            if (token != _barToken || _barShown)
            {
                return;
            }

            _hiding = false;
            ApplyIdleLook();
            Visibility = Visibility.Collapsed;
            ResetBarAnimation();
        });
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_center is null)
        {
            return;
        }

        BuildMoreList(_center.Ordered);
        _morePopup.IsOpen = true;
    }

    /// <summary>
    /// รายการใน popup "+N" = ทุกเรื่องที่ไม่ได้ขึ้นแถบหัว แต่ละเรื่องมีปุ่มแก้ของตัวเอง
    /// (ถ้าต้องปิด popup ไปแก้เรื่องแรกก่อนถึงจะกดแก้เรื่องที่สองได้ ก็เท่ากับซ่อนทางแก้ไว้อีกชั้น)
    /// </summary>
    private void BuildMoreList(IReadOnlyList<Announcement> list)
    {
        _moreList.Children.Clear();

        foreach (var item in list.Skip(1))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(Shape.FillProperty, item.Severity switch
            {
                AnnouncementSeverity.Danger => "Brush.Danger.Default",
                AnnouncementSeverity.Warning => "Brush.Status.WarningText",
                _ => "Brush.Accent.Default"
            });
            AddToGrid(row, dot, 0);

            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Primary");
            text.Inlines.Add(new Run(item.Title) { FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(item.Detail))
            {
                var detail = new Run("\n" + item.Detail) { FontSize = 12 };
                detail.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text.Secondary");
                text.Inlines.Add(detail);
            }
            AddToGrid(row, text, 1);

            if (item.HasFix)
            {
                var fix = new Button
                {
                    Content = item.FixText,
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                fix.SetResourceReference(StyleProperty, "ButtonSecondary");
                var captured = item;
                fix.Click += (_, _) =>
                {
                    _morePopup.IsOpen = false;
                    captured.Fix?.Invoke();
                };
                AddToGrid(row, fix, 2);
            }

            _moreList.Children.Add(row);
        }
    }
}
