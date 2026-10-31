using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using OnionProcOparetor.RosterManager.Models;

namespace OnionProcOparetor.RosterManager;

public partial class PrintSlipsWindow : Window
{
    // A4 ที่ 96 DPI (8.27in x 11.69in) - margin 40px ทุกด้าน
    private const double PageWidth = 793.92;
    private const double PageHeight = 1122.24;
    private const double PageMargin = 40;

    private readonly List<PasswordSlipDto> _slips;

    public PrintSlipsWindow(List<PasswordSlipDto> slips)
    {
        InitializeComponent();
        _slips = slips
            .OrderBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StudentCode, StringComparer.Ordinal)
            .ToList();

        SummaryText.Text = $"ใบรหัสผ่านทั้งหมด {_slips.Count} ใบ";
        RebuildPreview(this, new RoutedEventArgs());
    }

    private int PerPage => int.Parse((string)((ComboBoxItem)PerPageCombo.SelectedItem).Tag);

    private void PerPageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildPreview(sender, e);

    /// <summary>แบ่งใบรหัสเป็นหน้า A4 - ถ้า "ขึ้นหน้าใหม่เมื่อเปลี่ยนห้อง" ติ๊กไว้ จะไม่ให้สองห้อง
    /// ปนกันในหน้าเดียว (หน้าสุดท้ายของห้องหนึ่งอาจมีที่เหลือว่าง - ยอมรับได้ ตัดกระดาษง่ายกว่าห้องปน)</summary>
    private List<List<PasswordSlipDto>> BuildPages()
    {
        var perPage = PerPage;
        var pages = new List<List<PasswordSlipDto>>();

        if (NewPagePerClassCheckBox.IsChecked == true)
        {
            foreach (var group in _slips.GroupBy(s => s.ClassName))
            {
                var buffer = group.ToList();
                for (var i = 0; i < buffer.Count; i += perPage)
                {
                    pages.Add(buffer.Skip(i).Take(perPage).ToList());
                }
            }
        }
        else
        {
            for (var i = 0; i < _slips.Count; i += perPage)
            {
                pages.Add(_slips.Skip(i).Take(perPage).ToList());
            }
        }

        return pages.Count == 0 ? new List<List<PasswordSlipDto>> { new() } : pages;
    }

    private void RebuildPreview(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && sender != this)
        {
            return;
        }

        var pages = BuildPages();
        var firstPage = BuildPageVisual(pages[0], 1, pages.Count);
        PreviewHost.Width = PageWidth;
        PreviewHost.Height = PageHeight;
        PreviewHost.Child = firstPage;
    }

    private FrameworkElement BuildPageVisual(List<PasswordSlipDto> pageSlips, int pageNumber, int totalPages)
    {
        var classLabel = pageSlips.Select(s => s.ClassName).Distinct().Count() == 1
            ? pageSlips[0].ClassName
            : "หลายห้อง";

        var root = new Grid { Width = PageWidth, Height = PageHeight, Background = Brushes.White };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(PageMargin, PageMargin, PageMargin, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "โรงเรียน · ห้องคอมพิวเตอร์", FontSize = 11, Foreground = Brushes.Gray });
        var pageLabel = new TextBlock { Text = $"{classLabel} · หน้า {pageNumber}/{totalPages}", FontSize = 11, Foreground = Brushes.Gray };
        Grid.SetColumn(pageLabel, 1);
        header.Children.Add(pageLabel);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var grid = new UniformGrid { Columns = PerPage == 16 ? 4 : 2, Margin = new Thickness(PageMargin, 0, PageMargin, 0) };
        foreach (var slip in pageSlips)
        {
            grid.Children.Add(BuildSlipVisual(slip));
        }
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var footer = new TextBlock
        {
            Text = "ตัดตามเส้นประ",
            FontSize = 9,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private FrameworkElement BuildSlipVisual(PasswordSlipDto slip)
    {
        var stack = new StackPanel { Margin = new Thickness(4) };
        stack.Children.Add(new TextBlock { Text = slip.FullName, FontSize = 12, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = $"รหัสนักเรียน {slip.StudentCode} · {slip.ClassName}",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(slip.SchoolEmail)
                ? $"ล็อกอินด้วย: {slip.StudentCode}"
                : $"ล็อกอินด้วย: {slip.StudentCode} หรือ {slip.SchoolEmail}",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var pwRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        pwRow.Children.Add(new TextBlock { Text = "รหัสผ่านเริ่มต้น ", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        pwRow.Children.Add(new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = slip.Password,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            }
        });
        stack.Children.Add(pwRow);

        stack.Children.Add(new TextBlock
        {
            Text = FooterTextBox.Text,
            FontSize = 10,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(2),
            Child = stack
        };
    }

    /// <summary>ทั้งปุ่ม "บันทึกเป็น PDF" และ "พิมพ์" เรียกเมธอดนี้เหมือนกัน - เปิด PrintDialog ของ Windows
    /// ปกติ ถ้าครูเลือกเครื่องพิมพ์ "Microsoft Print to PDF" ตัวระบบจะถามตำแหน่งบันทึกไฟล์ให้เอง
    /// ตรงตาม docs/roadmap.md "บันทึกเป็น PDF ได้ผ่าน Microsoft Print to PDF" - ไม่มี PDF writer
    /// ของตัวเองแยกต่างหาก</summary>
    private void PrintOrSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var printDialog = new System.Windows.Controls.PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                return;
            }

            var pages = BuildPages();
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(PageWidth, PageHeight);

            for (var i = 0; i < pages.Count; i++)
            {
                var pageContent = new PageContent();
                var fixedPage = new FixedPage { Width = PageWidth, Height = PageHeight };
                fixedPage.Children.Add(BuildPageVisual(pages[i], i + 1, pages.Count));
                ((IAddChild)pageContent).AddChild(fixedPage);
                document.Pages.Add(pageContent);
            }

            printDialog.PrintDocument(document.DocumentPaginator, "ใบรหัสผ่านนักเรียน");
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
            StatusText.Text = $"ส่งพิมพ์แล้ว ({pages.Count} หน้า)";
        }
        catch (Exception ex)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = $"พิมพ์ไม่สำเร็จ: {ex.Message}";
        }
    }
}
