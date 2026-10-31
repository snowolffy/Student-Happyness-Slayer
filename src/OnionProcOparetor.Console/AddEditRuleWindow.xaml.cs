using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// ฟอร์มเดียวใช้ร่วมกัน 3 ทางเข้า - ตั้งใจไม่แยกเป็นคนละหน้าต่างเพื่อไม่ให้ field ของ Rule (Name/
/// PublisherName/ProcessNameContains/RequireSignedMatch/KillProcess/ActionCommand/ActionArguments/
/// IsEnabled) ต้องเขียนซ้ำ 3 ที่:
/// - Add mode: ฟอร์มว่าง กด SAVE แล้ว AddRuleAsync (Id == 0)
/// - Edit mode: prefill จาก RuleDto ที่มีอยู่แล้ว กด SAVE แล้ว UpdateRuleAsync แบบ in-place (เก็บ
///   Id/RuleOverride ของกฎเดิมไว้ครบ ไม่ต้องลบแล้วสร้างใหม่)
/// - Create-from-process mode: prefill PublisherName/ProcessNameContains/RequireSignedMatch จาก
///   process ที่ครูเลือกจาก RunningProcessesWindow ให้รีวิว/แก้ก่อนกด SAVE เสมอ (Id = 0 เหมือน Add)
/// </summary>
public partial class AddEditRuleWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _ruleId;

    /// <summary>RoomId ที่ต้องเลือกให้ตรงใน RoomCombo หลังโหลดรายการห้องเสร็จ (WP2-3) - null = "ทุกห้อง"</summary>
    private readonly int? _initialRoomId;

    /// <summary>Add mode - ฟอร์มว่าง</summary>
    public AddEditRuleWindow(ApiClient apiClient) : this(apiClient, existingRule: null, titleSuffix: null)
    {
    }

    /// <summary>Edit mode - prefill ทุก field จากกฎเดิม แก้แบบ in-place ไม่ลบแล้วสร้างใหม่</summary>
    public AddEditRuleWindow(ApiClient apiClient, RuleDto ruleToEdit)
        : this(apiClient, ruleToEdit, titleSuffix: $"Edit - {ruleToEdit.Name}")
    {
    }

    /// <summary>
    /// Create-from-process mode - prefill จากโปรแกรมที่ครูเลือกจาก RunningProcessesWindow ให้รีวิว/
    /// แก้ก่อนกด SAVE เสมอ (ไม่บันทึกทันทีแบบ one-click) Id = 0 เพื่อให้ SAVE ไปทาง AddRuleAsync
    /// </summary>
    public AddEditRuleWindow(ApiClient apiClient, RunningProcessDto process)
        : this(apiClient, new RuleDto
        {
            Id = 0,
            Name = process.ProcessName,
            PublisherName = process.PublisherName ?? string.Empty,
            ProcessNameContains = process.ProcessName,
            RequireSignedMatch = process.IsSignedMatch,
            IsEnabled = true
        }, titleSuffix: $"Add Rule - จาก {process.ProcessName}")
    {
    }

    private AddEditRuleWindow(ApiClient apiClient, RuleDto? existingRule, string? titleSuffix)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ruleId = existingRule?.Id ?? 0;
        _initialRoomId = existingRule?.RoomId;

        TitleText.Text = titleSuffix ?? "Add Rule";

        NameBox.Text = existingRule?.Name ?? string.Empty;
        PublisherBox.Text = existingRule?.PublisherName ?? string.Empty;
        ProcessNameContainsBox.Text = existingRule?.ProcessNameContains ?? string.Empty;
        RequireSignedMatchCheckBox.IsChecked = existingRule?.RequireSignedMatch ?? true;
        KillProcessCheckBox.IsChecked = existingRule?.KillProcess ?? false;
        IsEnabledCheckBox.IsChecked = existingRule?.IsEnabled ?? true;
        ActionCommandBox.Text = existingRule?.ActionCommand ?? string.Empty;
        ActionArgumentsBox.Text = existingRule?.ActionArguments ?? string.Empty;

        // โหลดรายการห้องแบบ async ตอน Loaded (ตาม pattern เดียวกับ RuleOverridesWindow/
        // WebsiteCatalogSourcesWindow) - constructor สั่ง await ตรงๆ ไม่ได้
        Loaded += async (_, _) => await LoadRoomsAsync();
    }

    /// <summary>เติม RoomCombo: ตัวเลือกแรกเสมอคือ "ทุกห้อง" (Tag=null) ตามด้วยห้องทั้งหมด แล้วเลือกคืน
    /// ให้ตรงกับ _initialRoomId ถ้ามี (edit mode) - ถ้าห้องนั้นหายไปแล้วถอยไป "ทุกห้อง" (WP2-3)</summary>
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
        var publisher = PublisherBox.Text.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(publisher))
        {
            StatusText.Text = "กรุณากรอกชื่อกฎและ Publisher อย่างน้อย";
            return;
        }

        var processNameContains = ProcessNameContainsBox.Text.Trim();
        var actionCommand = ActionCommandBox.Text.Trim();
        var actionArguments = ActionArgumentsBox.Text.Trim();
        var roomId = (RoomCombo.SelectedItem as ComboBoxItem)?.Tag as int?;

        var rule = new RuleDto
        {
            Id = _ruleId,
            Name = name,
            PublisherName = publisher,
            ProcessNameContains = string.IsNullOrEmpty(processNameContains) ? null : processNameContains,
            RequireSignedMatch = RequireSignedMatchCheckBox.IsChecked ?? true,
            KillProcess = KillProcessCheckBox.IsChecked ?? false,
            ActionCommand = string.IsNullOrEmpty(actionCommand) ? null : actionCommand,
            ActionArguments = string.IsNullOrEmpty(actionArguments) ? null : actionArguments,
            IsEnabled = IsEnabledCheckBox.IsChecked ?? true,
            RoomId = roomId
        };

        StatusText.Text = "กำลังบันทึก...";
        var success = _ruleId == 0
            ? await _apiClient.AddRuleAsync(rule)
            : await _apiClient.UpdateRuleAsync(rule);

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
