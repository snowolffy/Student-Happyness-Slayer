using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// แท็บ "ป้องกันการเลี่ยง" ของหน้า Website Control (WP2-1, 2026-10-31) - GET/POST
/// /api/browser-hardening(/set) ดู ApiClient.GetBrowserHardeningSettingsAsync/SetBrowserHardeningSettingsAsync
///
/// เดินตาม pattern เดียวกับ SettingsPage: โหลดค่าจริงทุกครั้งที่ "เห็น" แท็บนี้ (ไม่ใช่ Loaded)
/// เพื่อไม่ให้ค่าที่ครูคนอื่นเพิ่งเปลี่ยนจากอีกเครื่อง Console ดูเหมือนยังเป็นค่าเก่า และของที่พิมพ์/
/// ติ๊กค้างไว้โดยไม่กดบันทึกจะไม่หลอกว่าบันทึกแล้วตอนสลับแท็บไปมา
/// </summary>
public partial class WebsiteAntiEvasionTab : UserControl
{
    private ApiClient? _apiClient;

    public WebsiteAntiEvasionTab()
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

    private BrowserHardeningSettingsDto? _loaded;

    private async Task LoadFromServerAsync()
    {
        if (_apiClient is null)
        {
            return;
        }

        var settings = await _apiClient.GetBrowserHardeningSettingsAsync();
        if (settings is null)
        {
            ToastService.Error("โหลดค่าป้องกันการเลี่ยงไม่สำเร็จ");
            return;
        }

        _loaded = settings;
        ApplyToCheckBoxes(settings);
    }

    private void ApplyToCheckBoxes(BrowserHardeningSettingsDto settings)
    {
        BlockExtensionsCheckBox.IsChecked = settings.BlockExtensions;
        BlockGuestAndIncognitoCheckBox.IsChecked = settings.BlockGuestAndIncognito;
        BlockDevToolsCheckBox.IsChecked = settings.BlockDevTools;
        LockDnsAndProxyCheckBox.IsChecked = settings.LockDnsAndProxy;
        BlockOtherBrowsersCheckBox.IsChecked = settings.BlockOtherBrowsers;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loaded is not null)
        {
            ApplyToCheckBoxes(_loaded);
        }
        ToastService.Info("คืนค่าที่บันทึกไว้แล้ว");
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var updated = new BrowserHardeningSettingsDto
        {
            BlockExtensions = BlockExtensionsCheckBox.IsChecked ?? false,
            BlockGuestAndIncognito = BlockGuestAndIncognitoCheckBox.IsChecked ?? false,
            BlockDevTools = BlockDevToolsCheckBox.IsChecked ?? false,
            LockDnsAndProxy = LockDnsAndProxyCheckBox.IsChecked ?? false,
            BlockOtherBrowsers = BlockOtherBrowsersCheckBox.IsChecked ?? false
        };

        var success = await _apiClient.SetBrowserHardeningSettingsAsync(updated);
        if (!success)
        {
            ToastService.Error("บันทึกค่าป้องกันการเลี่ยงไม่สำเร็จ");
            return;
        }

        _loaded = updated;
        ToastService.Success("บันทึกค่าป้องกันการเลี่ยงแล้ว");
    }
}
