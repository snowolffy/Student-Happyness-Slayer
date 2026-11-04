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
public partial class WebsiteCatalogSourcesWindow : UserControl
{
    private readonly ApiClient _apiClient;
    private List<WebsiteCatalogSourceDto> _sources = new();

    /// <summary>UI-3c (2026-11-03): raise ตอนกดปิด - caller (DialogHost) ปิด host เอง</summary>
    public event System.Action? CloseRequested;

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
            // UI-4 (2026-11-04): ไอคอนหมุนคู่กับข้อความ "กำลังโหลด..." - ปิดเสมอใน finally
            StatusLoadingIcon.Visibility = Visibility.Visible;
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
        finally
        {
            StatusLoadingIcon.Visibility = Visibility.Collapsed;
        }
    }

    private async void AddSourceButton_Click(object sender, RoutedEventArgs e)
    {
        // UI-3b/3c (2026-11-03): AddEditWebsiteCatalogSourceWindow กลายเป็น UserControl แล้ว (UI-3b)
        // และ WebsiteCatalogSourcesWindow เองก็กลายเป็น UserControl ที่ฝังใน DialogHost ของ
        // MainWindow แล้ว (UI-3c, เป็น "dialog ใหญ่") - Window.GetWindow(this) จึงแก้ไปที่ MainWindow
        // เสมอ dialog นี้จึงเปิดซ้อนทับผ่าน DialogHost ได้จริง (stack ได้ - ดู DialogHost.Push)
        var dialog = new AddEditWebsiteCatalogSourceWindow(_apiClient);
        if (await AddEditWebsiteCatalogSourceWindow.ShowViaHost(Window.GetWindow(this), dialog))
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
            if (await AddEditWebsiteCatalogSourceWindow.ShowViaHost(Window.GetWindow(this), dialog))
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
                "ยืนยันการลบ Source", source.Name, Window.GetWindow(this), "ลบ source นี้");
            if (!confirmed)
            {
                return;
            }

            var success = await _apiClient.DeleteWebsiteCatalogSourceAsync(source.Id);
            StatusText.Text = success ? "ลบ source สำเร็จ" : "ลบ source ไม่สำเร็จ";
            await RefreshAsync();
        }
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) => CloseRequested?.Invoke();
}
