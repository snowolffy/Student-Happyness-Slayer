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

        StatusText.Text = "กำลังบันทึก...";
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Status.Offline");

        var success = await _apiClient.UpdateClientSettingsAsync(_client.Id, overrideSeconds);

        if (success)
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

    private async void ClearOverrideButton_Click(object sender, RoutedEventArgs e)
    {
        PollIntervalOverrideBox.Text = "";
        var success = await _apiClient.UpdateClientSettingsAsync(_client.Id, null);

        if (success)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.Text = "ล้างค่าไม่สำเร็จ ลองใหม่อีกครั้ง";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Default");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}