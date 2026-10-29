using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// ฟอร์มเดียวใช้ร่วมกัน Add/Edit เหมือน AddEditRuleWindow แต่สั้นกว่ามาก - Website Control มีแค่
/// Name/UrlPattern/IsEnabled ไม่มี publisher/signature/action-command
/// </summary>
public partial class AddEditWebsiteRuleWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _ruleId;

    /// <summary>Add mode - ฟอร์มว่าง</summary>
    public AddEditWebsiteRuleWindow(ApiClient apiClient) : this(apiClient, existingRule: null, titleSuffix: null)
    {
    }

    /// <summary>Edit mode - prefill จากกฎเดิม แก้แบบ in-place ไม่ลบแล้วสร้างใหม่</summary>
    public AddEditWebsiteRuleWindow(ApiClient apiClient, WebsiteRuleDto ruleToEdit)
        : this(apiClient, ruleToEdit, titleSuffix: $"Edit - {ruleToEdit.Name}")
    {
    }

    private AddEditWebsiteRuleWindow(ApiClient apiClient, WebsiteRuleDto? existingRule, string? titleSuffix)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ruleId = existingRule?.Id ?? 0;

        TitleText.Text = titleSuffix ?? "Add Website Rule";

        NameBox.Text = existingRule?.Name ?? string.Empty;
        UrlPatternBox.Text = existingRule?.UrlPattern ?? string.Empty;
        IsEnabledCheckBox.IsChecked = existingRule?.IsEnabled ?? true;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var urlPattern = UrlPatternBox.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(urlPattern))
        {
            StatusText.Text = "กรุณากรอกชื่อกฎและ URL pattern";
            return;
        }

        var rule = new WebsiteRuleDto
        {
            Id = _ruleId,
            Name = name,
            UrlPattern = urlPattern,
            IsEnabled = IsEnabledCheckBox.IsChecked ?? true
        };

        StatusText.Text = "กำลังบันทึก...";
        var success = _ruleId == 0
            ? await _apiClient.AddWebsiteRuleAsync(rule)
            : await _apiClient.UpdateWebsiteRuleAsync(rule);

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
