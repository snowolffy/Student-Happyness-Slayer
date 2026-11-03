using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>ผลลัพธ์ของการกด "เริ่มส่งจอ" สำเร็จ - DashboardWindow ใช้เปิด BroadcastWarningBarWindow ต่อ (ดู ScreenBroadcastButton_Click)</summary>
public sealed class ScreenBroadcastSession
{
    public required List<ClientMachineDto> Targets { get; init; }
    public required string ViewerMode { get; init; }
    public required string? TeacherDisplayName { get; init; }
    public int SuccessCount { get; init; }
    public int FailCount { get; init; }
}

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01) - ขั้นตอน 1 (ตั้งค่าก่อนเริ่ม) ดู wireframe tc-broadcast.dc.html
/// เปิดจากปุ่มบนแถบคำสั่งของ Clients (ดู DashboardWindow.ScreenBroadcastButton_Click)
///
/// ตั้งใจให้หน้าต่างนี้ "เรียก API เอง" เหมือน ExamModeWindow (ไม่ใช่แค่เก็บ input แล้วให้ผู้เรียกไปสั่งต่อ
/// แบบ OpenUrlWindow) เพราะต้องรอผล "ส่ง command ถึงกี่เครื่อง" ก่อนตัดสินใจว่าจะเปิดแถบแดง (ขั้นตอน 2)
/// ต่อไหม - ถ้าส่งไม่ถึงเครื่องไหนเลยก็ไม่ควรเริ่ม capture หน้าจอเปล่าๆ
/// </summary>
public partial class ScreenBroadcastSetupWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<ClientMachineDto> _allClients;
    private readonly List<RoomDto> _allRooms;

    /// <summary>ไม่ null = เริ่มส่งจอสำเร็จอย่างน้อย 1 เครื่อง - DashboardWindow เปิด BroadcastWarningBarWindow ต่อ</summary>
    public ScreenBroadcastSession? StartedSession { get; private set; }

    public ScreenBroadcastSetupWindow(
        ApiClient apiClient,
        List<ClientMachineDto> selectedClients,
        List<ClientMachineDto> allClients,
        List<RoomDto> allRooms,
        int? currentRoomFilterId)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _selectedClients = selectedClients;
        _allClients = allClients;
        _allRooms = allRooms;

        RoomCombo.ItemsSource = _allRooms;
        if (_allRooms.Count > 0)
        {
            RoomCombo.SelectedItem = _allRooms.FirstOrDefault(r => r.Id == currentRoomFilterId) ?? _allRooms[0];
        }
        else
        {
            RoomCombo.IsEnabled = false;
            TargetRoomRadio.IsEnabled = false;
        }

        TargetSelectedRadio.Content = _selectedClients.Count > 0
            ? $"เครื่องที่เลือกไว้ ({_selectedClients.Count} เครื่อง)"
            : "เครื่องที่เลือกไว้ (ยังไม่ได้เลือกเครื่องไหน)";
        TargetSelectedRadio.IsEnabled = _selectedClients.Count > 0;

        if (_allRooms.Count > 0)
        {
            TargetRoomRadio.IsChecked = true;
        }
        else
        {
            TargetSelectedRadio.IsChecked = true;
        }

        RefreshSummary();
    }

    private void TargetRadio_Changed(object sender, RoutedEventArgs e) => RefreshSummary();

    private void RoomCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RoomCombo.SelectedItem is RoomDto room)
        {
            TargetRoomRadio.Content = $"ทั้งห้อง {room.Name} ({room.ClientCount} เครื่อง)";
        }
        RefreshSummary();
    }

    private List<ClientMachineDto> ResolveTargets()
    {
        if (TargetRoomRadio.IsChecked == true && RoomCombo.SelectedItem is RoomDto room)
        {
            return _allClients.Where(c => c.RoomId == room.Id).ToList();
        }

        return _selectedClients;
    }

    private void RefreshSummary()
    {
        var targets = ResolveTargets();
        var targetText = TargetRoomRadio.IsChecked == true && RoomCombo.SelectedItem is RoomDto room
            ? $"ห้อง {room.Name}"
            : $"{_selectedClients.Count} เครื่องที่เลือกไว้";

        var modeText = FullscreenLockedRadio.IsChecked == true ? "เต็มจอ + ล็อกอินพุต" : "หน้าต่างลอย";
        SummaryText.Text = $"สรุป: ส่งจอครูให้ {targetText} ({targets.Count} เครื่อง) แบบ{modeText}";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = ResolveTargets();
        var useRoom = TargetRoomRadio.IsChecked == true;

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

        if (targets.Count == 0)
        {
            SetStatus("เป้าหมายที่เลือกไม่มีเครื่องอยู่เลย", isError: true);
            return;
        }

        StartButton.IsEnabled = false;
        SetStatus("กำลังส่งคำสั่งเริ่มดูจอครู...", isError: false);

        var viewerMode = FullscreenLockedRadio.IsChecked == true ? "FullscreenLocked" : "Windowed";
        var teacherDisplayName = string.IsNullOrWhiteSpace(_apiClient.DisplayName) ? _apiClient.Username : _apiClient.DisplayName;
        var payload = new { viewerMode, teacherDisplayName };

        var successCount = 0;
        foreach (var client in targets)
        {
            var success = await _apiClient.SendCommandAsync(client.ClientGuid, "StartScreenBroadcast", payload);
            if (success)
            {
                successCount++;
            }
        }

        if (successCount == 0)
        {
            StartButton.IsEnabled = true;
            SetStatus("ส่งคำสั่งไม่สำเร็จเลยทั้ง " + targets.Count + " เครื่อง - เครื่องอาจออฟไลน์หมดหรือ Server เข้าไม่ถึง", isError: true);
            return;
        }

        StartedSession = new ScreenBroadcastSession
        {
            Targets = targets,
            ViewerMode = viewerMode,
            TeacherDisplayName = teacherDisplayName,
            SuccessCount = successCount,
            FailCount = targets.Count - successCount
        };

        DialogResult = true;
        Close();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
