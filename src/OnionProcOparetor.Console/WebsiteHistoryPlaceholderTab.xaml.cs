using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// แท็บ "ประวัติเว็บ" ของหน้า Website Control (WP2-4, 2026-10-31) - อ่าน/กรอง/สรุปเว็บที่เข้าบ่อย จาก
/// ประวัติที่ Agent ส่งขึ้น Server ทุก 1 นาที (ดู Server/Endpoints/WebVisitEndpoints.cs) คอลัมน์
/// "ห้องเรียน" (ComboBox) สร้างจากค่า StudentClassName ที่เจอจริงในหน้าปัจจุบันเท่านั้น (ไม่มี endpoint
/// "distinct class names" แยกในรอบนี้ - ยอมรับได้เพราะเป็นตัวกรองช่วยดู ไม่ใช่ตัวกรองที่ critical)
///
/// เดินตาม pager/debounce pattern เดียวกับ WebsiteBlockTab (ดูไฟล์นั้นสำหรับที่มาของ pattern)
/// </summary>
public partial class WebsiteHistoryPlaceholderTab : UserControl
{
    private const int PageSize = 200;

    private ApiClient? _apiClient;
    private readonly DispatcherTimer _searchDebounceTimer;

    private List<WebVisitDto> _currentPageVisits = new();
    private int _page = 1;
    private int _totalCount;

    public WebsiteHistoryPlaceholderTab()
    {
        InitializeComponent();

        // ค่าเริ่มต้น: วันนี้เป็นต้นไป (ไม่จำกัดเวลาสูงสุด = ถึงตอนนี้) - ตรงกับความต้องการปกติที่สุด
        // "ดูว่าเด็กเข้าเว็บอะไรวันนี้" ตัวกรองยังแก้เป็นช่วงอื่นได้เสมอ
        FromDatePicker.SelectedDate = DateTime.Today;

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _searchDebounceTimer.Tick += async (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _page = 1;
            await RefreshAsync();
        };

        // โหลดตอน "เห็น" ไม่ใช่ตอน Loaded - เหตุผลเดียวกับแท็บอื่นในหน้านี้ (ดู WebsiteBlockTab)
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await RefreshAsync();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก WebsiteControlPage หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    private Window? OwnerWindow => Window.GetWindow(this);

    private async Task RefreshAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        try
        {
            var from = FromDatePicker.SelectedDate;
            // "ถึง" รวมทั้งวันนั้น (เที่ยงคืนของวันถัดไป) ไม่ใช่แค่ 00:00:00 ของวันที่เลือก ไม่งั้นเลือก
            // "ถึงวันนี้" แล้วจะไม่เห็นประวัติของวันนี้เองเลยสักแถว
            var to = ToDatePicker.SelectedDate?.AddDays(1);
            var className = (ClassNameCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();
            var domain = string.IsNullOrWhiteSpace(DomainBox.Text) ? null : DomainBox.Text.Trim();
            var blockedOnly = BlockedOnlyCheck.IsChecked == true;

            var pageResult = await _apiClient.GetWebVisitsAsync(from, to, className, search, domain, blockedOnly, _page, PageSize);
            _currentPageVisits = pageResult.Items;

            RebuildClassNameCombo(_currentPageVisits);

            VisitsGrid.ItemsSource = _currentPageVisits;
            VisitsEmptyText.Visibility = _currentPageVisits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            _totalCount = pageResult.TotalCount;
            var totalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
            PageInfoText.Text = $"หน้า {pageResult.Page}/{totalPages} ({_totalCount} รายการ)";

            await RefreshTopDomainsAsync(from, to);
        }
        catch (Exception ex)
        {
            ToastService.Error($"โหลดประวัติเว็บไม่สำเร็จ: {ex.Message}");
        }
    }

    private async Task RefreshTopDomainsAsync(DateTime? from, DateTime? to)
    {
        if (_apiClient is null) return;

        var top = await _apiClient.GetTopWebDomainsAsync(from, to, take: 6);
        var maxCount = top.Count == 0 ? 1 : top.Max(t => t.Count);

        // ความกว้างแถบเทียบกับตัวที่มากที่สุด (เต็ม 100% = 220px - พื้นที่ aside กว้าง 300 หัก padding/
        // ตัวเลขข้างๆ) เหมือน wireframe's bar{width} ที่คิดเทียบกับค่าสูงสุดในชุดเดียวกัน
        const double maxBarWidth = 220d;
        var rows = top.Select(t => new TopDomainRow
        {
            Domain = t.AnyBlocked ? $"{t.Domain} (บล็อกแล้ว)" : t.Domain,
            Count = t.Count,
            BarWidth = maxCount == 0 ? 0 : Math.Max(4, maxBarWidth * t.Count / maxCount)
        }).ToList();

        TopDomainsList.ItemsSource = rows;
    }

    /// <summary>สร้างตัวเลือก "ทุกห้องเรียน" + รายชื่อห้องเรียนที่เจอจริงในหน้าปัจจุบัน (ดู doc comment
    /// บนคลาส - ไม่มี endpoint "distinct class names" แยก) รักษา selection เดิมไว้ถ้ายังมีอยู่</summary>
    private void RebuildClassNameCombo(List<WebVisitDto> visits)
    {
        var previousTag = (ClassNameCombo.SelectedItem as ComboBoxItem)?.Tag as string;

        ClassNameCombo.SelectionChanged -= FilterChanged;
        try
        {
            ClassNameCombo.Items.Clear();
            ClassNameCombo.Items.Add(new ComboBoxItem { Content = "ทุกห้องเรียน", Tag = null });

            var classNames = visits
                .Where(v => !string.IsNullOrWhiteSpace(v.StudentClassName))
                .Select(v => v.StudentClassName!)
                .Distinct()
                .OrderBy(c => c);

            foreach (var className in classNames)
            {
                ClassNameCombo.Items.Add(new ComboBoxItem { Content = className, Tag = className });
            }

            var toSelect = ClassNameCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previousTag);
            ClassNameCombo.SelectedItem = toSelect ?? ClassNameCombo.Items[0];
        }
        finally
        {
            ClassNameCombo.SelectionChanged += FilterChanged;
        }
    }

    private async void FilterChanged(object sender, EventArgs e)
    {
        if (!IsLoaded) return;
        _page = 1;
        await RefreshAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        await RefreshAsync();
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
        if (_page >= totalPages) return;
        _page++;
        await RefreshAsync();
    }

    /// <summary>
    /// "+ บล็อกโดเมนที่เลือก" (con-website-history.dc.html) - ไม่มี endpoint แยกสำหรับการนี้ ใช้
    /// POST /api/website-rules/bulk ตัวเดียวกับปุ่ม "Bulk Add..." ของแท็บ "บล็อกเว็บ" ตรงๆ (ดู
    /// ApiClient.AddWebsiteRulesBulkAsync) เพราะผลลัพธ์ที่ต้องการเหมือนกันทุกประการ: เพิ่ม WebsiteRule
    /// ของแต่ละโดเมนที่ยังไม่มี
    /// </summary>
    private async void BlockSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;

        var selectedDomains = _currentPageVisits
            .Where(v => v.IsSelected)
            .Select(v => v.Domain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedDomains.Count == 0)
        {
            ToastService.Error("ยังไม่ได้เลือกแถวไหนเลย (ติ๊กที่ช่องซ้ายของตารางก่อน)");
            return;
        }

        if (!ConfirmWindow.Show(
                $"จะเพิ่มกฎบล็อกเว็บให้ {selectedDomains.Count} โดเมนที่เลือกไว้ ({string.Join(", ", selectedDomains.Take(5))}{(selectedDomains.Count > 5 ? ", ..." : "")})",
                "ยืนยันการบล็อกโดเมนที่เลือก", OwnerWindow, "บล็อกโดเมนนี้"))
        {
            return;
        }

        var result = await _apiClient.AddWebsiteRulesBulkAsync(selectedDomains);
        if (result is null)
        {
            ToastService.Error("บล็อกโดเมนที่เลือกไม่สำเร็จ");
            return;
        }

        ToastService.Success($"เพิ่มกฎบล็อกเว็บแล้ว {result.Added} โดเมน (ข้าม {result.Skipped} โดเมนที่มีกฎอยู่แล้ว)");
    }

    /// <summary>แถวของ "เว็บที่เข้าบ่อยช่วงนี้" หลังคำนวณความกว้างแถบแล้ว (client-side ล้วนๆ)</summary>
    private sealed class TopDomainRow
    {
        public string Domain { get; init; } = string.Empty;
        public int Count { get; init; }
        public double BarWidth { get; init; }
    }
}
