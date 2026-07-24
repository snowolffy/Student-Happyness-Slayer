using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// Panel รวม action ทั้งหมดที่ทำกับเครื่อง client ได้ - ใช้ทั้ง 2 โหมด:
/// - Single: เปิดจากปุ่ม ACTIONS ต่อแถวใน ClientsGrid (ทำกับเครื่องเดียว มี Settings/Delete ให้ด้วย)
/// - Bulk: เปิดจากปุ่ม ACTIONS บน header (multi-select) - ไม่มี Settings/Delete (เสี่ยงเกินไปถ้าทำ
///   เป็น bulk) แต่เพิ่มตัวเลือกว่าจะ apply กับ "เครื่องที่เลือกไว้" หรือ "ทุกเครื่อง" แทน BroadcastMessageWindow
///   ที่มี pattern เดียวกันอยู่แล้ว
/// แทนที่ ContextMenu เดิม (ClientActionsButton_Click) และปุ่มแยกบน header (Toggle/Shutdown/Restart Selected)
/// </summary>
public partial class ActionPanelWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly bool _isBulk;
    private readonly ClientMachineDto? _singleClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<ClientMachineDto> _allClients;

    /// <summary>true ถ้ามี action ใดๆ ที่แก้ข้อมูลไปแล้ว - DashboardWindow ใช้ตัดสินใจว่าต้อง reload หรือไม่</summary>
    public bool DataChanged { get; private set; }

    /// <summary>Single-client mode - เปิดจากปุ่ม ACTIONS ต่อแถว</summary>
    public ActionPanelWindow(ApiClient apiClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _isBulk = false;
        _singleClient = client;
        _selectedClients = new List<ClientMachineDto> { client };
        _allClients = _selectedClients;

        HeaderText.Text = $"Actions - {client.MachineName}";
        TargetPanel.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Visible;
        DeleteButton.Visibility = Visibility.Visible;
        ToggleButton.Content = client.IsEnabled ? "DISABLE ENFORCEMENT" : "ENABLE ENFORCEMENT";
    }

    /// <summary>
    /// Bulk mode - เปิดจากปุ่ม ACTIONS บน header (multi-select) selectedClients ต้องมีอย่างน้อย 1 เครื่อง
    /// (caller เช็คมาก่อนแล้ว) allClients ใช้ตอนเลือก "ทุกเครื่อง" แทน "เครื่องที่เลือกไว้"
    /// </summary>
    public ActionPanelWindow(ApiClient apiClient, List<ClientMachineDto> selectedClients, List<ClientMachineDto> allClients)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _isBulk = true;
        _selectedClients = selectedClients;
        _allClients = allClients;

        TargetPanel.Visibility = Visibility.Visible;
        ApplyToSelectedRadio.Content = $"เครื่องที่เลือกไว้ ({selectedClients.Count} เครื่อง)";
        ApplyToAllRadio.Content = $"ทุกเครื่อง ({allClients.Count} เครื่อง)";
        ApplyToSelectedRadio.IsChecked = true;

        ApplyToSelectedRadio.Checked += (_, _) => HeaderText.Text = $"Applying to {_selectedClients.Count} selected machines";
        ApplyToAllRadio.Checked += (_, _) => HeaderText.Text = $"Applying to all {_allClients.Count} machines";
        HeaderText.Text = $"Applying to {selectedClients.Count} selected machines";

        SettingsButton.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = Visibility.Collapsed;
        ToggleButton.Content = "TOGGLE ENFORCEMENT";
    }

    private List<ClientMachineDto> CurrentTargets =>
        _isBulk && ApplyToAllRadio.IsChecked == true ? _allClients : _selectedClients;

    private static bool Confirm(string message, string title) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = CurrentTargets;

        if (_isBulk && !Confirm($"ยืนยัน toggle สถานะของ {targets.Count} เครื่องที่เลือกไว้?", "ยืนยัน Toggle"))
        {
            return;
        }

        StatusText.Text = $"กำลัง toggle {targets.Count} เครื่อง...";
        var successCount = 0;
        foreach (var client in targets)
        {
            if (await _apiClient.ToggleClientAsync(client.Id))
            {
                successCount++;
            }
        }

        DataChanged = true;
        StatusText.Text = $"Toggle สำเร็จ {successCount}/{targets.Count} เครื่อง";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        var settingsWindow = new ClientSettingsWindow(_apiClient, _singleClient) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            DataChanged = true;
            StatusText.Text = "บันทึก Settings สำเร็จ";
        }
    }

    // Lock ไม่ต้อง confirm (ทำคืนง่าย - ครูกด Unlock จาก panel นี้ได้ทันที)
    private Task LockButton_ClickAsync() => SendToTargetsAsync("LockWorkstation", null, "Lock Workstation");
    private async void LockButton_Click(object sender, RoutedEventArgs e) => await LockButton_ClickAsync();

    private Task UnlockButton_ClickAsync() => SendToTargetsAsync("UnlockWorkstation", null, "Unlock Workstation");
    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await UnlockButton_ClickAsync();

    private async void RestartButton_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("Restart");
    private async void ShutdownButton_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("Shutdown");

    /// <summary>Shutdown/Restart - ต้อง confirm เสมอ (destructive, ทำคืนไม่ได้ทันที ต่างจาก Lock/Toggle)</summary>
    private async Task SendPowerActionAsync(string commandType)
    {
        var targets = CurrentTargets;

        if (!Confirm(
            $"ยืนยัน {commandType} เครื่อง {targets.Count} เครื่อง?\n\n" +
            "แต่ละเครื่องจะเตือน user ล่วงหน้า 10 วินาทีก่อนดำเนินการจริง การกระทำนี้ย้อนกลับไม่ได้ทันที",
            $"ยืนยัน {commandType} {targets.Count} เครื่อง"))
        {
            return;
        }

        await SendToTargetsAsync(commandType, null, commandType);
    }

    private async Task SendToTargetsAsync(string commandType, object? payload, string actionLabel)
    {
        var targets = CurrentTargets;

        StatusText.Text = $"กำลังส่งคำสั่ง {actionLabel} ไป {targets.Count} เครื่อง...";
        var successCount = 0;
        foreach (var client in targets)
        {
            if (await _apiClient.SendCommandAsync(client.ClientGuid, commandType, payload))
            {
                successCount++;
            }
        }

        StatusText.Text = $"ส่งคำสั่ง {actionLabel} สำเร็จ {successCount}/{targets.Count} เครื่อง";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        if (!Confirm($"ยืนยันการลบเครื่อง '{_singleClient.MachineName}'? การกระทำนี้ย้อนกลับไม่ได้", "ยืนยันการลบ"))
        {
            return;
        }

        var success = await _apiClient.DeleteClientAsync(_singleClient.Id);
        StatusText.Text = success ? "ลบเครื่องสำเร็จ" : "ลบเครื่องไม่สำเร็จ";

        if (success)
        {
            DataChanged = true;
            DialogResult = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = DataChanged;
        Close();
    }
}
