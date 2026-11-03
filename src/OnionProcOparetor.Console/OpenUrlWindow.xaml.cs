using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// "เปิด URL / โปรแกรมทั้งห้อง" (WP4-1, 2026-11-01) - ดู wireframe tc-openurl.dc.html และ comment ใน
/// .xaml เรื่องจุดที่ต่างจาก wireframe ตั้งใจ เปิดจากปุ่มบนแถบคำสั่งของ Clients (ดู
/// DashboardWindow.OpenUrlProgramButton_Click)
///
/// ต่างจาก ExamModeWindow ตรงที่ไม่ได้เรียก API เอง - ใช้ pattern เดียวกับ ActionPanelWindow
/// (OpenReportFor/PendingReport): สร้าง ActionReportRequest แล้วปิดตัวเองทันที ให้ DashboardWindow เปิด
/// ActionReportWindow ต่อ เพราะ "เปิด URL/โปรแกรม" ก็เป็น command ที่ผ่านระบบ command/ack เดียวกับ
/// Lock/Shutdown ทุกอย่าง (ไม่ใช่ API เฉพาะทางแบบโหมดสอบที่ต้องสร้าง session แยกเก็บใน DB)
/// </summary>
public partial class OpenUrlWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = กดเปิด (PendingReport ถูกเติมแล้ว) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    private readonly ApiClient _apiClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<ClientMachineDto> _allClients;
    private readonly List<RoomDto> _allRooms;

    private List<LaunchableProgramDto> _programs = new();

    /// <summary>ไม่ null = ครูกด "เปิดทั้งห้อง" แล้ว - DashboardWindow เป็นคนเปิด ActionReportWindow ต่อ
    /// (เหตุผลเดียวกับ ActionPanelWindow.PendingReport - หน้าต่างนี้ถูกเปิดด้วย ShowDialog เลยปิดตัวเองก่อน
    /// แทนที่จะเปิดหน้าต่างใหม่ซ้อนกัน)</summary>
    public ActionReportRequest? PendingReport { get; private set; }

    public OpenUrlWindow(
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

        Loaded += async (_, _) => await LoadProgramsAsync();
    }

    private async Task LoadProgramsAsync()
    {
        try
        {
            _programs = await _apiClient.GetLaunchableProgramsAsync();
            ProgramCombo.ItemsSource = _programs;
            if (_programs.Count > 0)
            {
                ProgramCombo.SelectedItem = _programs[0];
            }
            RefreshSummary();
        }
        catch (Exception)
        {
            // โหลดรายการโปรแกรมไม่สำเร็จ (Server เข้าไม่ถึง/ApiClient ยังไม่ login) - อย่าทำให้หน้าต่างพัง
            // ปล่อยให้โหมดเว็บไซต์ยังใช้ได้ตามปกติ แค่โหมดโปรแกรมจะว่าง
            SetStatus("โหลดรายการโปรแกรมจาก Server ไม่สำเร็จ - โหมดเว็บไซต์ยังใช้ได้ตามปกติ", isError: true);
        }
    }

    /// <summary>ปุ่ม pill เว็บไซต์/โปรแกรม - mutual exclusion ทำเอง (ToggleButton ไม่มี GroupName) เหมือน
    /// DurationPill_Click ของ ExamModeWindow</summary>
    private void ModePill_Click(object sender, RoutedEventArgs e)
    {
        var isWebsite = ReferenceEquals(sender, WebsiteModeButton);
        WebsiteModeButton.IsChecked = isWebsite;
        ProgramModeButton.IsChecked = !isWebsite;
        WebsitePanel.Visibility = isWebsite ? Visibility.Visible : Visibility.Collapsed;
        ProgramPanel.Visibility = isWebsite ? Visibility.Collapsed : Visibility.Visible;
        RefreshSummary();
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshSummary();
    private void ProgramCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSummary();
    private void TargetRadio_Changed(object sender, RoutedEventArgs e) => RefreshSummary();

    private void RoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomCombo.SelectedItem is RoomDto room)
        {
            // RoomDto.ClientCount มาจาก Server ตรงๆ (GET /api/rooms) ไม่ต้องคำนวณเอง - pattern เดียวกับ
            // ExamModeWindow.RefreshScopeLabelsAndSummary (ตัวเลขนี้คือจำนวนเครื่องทั้งหมดในห้อง ไม่ใช่
            // แค่ออนไลน์ - ต่างจาก wireframe เล็กน้อยที่เขียนว่า "เครื่องออนไลน์" ตั้งใจเดินตาม pattern เดิม)
            TargetRoomRadio.Content = $"ทั้งห้อง {room.Name} ({room.ClientCount} เครื่อง)";
        }
        RefreshSummary();
    }

    private bool IsWebsiteMode => WebsiteModeButton.IsChecked == true;

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

        if (IsWebsiteMode)
        {
            var url = UrlTextBox.Text.Trim();
            SummaryText.Text = string.IsNullOrEmpty(url)
                ? "สรุป: ยังไม่ได้กรอกลิงก์"
                : $"สรุป: เปิด {url} ให้ {targetText} ({targets.Count} เครื่อง)";
        }
        else
        {
            var program = ProgramCombo.SelectedItem as LaunchableProgramDto;
            SummaryText.Text = program is null
                ? "สรุป: ยังไม่มีโปรแกรมให้เลือก (ตั้งรายการโปรแกรมที่หน้าตั้งค่าก่อน)"
                : $"สรุป: เปิดโปรแกรม '{program.Name}' ให้ {targetText} ({targets.Count} เครื่อง)";
        }

        OpenButton.Content = targetText.StartsWith("ห้อง") ? "เปิดทั้งห้อง" : $"เปิด {targets.Count} เครื่อง";
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
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

        string commandType;
        object payload;
        string actionLabel;

        if (IsWebsiteMode)
        {
            var url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                SetStatus("กรุณากรอกลิงก์ที่ถูกต้อง (ต้องเป็น http:// หรือ https:// เท่านั้น)", isError: true);
                return;
            }

            commandType = "OpenUrl";
            payload = new { url = uri.AbsoluteUri, mode = FullscreenRadio.IsChecked == true ? "Fullscreen" : "NewTab" };
            actionLabel = "เปิด URL ทั้งห้อง";
        }
        else
        {
            if (ProgramCombo.SelectedItem is not LaunchableProgramDto program)
            {
                SetStatus("กรุณาเลือกโปรแกรม", isError: true);
                return;
            }

            commandType = "LaunchProgram";
            payload = new { programId = program.Id };
            actionLabel = $"เปิดโปรแกรม '{program.Name}' ทั้งห้อง";
        }

        var batchId = Guid.NewGuid().ToString("N");

        PendingReport = new ActionReportRequest
        {
            ActionLabel = actionLabel,
            BatchId = batchId,
            Rows = targets
                .Select(c => new ActionReportRow { ClientGuid = c.ClientGuid, MachineName = c.MachineName })
                .ToList(),
            SendAsync = async row =>
            {
                var (commandId, error) = await _apiClient.SendCommandTrackedAsync(row.ClientGuid, commandType, payload, batchId);
                if (commandId is null)
                {
                    return (false, error);
                }

                row.CommandId = commandId;
                return (true, null);
            }
        };

        ResultChosen?.Invoke(true);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Brush.Danger.Default" : "Brush.Text.Secondary");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
