using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>ของ ItemsControl ด้านซ้าย - ครูเลือกชุดได้หลายชุดพร้อมกัน (checkbox ไม่ใช่ radio)</summary>
public sealed class ExamPresetSelection
{
    public required AllowListPresetDto Preset { get; init; }
    public bool IsChecked { get; set; }
}

/// <summary>
/// "เปิดโหมดสอบ" (WP2-2, 2026-10-31) - ดู wireframe nf-exam.dc.html และ comment ใน .xaml ว่าทำไมเลย์เอาต์
/// ต่างออกไปบ้าง เปิดจากปุ่มบนแถบคำสั่งของ Clients (ดู DashboardWindow.OpenExamModeButton_Click)
///
/// ตั้งใจให้หน้าต่างนี้ "เรียก API เอง" (เหมือน WebsiteAntiEvasionTab) ไม่ใช่แค่เก็บ input แล้วให้ผู้เรียก
/// ไปสั่งต่อ (ต่างจาก BroadcastMessageWindow) เพราะต้องโหลดชุด Allow-list จาก Server ก่อนแสดงผลอยู่แล้ว
/// (async ตั้งแต่เปิดหน้าต่าง) เพิ่ม "ยิง start" เข้าไปในที่เดียวกันจึงเป็นธรรมชาติกว่า
/// </summary>
public partial class ExamModeWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<RoomDto> _allRooms;

    private List<ExamPresetSelection> _presetSelections = new();

    /// <summary>ไม่ null = เริ่มโหมดสอบสำเร็จ - ผู้เรียก (DashboardWindow) ใช้รีเฟรชประกาศ "โหมดสอบกำลังทำงาน"</summary>
    public ExamSessionDto? StartedSession { get; private set; }

    public ExamModeWindow(ApiClient apiClient, List<ClientMachineDto> selectedClients, List<RoomDto> allRooms, int? currentRoomFilterId)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _selectedClients = selectedClients;
        _allRooms = allRooms;

        RoomCombo.ItemsSource = _allRooms;
        if (_allRooms.Count > 0)
        {
            RoomCombo.SelectedItem = _allRooms.FirstOrDefault(r => r.Id == currentRoomFilterId) ?? _allRooms[0];
        }
        else
        {
            RoomCombo.IsEnabled = false;
            ScopeRoomRadio.IsEnabled = false;
        }

        ScopeSelectedRadio.Content = _selectedClients.Count > 0
            ? $"เครื่องที่เลือกไว้ ({_selectedClients.Count} เครื่อง)"
            : "เครื่องที่เลือกไว้ (ยังไม่ได้เลือกเครื่องไหน)";
        ScopeSelectedRadio.IsEnabled = _selectedClients.Count > 0;

        if (_allRooms.Count > 0)
        {
            ScopeRoomRadio.IsChecked = true;
        }
        else
        {
            ScopeSelectedRadio.IsChecked = true;
        }

        Loaded += async (_, _) => await LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _apiClient.GetAllowListPresetsAsync();
            _presetSelections = presets.Select(p => new ExamPresetSelection { Preset = p }).ToList();
            PresetsList.ItemsSource = _presetSelections;
            RefreshSummary();
        }
        catch (Exception)
        {
            // โหลดชุด Allow-list ไม่สำเร็จ (Server เข้าไม่ถึง/ApiClient ยังไม่ login - เช่นโหมดตรวจ UI) -
            // อย่าทำให้หน้าต่างพัง ปล่อยให้รายการว่างแล้วบอกสถานะไว้ ครูยังพิมพ์ "เว็บเพิ่มเติม" เองได้ตามปกติ
            SetStatus("โหลดชุด Allow-list จาก Server ไม่สำเร็จ - ยังพิมพ์เว็บเพิ่มเติมเองได้", isError: true);
        }
    }

    private void PresetCheckBox_Changed(object sender, RoutedEventArgs e) => RefreshSummary();
    private void ExtraDomainsTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshSummary();
    private void RoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshScopeLabelsAndSummary();

    private void ScopeRadio_Changed(object sender, RoutedEventArgs e) => RefreshSummary();

    /// <summary>ปุ่ม pill 30/50/60 นาที - mutual exclusion ทำเอง (ToggleButton ไม่มี GroupName แบบ RadioButton) เลือกปุ่มใหม่แล้วเคลียร์ช่องกำหนดเองทิ้งเสมอ (กันค่าสองที่ขัดกัน)</summary>
    private void DurationPill_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in new[] { Duration30Button, Duration50Button, Duration60Button })
        {
            button.IsChecked = ReferenceEquals(button, sender);
        }

        CustomMinutesTextBox.Text = "";
        RefreshSummary();
    }

    private void CustomMinutesTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CustomMinutesTextBox.Text))
        {
            // กำหนดเองชนะ pill เสมอ - ปลดปุ่ม pill ทั้งหมดทิ้งให้เห็นชัดว่าใช้ค่าไหนอยู่
            foreach (var button in new[] { Duration30Button, Duration50Button, Duration60Button })
            {
                button.IsChecked = false;
            }
        }

        RefreshSummary();
    }

    private void RefreshScopeLabelsAndSummary()
    {
        if (RoomCombo.SelectedItem is RoomDto room)
        {
            // RoomDto.ClientCount มาจาก Server ตรงๆ (GET /api/rooms - ดู RoomEndpoints.cs) ไม่ต้องคำนวณเอง
            ScopeRoomRadio.Content = $"ทั้งห้อง {room.Name} ({room.ClientCount} เครื่อง)";
        }
        RefreshSummary();
    }

    private int GetSelectedDurationMinutes()
    {
        if (int.TryParse(CustomMinutesTextBox.Text, out var custom) && custom > 0)
        {
            return custom;
        }

        if (Duration30Button.IsChecked == true) return 30;
        if (Duration60Button.IsChecked == true) return 60;
        return 50; // Duration50Button เป็นค่าเริ่มต้น (IsChecked="True" ใน XAML)
    }

    private List<int> GetSelectedPresetIds() =>
        _presetSelections.Where(s => s.IsChecked).Select(s => s.Preset.Id).ToList();

    private List<string> GetExtraDomains() =>
        ExtraDomainsTextBox.Text
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private void RefreshSummary()
    {
        var presetIds = GetSelectedPresetIds();
        var extraDomains = GetExtraDomains();
        var domainCount = _presetSelections.Where(s => s.IsChecked).Sum(s => s.Preset.Domains.Count) + extraDomains.Count;

        var minutes = GetSelectedDurationMinutes();
        var endsAt = DateTime.Now.AddMinutes(minutes);

        var targetText = ScopeRoomRadio.IsChecked == true && RoomCombo.SelectedItem is RoomDto room
            ? $"ห้อง {room.Name}"
            : $"{_selectedClients.Count} เครื่องที่เลือกไว้";

        SummaryText.Text = domainCount == 0
            ? "สรุป: ยังไม่ได้เลือกเว็บที่อนุญาตเลย - ต้องเลือกชุดสำเร็จรูปหรือพิมพ์เว็บเพิ่มเติมอย่างน้อยหนึ่งรายการ"
            : $"สรุป: {targetText} · เปิดได้ {domainCount} โดเมน · ถึง {endsAt:HH:mm} แล้วกลับเป็นกฎปกติเอง แม้ครูลืมปิดหรือเครื่องครูดับ";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var presetIds = GetSelectedPresetIds();
        var extraDomains = GetExtraDomains();

        if (presetIds.Count == 0 && extraDomains.Count == 0)
        {
            SetStatus("ต้องเลือกชุด Allow-list หรือพิมพ์เว็บเพิ่มเติมอย่างน้อยหนึ่งรายการ", isError: true);
            return;
        }

        var minutes = GetSelectedDurationMinutes();
        var useRoom = ScopeRoomRadio.IsChecked == true;

        if (useRoom && RoomCombo.SelectedItem is not RoomDto)
        {
            SetStatus("กรุณาเลือกห้อง", isError: true);
            return;
        }

        if (!useRoom && _selectedClients.Count == 0)
        {
            SetStatus("ยังไม่ได้เลือกเครื่องไหนไว้ - กลับไปเลือกเครื่องก่อน หรือเลือกเป้าหมายเป็นห้องแทน", isError: true);
            return;
        }

        StartButton.IsEnabled = false;
        try
        {
            var result = await _apiClient.StartExamSessionAsync(
                roomId: useRoom ? (RoomCombo.SelectedItem as RoomDto)?.Id : null,
                clientGuids: useRoom ? null : _selectedClients.Select(c => c.ClientGuid).ToList(),
                presetIds: presetIds,
                extraDomains: extraDomains,
                durationMinutes: minutes);

            if (result is null)
            {
                SetStatus("เริ่มโหมดสอบไม่สำเร็จ - ตรวจสอบว่าเลือกเป้าหมายและเว็บที่อนุญาตไว้ถูกต้องแล้ว", isError: true);
                StartButton.IsEnabled = true;
                return;
            }

            StartedSession = result;
            DialogResult = true;
            Close();
        }
        catch (Exception)
        {
            SetStatus("เริ่มโหมดสอบไม่สำเร็จ - ต่อ Server ไม่ได้", isError: true);
            StartButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
