using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// Website Control (2026-09-11) - แยกจากหน้ากฎโปรแกรม ไม่ปนกฎบล็อกเว็บกับกฎบล็อกโปรแกรม ใช้ global
/// enforcement toggle ตัวเดียวกับ Program Control (ไม่มี toggle แยกในนี้)
///
/// WP6 (2026-10-30): ย้ายจากหน้าต่างลอย (WebsiteControlWindow) มาเป็นหน้าใน shell - ApiClient
/// ส่งเข้ามาทาง <see cref="Initialize"/>, ผลลัพธ์/error ไปเป็น toast, หน้าต่างย่อย (เพิ่ม/แก้กฎ,
/// Bulk Add, Catalog) ใช้หน้าต่างหลักเป็น Owner, และโหลดใหม่ทุกครั้งที่สลับกลับมาหน้านี้
///
/// 2026-09-12: เพิ่ม Bulk Add + Catalog Sources + ค้นหา/กรอง/แบ่งหน้า - เดินตาม pager pattern
/// เดียวกับ Audit Log tab ใน DashboardWindow.xaml.cs (debounce ช่องค้นหา, ก่อนหน้า/ถัดไป)
/// </summary>
public partial class WebsiteControlPage : UserControl
{
    private const int PageSize = 100;

    private ApiClient? _apiClient;
    private readonly DispatcherTimer _searchDebounceTimer;

    private List<WebsiteRuleDto> _currentPageRules = new();
    private List<WebsiteCatalogSourceDto> _catalogSources = new();
    private int _page = 1;
    private int _totalCount;

    private WebsiteCatalogSourcesWindow? _catalogSourcesWindow;

    public WebsiteControlPage()
    {
        InitializeComponent();

        _searchDebounceTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(400) };
        _searchDebounceTimer.Tick += async (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _page = 1;
            await RefreshAsync();
        };

        // โหลดตอน "เห็น" ไม่ใช่ตอน Loaded - เหตุผลเดียวกับ ProgramReviewPage (Loaded ยิงให้ทุกแท็บ
        // ตั้งแต่เปิด Dashboard แม้ยังไม่เคยเลือกแท็บนี้)
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await RefreshAsync();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก DashboardWindow หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    /// <summary>หน้าต่างหลักที่หน้านี้อยู่ - ใช้เป็น Owner ของหน้าต่างย่อยให้จัดกึ่งกลางและซ้อนถูกที่</summary>
    private Window? OwnerWindow => Window.GetWindow(this);

    /// <summary>Tag ของตัวเลือก "พิมพ์เอง" ใน SourceFilterCombo - แยกจาก null ("ทั้งหมด") ให้ชัดเจน</summary>
    private const string ManualFilterTag = "__manual__";

    private async Task RefreshAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        try
        {

            // โหลด source list ก่อนเสมอ (ใช้ทั้ง populate ComboBox และ join ชื่อ source เข้าตาราง)
            _catalogSources = await _apiClient.GetWebsiteCatalogSourcesAsync();
            RebuildSourceFilterCombo();

            var (search, sourceCatalogId, manualOnly) = ReadCurrentFilter();
            var pageResult = await _apiClient.GetWebsiteRulesAsync(search, sourceCatalogId, manualOnly, _page, PageSize);

            _currentPageRules = pageResult.Items;
            foreach (var rule in _currentPageRules)
            {
                rule.SourceText = rule.SourceCatalogId is null
                    ? "พิมพ์เอง"
                    : _catalogSources.FirstOrDefault(s => s.Id == rule.SourceCatalogId)?.Name ?? "Catalog (ลบไปแล้ว)";
            }

            RulesGrid.ItemsSource = _currentPageRules;
            RulesEmptyText.Visibility = _currentPageRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            _totalCount = pageResult.TotalCount;
            var totalPages = System.Math.Max(1, (int)System.Math.Ceiling(_totalCount / (double)PageSize));
            PageInfoText.Text = $"หน้า {pageResult.Page}/{totalPages} ({_totalCount} รายการ)";

        }
        catch (System.Exception ex)
        {
            ToastService.Error($"โหลดกฎเว็บไซต์ไม่สำเร็จ: {ex.Message}");
        }
    }

    /// <summary>สร้าง ComboBox "ทั้งหมด" / "พิมพ์เอง" / รายชื่อ source ทีละตัว - รักษา selection เดิมไว้ถ้ายังมีอยู่</summary>
    private void RebuildSourceFilterCombo()
    {
        var previousTag = (SourceFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        SourceFilterCombo.SelectionChanged -= SourceFilterCombo_SelectionChanged;
        try
        {
            SourceFilterCombo.Items.Clear();
            SourceFilterCombo.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = null });
            SourceFilterCombo.Items.Add(new ComboBoxItem { Content = "พิมพ์เอง", Tag = ManualFilterTag });

            foreach (var source in _catalogSources.OrderBy(s => s.Name))
            {
                SourceFilterCombo.Items.Add(new ComboBoxItem { Content = source.Name, Tag = source.Id.ToString() });
            }

            var toSelect = SourceFilterCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previousTag);
            SourceFilterCombo.SelectedItem = toSelect ?? SourceFilterCombo.Items[0];
        }
        finally
        {
            SourceFilterCombo.SelectionChanged += SourceFilterCombo_SelectionChanged;
        }
    }

    private (string? search, int? sourceCatalogId, bool manualOnly) ReadCurrentFilter()
    {
        var search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();
        var tag = (SourceFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        if (tag == ManualFilterTag)
        {
            return (search, null, true);
        }

        if (int.TryParse(tag, out var sourceId))
        {
            return (search, sourceId, false);
        }

        return (search, null, false);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private async void SourceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _page = 1;
        await RefreshAsync();
    }

    private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        await RefreshAsync();
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = System.Math.Max(1, (int)System.Math.Ceiling(_totalCount / (double)PageSize));
        if (_page >= totalPages) return;
        _page++;
        await RefreshAsync();
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;
        var dialog = new AddEditWebsiteRuleWindow(_apiClient) { Owner = OwnerWindow };
        if (dialog.ShowDialog() == true)
        {
            ToastService.Success("เพิ่มกฎเว็บไซต์แล้ว");
            await RefreshAsync();
        }
    }

    private async void EditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is not null && sender is Button { Tag: WebsiteRuleDto rule })
        {
            var dialog = new AddEditWebsiteRuleWindow(_apiClient, rule) { Owner = OwnerWindow };
            if (dialog.ShowDialog() == true)
            {
                ToastService.Success($"แก้ไขกฎ \"{rule.Name}\" แล้ว");
                await RefreshAsync();
            }
        }
    }

    private async void ToggleRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is not null && sender is Button { Tag: int ruleId })
        {
            var rule = _currentPageRules.FirstOrDefault(r => r.Id == ruleId);
            var success = await _apiClient.ToggleWebsiteRuleAsync(ruleId);
            if (success)
            {
                // บอกผลที่เกิดจริง ไม่ใช่ "Toggle สำเร็จ" ซึ่งไม่บอกว่าตอนนี้เปิดหรือปิด (กติกาตั้งชื่อ §5.1)
                ToastService.Success(rule is null
                    ? "เปลี่ยนสถานะกฎแล้ว"
                    : rule.IsEnabled ? $"ปิดใช้กฎ \"{rule.Name}\" แล้ว" : $"เปิดใช้กฎ \"{rule.Name}\" แล้ว");
            }
            else
            {
                ToastService.Error("เปลี่ยนสถานะกฎไม่สำเร็จ");
            }
            await RefreshAsync();
        }
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is not null && sender is Button { Tag: int ruleId })
        {
            var ruleName = _currentPageRules.FirstOrDefault(r => r.Id == ruleId)?.Name;
            var confirmed = string.IsNullOrWhiteSpace(ruleName)
                ? ConfirmWindow.Show("ยืนยันการลบกฎนี้? การกระทำนี้ย้อนกลับไม่ได้", "ยืนยันการลบ", OwnerWindow)
                : ConfirmWindow.ShowWithTypeToConfirm(
                    $"กำลังจะลบกฎ \"{ruleName}\" ออกจากระบบถาวร การกระทำนี้ย้อนกลับไม่ได้",
                    "ยืนยันการลบกฎ", ruleName, OwnerWindow, "ลบกฎนี้");
            if (!confirmed)
            {
                return;
            }

            var success = await _apiClient.DeleteWebsiteRuleAsync(ruleId);
            if (success)
            {
                ToastService.Success(string.IsNullOrWhiteSpace(ruleName) ? "ลบกฎแล้ว" : $"ลบกฎ \"{ruleName}\" แล้ว");
            }
            else
            {
                ToastService.Error("ลบกฎไม่สำเร็จ");
            }
            await RefreshAsync();
        }
    }

    private async void BulkAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;
        var dialog = new BulkAddWebsiteRulesWindow(_apiClient) { Owner = OwnerWindow };
        if (dialog.ShowDialog() == true)
        {
            ToastService.Success(dialog.ResultText);
            await RefreshAsync();
        }
    }

    /// <summary>เก็บ reference ไว้กันเปิดซ้อนหลายบาน (ครูมักกดปุ่มซ้ำตอนหน้าต่างยังโหลดไม่เสร็จ)</summary>
    private void ManageCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;
        if (_catalogSourcesWindow is { IsLoaded: true })
        {
            _catalogSourcesWindow.Activate();
            return;
        }

        _catalogSourcesWindow = new WebsiteCatalogSourcesWindow(_apiClient) { Owner = OwnerWindow };
        _catalogSourcesWindow.Closed += async (_, _) =>
        {
            _catalogSourcesWindow = null;
            // source อาจถูกเพิ่ม/ลบ/sync ระหว่างที่หน้าต่างนั้นเปิดอยู่ - รีเฟรชตารางหลักให้ตรงเสมอ
            await RefreshAsync();
        };
        _catalogSourcesWindow.Show();
    }
}
