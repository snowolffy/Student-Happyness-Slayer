using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// แท็บ "ชุด Allow-list" ของหน้า Website Control (WP2-2, 2026-10-31) - CRUD ชุด Allow-list สำเร็จรูป/
/// กำหนดเองที่ครูเลือกได้ตอนเปิดโหมดสอบ (ดู ExamModeWindow + ApiClient.GetAllowListPresetsAsync ฯลฯ)
///
/// เดินตาม pattern เดียวกับ WebsiteAntiEvasionTab: โหลดค่าจริงทุกครั้งที่ "เห็น" แท็บนี้ (IsVisibleChanged)
/// ไม่ใช่ Loaded ครั้งเดียว เพื่อไม่ให้ค่าที่ครูคนอื่นเพิ่งแก้จากอีกเครื่อง Console ดูเหมือนเก่า
/// </summary>
public partial class WebsiteAllowListPlaceholderTab : UserControl
{
    private ApiClient? _apiClient;
    private List<AllowListPresetDto> _presets = new();
    private AllowListPresetDto? _selected;

    public WebsiteAllowListPlaceholderTab()
    {
        InitializeComponent();

        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
            {
                await LoadFromServerAsync();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก WebsiteControlPage หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    private async Task LoadFromServerAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        var selectedId = _selected?.Id;

        try
        {
            _presets = await _apiClient.GetAllowListPresetsAsync();
        }
        catch (Exception)
        {
            // เหตุผลเดียวกับ WebsiteAntiEvasionTab.LoadFromServerAsync: ApiClient ที่ยังไม่ได้ตั้ง
            // BaseAddress (เช่นโหมดตรวจ UI ที่ไม่ได้ login จริง) โยน InvalidOperationException ไม่ใช่
            // HttpRequestException - ต้องจับแบบกว้างไม่งั้นแท็บนี้พังทุกครั้งที่สลับมาโดยไม่มี Server ต่ออยู่จริง
            ToastService.Error("โหลดชุด Allow-list ไม่สำเร็จ");
            return;
        }

        PresetsList.ItemsSource = _presets;

        // คงการเลือกเดิมไว้ถ้ายังมีอยู่ (เช่นโหลดใหม่หลังเพิ่ม/ลบโดเมน) - ไม่งั้นแผงขวาจะหุบทุกครั้งที่
        // แก้อะไรสักอย่างสำเร็จแล้ว LoadFromServerAsync ถูกเรียกซ้ำ
        var toReselect = selectedId is not null ? _presets.FirstOrDefault(p => p.Id == selectedId) : null;
        if (toReselect is not null)
        {
            PresetsList.SelectedItem = toReselect;
        }
        else if (_presets.Count > 0 && PresetsList.SelectedItem is null)
        {
            PresetsList.SelectedIndex = 0;
        }
        else if (_presets.Count == 0)
        {
            ShowEmptyDetail();
        }
    }

    private void PresetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = PresetsList.SelectedItem as AllowListPresetDto;
        if (_selected is null)
        {
            ShowEmptyDetail();
            return;
        }

        DetailGrid.Visibility = Visibility.Visible;
        EmptyDetailState.Visibility = Visibility.Collapsed;

        SelectedPresetNameText.Text = _selected.Name;
        SelectedPresetMetaText.Text = _selected.IsBuiltIn
            ? $"ชุดสำเร็จรูป · แก้ได้ · {_selected.DomainCountText}"
            : $"ชุดกำหนดเอง · {_selected.DomainCountText}";

        DomainsList.ItemsSource = _selected.Domains;
        TestResultText.Text = "";
    }

    private void ShowEmptyDetail()
    {
        DetailGrid.Visibility = Visibility.Collapsed;
        EmptyDetailState.Visibility = Visibility.Visible;
    }

    private async void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;

        var dialog = new AddAllowListPresetWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var success = await _apiClient.AddAllowListPresetAsync(dialog.PresetName);
        if (!success)
        {
            ToastService.Error("สร้างชุด Allow-list ไม่สำเร็จ");
            return;
        }

        ToastService.Success($"สร้างชุด '{dialog.PresetName}' แล้ว");
        await LoadFromServerAsync();
    }

    private async void DuplicatePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null || _selected is null) return;

        var success = await _apiClient.DuplicateAllowListPresetAsync(_selected.Id);
        if (!success)
        {
            ToastService.Error("ทำสำเนาชุดไม่สำเร็จ");
            return;
        }

        ToastService.Success($"ทำสำเนาชุด '{_selected.Name}' แล้ว");
        _selected = null; // เลือกตัวใหม่ (สำเนา) ไม่ได้ง่ายๆ เพราะไม่รู้ Id ล่วงหน้า - ปล่อยให้ reload เลือกตัวแรกแทน
        await LoadFromServerAsync();
    }

    private async void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null || _selected is null) return;

        if (!ConfirmWindow.Show(
                $"ชุด '{_selected.Name}' และโดเมนทั้งหมดในชุดจะถูกลบ - เซสชันโหมดสอบที่เคยใช้ชุดนี้ (ถ้ามี) จะไม่ได้รับผลย้อนหลัง",
                "ลบชุด Allow-list?", Window.GetWindow(this), "ลบชุด"))
        {
            return;
        }

        var success = await _apiClient.DeleteAllowListPresetAsync(_selected.Id);
        if (!success)
        {
            ToastService.Error("ลบชุดไม่สำเร็จ");
            return;
        }

        ToastService.Success($"ลบชุด '{_selected.Name}' แล้ว");
        _selected = null;
        await LoadFromServerAsync();
    }

    private async void AddDomainButton_Click(object sender, RoutedEventArgs e) => await AddDomainAsync();

    private async void NewDomainTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddDomainAsync();
        }
    }

    private async Task AddDomainAsync()
    {
        if (_apiClient is null || _selected is null) return;

        var domain = NewDomainTextBox.Text.Trim();
        if (string.IsNullOrEmpty(domain))
        {
            return;
        }

        var success = await _apiClient.AddAllowListPresetDomainAsync(_selected.Id, domain);
        if (!success)
        {
            ToastService.Error($"เพิ่มโดเมน '{domain}' ไม่สำเร็จ (อาจมีอยู่แล้วในชุดนี้)");
            return;
        }

        NewDomainTextBox.Text = "";
        ToastService.Success($"เพิ่มโดเมน '{domain}' แล้ว");
        await LoadFromServerAsync();
    }

    private async void RemoveDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null || _selected is null) return;
        if (sender is not Button { Tag: AllowListPresetDomainDto domain }) return;

        var success = await _apiClient.DeleteAllowListPresetDomainAsync(_selected.Id, domain.Id);
        if (!success)
        {
            ToastService.Error($"ลบโดเมน '{domain.Domain}' ไม่สำเร็จ");
            return;
        }

        await LoadFromServerAsync();
    }

    /// <summary>
    /// ทดสอบแบบ client-side ล้วนๆ (ไม่ได้เปิดเบราว์เซอร์จริง) - จำลอง semantics ของ Chrome/Edge
    /// URLAllowlist: domain "example.com" ครอบคลุม subdomain ทุกระดับ (เทียบ host เท่ากันเป๊ะ หรือ host
    /// ลงท้ายด้วย ".example.com") ไม่ครอบคลุม path/query/scheme ดังนั้นพอ parse host ได้ก็พอตัดสินได้แล้ว
    ///
    /// ไม่ใช่การยืนยันผลจริงในเบราว์เซอร์ - ดู docs/roadmap.md WP2-2 "เกณฑ์เสร็จ" ที่ระบุว่าการเปิดเว็บจริง
    /// ต้องทดสอบในแล็บ (เครื่อง dev นี้ห้ามเขียน policy จริงใต้ HKLM) เครื่องมือนี้ช่วยกรองเคสที่ชัดเจนว่า
    /// "เปิดไม่ได้แน่ๆ" (เช่น ลืมเติม accounts.google.com) ได้เร็วกว่าต้องไปลองเปิดเบราว์เซอร์จริงทุกครั้ง
    /// </summary>
    private void TestUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var input = TestUrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            TestResultText.Text = "";
            return;
        }

        string? host;
        try
        {
            var withScheme = input.Contains("://") ? input : $"https://{input}";
            host = new Uri(withScheme).Host;
        }
        catch (UriFormatException)
        {
            TestResultText.Text = "URL ไม่ถูกต้อง";
            TestResultText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        var matched = _selected.Domains.Any(d =>
            string.Equals(host, d.Domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + d.Domain, StringComparison.OrdinalIgnoreCase));

        if (matched)
        {
            TestResultText.Text = "เปิดได้ ✓";
            TestResultText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Online");
        }
        else
        {
            TestResultText.Text = $"เปิดไม่ได้ ({host} ไม่อยู่ในชุด)";
            TestResultText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger.Default");
        }
    }
}
