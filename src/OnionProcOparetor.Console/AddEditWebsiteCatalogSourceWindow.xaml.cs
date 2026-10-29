using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>Add/Edit สำหรับ WebsiteCatalogSource (2026-09-12) - เดินตามโครง AddEditWebsiteRuleWindow เป๊ะ</summary>
public partial class AddEditWebsiteCatalogSourceWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _sourceId;

    /// <summary>Add mode - ฟอร์มว่าง MaxEntries default 1000</summary>
    public AddEditWebsiteCatalogSourceWindow(ApiClient apiClient) : this(apiClient, existingSource: null, titleSuffix: null)
    {
    }

    /// <summary>Edit mode - prefill จาก source เดิม แก้แบบ in-place</summary>
    public AddEditWebsiteCatalogSourceWindow(ApiClient apiClient, WebsiteCatalogSourceDto existingSource)
        : this(apiClient, existingSource, titleSuffix: $"Edit - {existingSource.Name}")
    {
    }

    private AddEditWebsiteCatalogSourceWindow(ApiClient apiClient, WebsiteCatalogSourceDto? existingSource, string? titleSuffix)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _sourceId = existingSource?.Id ?? 0;

        TitleText.Text = titleSuffix ?? "Add Catalog Source";

        NameBox.Text = existingSource?.Name ?? string.Empty;
        SourceUrlBox.Text = existingSource?.SourceUrl ?? string.Empty;
        MaxEntriesBox.Text = (existingSource?.MaxEntries ?? 1000).ToString();
        IsEnabledCheckBox.IsChecked = existingSource?.IsEnabled ?? true;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var url = SourceUrlBox.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
        {
            StatusText.Text = "กรุณากรอกชื่อและ Source URL";
            return;
        }

        if (!int.TryParse(MaxEntriesBox.Text.Trim(), out var maxEntries) || maxEntries <= 0)
        {
            StatusText.Text = "Max Entries ต้องเป็นตัวเลขมากกว่า 0";
            return;
        }

        var source = new WebsiteCatalogSourceDto
        {
            Id = _sourceId,
            Name = name,
            SourceUrl = url,
            MaxEntries = maxEntries,
            IsEnabled = IsEnabledCheckBox.IsChecked ?? true
        };

        StatusText.Text = "กำลังบันทึก...";
        var success = _sourceId == 0
            ? await _apiClient.AddWebsiteCatalogSourceAsync(source)
            : await _apiClient.UpdateWebsiteCatalogSourceAsync(source);

        if (success)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "บันทึกไม่สำเร็จ ลองใหม่อีกครั้ง";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
