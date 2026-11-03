using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// แสดง+ยกเลิกคำสั่งที่ยังค้าง Pending อยู่ของเครื่อง client ที่ระบุ (เปิดจาก ActionPanelWindow →
/// PENDING COMMANDS...) ต่างจาก ActionReportWindow ตรงที่ไม่ผูกกับ BatchId/session การสั่งครั้งไหน
/// เลย - ดึงสถานะปัจจุบันจริงจาก Server ทุกครั้ง ใช้ได้แม้พลาดหน้าต่างตอนสั่งไปแล้วหรือ Console
/// เปิดใหม่ทับ (เช่น command ที่ค้างจากบั๊ก Restart/Shutdown ack ไม่ทันตอนใช้ Tailscale fallback)
/// </summary>
public partial class PendingCommandsWindow : UserControl
{
    private readonly ApiClient _apiClient;
    private readonly ClientMachineDto _client;

    /// <summary>UI-3c (2026-11-03): raise ตอนกดปิด - caller (SidePanelHost) ปิด host เอง</summary>
    public event System.Action? CloseRequested;

    public PendingCommandsWindow(ApiClient apiClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _client = client;

        MachineNameText.Text = $"Pending Commands - {client.MachineName}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "กำลังโหลดคำสั่งที่ค้างอยู่...";
            CommandsGrid.ItemsSource = null;
            EmptyText.Visibility = Visibility.Collapsed;

            var pending = await _apiClient.GetPendingCommandsAsync(_client.ClientGuid);

            CommandsGrid.ItemsSource = pending;
            EmptyText.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = pending.Count == 0
                ? "ไม่มีคำสั่งที่ค้างอยู่"
                : $"พบ {pending.Count} คำสั่งที่ยังค้าง Pending อยู่";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"เกิดข้อผิดพลาดขณะโหลดรายการคำสั่ง: {ex.Message}";
        }
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CommandResultDto dto } button)
        {
            return;
        }

        button.IsEnabled = false;
        StatusText.Text = $"กำลังยกเลิกคำสั่ง #{dto.CommandId} ({dto.CommandType})...";

        var (success, message) = await _apiClient.CancelCommandAsync(dto.CommandId);

        if (!success)
        {
            button.IsEnabled = true;
            StatusText.Text = message ?? "ยกเลิกไม่สำเร็จ";
            return;
        }

        StatusText.Text = $"ยกเลิกคำสั่ง #{dto.CommandId} สำเร็จ";
        await RefreshAsync(); // โหลดใหม่ - command ที่ยกเลิกแล้วต้องหายจาก list นี้เพราะไม่ Pending อีกต่อไป
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
