using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// ฟอร์มเดียวใช้ร่วมกัน Add/Edit เหมือน AddEditRuleWindow แต่สั้นกว่ามาก - Website Control มีแค่
/// Name/UrlPattern/IsEnabled ไม่มี publisher/signature/action-command
/// </summary>
public partial class AddEditWebsiteRuleWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = บันทึกสำเร็จ false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    private readonly ApiClient _apiClient;
    private readonly int _ruleId;

    /// <summary>RoomId ที่ต้องเลือกให้ตรงใน RoomCombo หลังโหลดรายการห้องเสร็จ (WP2-3) - null = "ทุกห้อง"</summary>
    private readonly int? _initialRoomId;

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
        _initialRoomId = existingRule?.RoomId;

        TitleText.Text = titleSuffix ?? "Add Website Rule";

        NameBox.Text = existingRule?.Name ?? string.Empty;
        UrlPatternBox.Text = existingRule?.UrlPattern ?? string.Empty;
        IsEnabledCheckBox.IsChecked = existingRule?.IsEnabled ?? true;

        Loaded += async (_, _) => await LoadRoomsAsync();
    }

    /// <summary>เหตุผลและ pattern เดียวกับ AddEditRuleWindow.LoadRoomsAsync (WP2-3)</summary>
    private async Task LoadRoomsAsync()
    {
        var rooms = await _apiClient.GetRoomsAsync();

        RoomCombo.Items.Clear();
        RoomCombo.Items.Add(new ComboBoxItem { Content = "ทุกห้อง", Tag = null });
        foreach (var room in rooms.OrderBy(r => r.Name))
        {
            RoomCombo.Items.Add(new ComboBoxItem { Content = room.Name, Tag = room.Id });
        }

        var toSelect = RoomCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => Equals(i.Tag, _initialRoomId));
        RoomCombo.SelectedItem = toSelect ?? RoomCombo.Items[0];
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
            IsEnabled = IsEnabledCheckBox.IsChecked ?? true,
            RoomId = (RoomCombo.SelectedItem as ComboBoxItem)?.Tag as int?
        };

        StatusText.Text = "กำลังบันทึก...";
        var success = _ruleId == 0
            ? await _apiClient.AddWebsiteRuleAsync(rule)
            : await _apiClient.UpdateWebsiteRuleAsync(rule);

        if (success)
        {
            ResultChosen?.Invoke(true);
        }
        else
        {
            StatusText.Text = "บันทึกไม่สำเร็จ ลองใหม่อีกครั้ง";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
