using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// จัดการ WebsiteCatalogSource (2026-09-12) - popup ที่สอง เปิดจากปุ่ม "จัดการ Catalog..." ใน
/// WebsiteControlWindow แยกจากตาราง WebsiteRule เอง เพราะคนละ data/lifecycle กัน
/// </summary>
public partial class WebsiteCatalogSourcesWindow : Window
{
    private readonly ApiClient _apiClient;
    private List<WebsiteCatalogSourceDto> _sources = new();

    public WebsiteCatalogSourcesWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "กำลังโหลด...";
            _sources = await _apiClient.GetWebsiteCatalogSourcesAsync();
            SourcesGrid.ItemsSource = _sources;
            SourcesEmptyText.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"อัพเดตล่าสุด: {System.DateTime.Now:HH:mm:ss}";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"โหลดข้อมูลไม่สำเร็จ: {ex.Message}";
        }
    }

    private async void AddSourceButton_Click(object sender, RoutedEventArgs e)
    {
        // UI-3b (2026-11-03): AddEditWebsiteCatalogSourceWindow กลายเป็น UserControl แล้ว - owner นี้
        // (WebsiteCatalogSourcesWindow) ยังไม่มี DialogHost ของตัวเอง (รอ UI-3c ย้าย) ShowViaHost จึง
        // เลือก fallback เปิดเป็น Window ลอยแบบเดิมเอง
        var dialog = new AddEditWebsiteCatalogSourceWindow(_apiClient);
        if (await AddEditWebsiteCatalogSourceWindow.ShowViaHost(this, dialog))
        {
            StatusText.Text = "เพิ่ม source สำเร็จ";
            await RefreshAsync();
        }
    }

    private async void EditSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WebsiteCatalogSourceDto source })
        {
            var dialog = new AddEditWebsiteCatalogSourceWindow(_apiClient, source);
            if (await AddEditWebsiteCatalogSourceWindow.ShowViaHost(this, dialog))
            {
                StatusText.Text = "แก้ไข source สำเร็จ";
                await RefreshAsync();
            }
        }
    }

    private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int sourceId })
        {
            StatusText.Text = "กำลัง sync...";
            var success = await _apiClient.SyncWebsiteCatalogSourceNowAsync(sourceId);
            StatusText.Text = success ? "Sync สำเร็จ - ดูผลได้ที่คอลัมน์ 'ผล sync ล่าสุด'" : "Sync ไม่สำเร็จ";
            await RefreshAsync();
        }
    }

    private async void DeleteSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WebsiteCatalogSourceDto source })
        {
            var confirmed = ConfirmWindow.ShowWithTypeToConfirm(
                $"กำลังจะลบ catalog source \"{source.Name}\" — กฎบล็อกเว็บทุกรายการที่มาจาก source นี้จะถูกลบไปด้วย การกระทำนี้ย้อนกลับไม่ได้",
                "ยืนยันการลบ Source", source.Name, this, "ลบ source นี้");
            if (!confirmed)
            {
                return;
            }

            var success = await _apiClient.DeleteWebsiteCatalogSourceAsync(source.Id);
            StatusText.Text = success ? "ลบ source สำเร็จ" : "ลบ source ไม่สำเร็จ";
            await RefreshAsync();
        }
    }
}
