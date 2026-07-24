using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ClientSettingsWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly ClientMachineDto _client;

    public ClientSettingsWindow(ApiClient apiClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _client = client;

        MachineNameText.Text = $"Settings - {client.MachineName}";
        PollIntervalOverrideBox.Text = client.PollIntervalOverrideSeconds?.ToString() ?? "";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var input = PollIntervalOverrideBox.Text.Trim();
        int? overrideSeconds;

        if (string.IsNullOrEmpty(input))
        {
            overrideSeconds = null;
        }
        else if (int.TryParse(input, out var parsed) && parsed > 0)
        {
            overrideSeconds = parsed;
        }
        else
        {
            StatusText.Text = "กรุณากรอกตัวเลขจำนวนเต็มบวก หรือเว้นว่างไว้เพื่อใช้ค่า default";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Default");
            return;
        }

        await ApplySettingAsync(overrideSeconds);
    }

    private async void ClearOverrideButton_Click(object sender, RoutedEventArgs e)
    {
        PollIntervalOverrideBox.Text = "";
        await ApplySettingAsync(null);
    }

    /// <summary>
    /// ทางหลัก: push ผ่าน SendCommandAsync (SignalR) ทันที ให้ Agent ที่ connected อยู่ตอนนี้ apply
    /// effect ทันทีไม่ต้องรอ poll รอบถัดไป (ดู CommandProcessor.HandleUpdateSettingsAsync ฝั่ง Agent)
    /// ถือว่า "ส่งสำเร็จ" คือจบ ไม่รอ ack กลับมาก่อนถึงจะอัปเดต UI - เขียนลง DB (ที่มาของ poll
    /// safety-net เดิม) แบบ fire-and-forget คู่ขนานไปด้วยเพื่อให้ค่าที่แสดงผล/poll สอดคล้องกัน
    ///
    /// ถ้า SendCommandAsync ล้มเหลวจริงๆ (เช่น server ต่อไม่ได้เลย) ค่อย fallback ไปรอผลของการเขียน
    /// DB ตรงแทน (ทางเดียวที่เหลือให้ persist ได้ตอนนี้)
    /// </summary>
    private async Task ApplySettingAsync(int? overrideSeconds)
    {
        StatusText.Text = "กำลังส่งคำสั่ง...";
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Status.Offline");

        var payload = new { pollIntervalOverrideSeconds = overrideSeconds };
        var commandSent = await _apiClient.SendCommandAsync(_client.ClientGuid, "UpdateSettings", payload);

        if (commandSent)
        {
            _ = _apiClient.UpdateClientSettingsAsync(_client.Id, overrideSeconds);
            DialogResult = true;
            Close();
            return;
        }

        var dbSuccess = await _apiClient.UpdateClientSettingsAsync(_client.Id, overrideSeconds);
        if (dbSuccess)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "บันทึกไม่สำเร็จ ลองใหม่อีกครั้ง";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Default");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}