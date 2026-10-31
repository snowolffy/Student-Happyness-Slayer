using System.Windows;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// "รหัสฉุกเฉิน..." จากเมนู tray (WP3-4, 2026-10-31) - ดู comment เต็มใน EmergencyWindow.xaml ว่าทำไม
/// เป็นหน้าต่างแยก ไม่ได้แทนที่ LoginWindow/LockScreenWindow จริง (งานของ WP3-3)
/// </summary>
public partial class EmergencyWindow : Window
{
    private readonly ClientApiClient _apiClient;

    public EmergencyWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _ = CheckConnectivityAsync();
    }

    private async Task CheckConnectivityAsync()
    {
        var status = await _apiClient.GetStatusAsync();
        if (status is null)
        {
            ConnectivityText.Text = "ติดต่อ Agent Service ไม่ได้เลย";
        }
        else if (!status.LastPollSucceeded)
        {
            ConnectivityText.Text = "ติดต่อ Server ไม่ได้ - ใช้รหัสฉุกเฉินปลดเครื่องชั่วคราวได้";
        }
        else
        {
            ConnectivityText.Text = "ยังต่อ Server ได้อยู่ - ปกติไม่ต้องใช้รหัสนี้ (ใช้ล็อกอินตามปกติแทน)";
        }
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            StatusText.Text = "กรอกรหัส 6 หลักก่อน";
            return;
        }

        UnlockButton.IsEnabled = false;
        StatusText.Text = "กำลังตรวจสอบ...";

        var result = await _apiClient.VerifyEmergencyCodeAsync(code);

        if (result == true)
        {
            StatusText.Text = "ปลดล็อกสำเร็จ - ใช้งานเครื่องนี้แบบไม่ระบุชื่อได้แล้ว";
            UnlockButton.Content = "ปลดล็อกแล้ว";
        }
        else if (result == false)
        {
            StatusText.Text = "รหัสไม่ถูกต้อง หรือเครื่องนี้ยังไม่ได้ตั้งค่ารหัสฉุกเฉินไว้";
            UnlockButton.IsEnabled = true;
        }
        else
        {
            StatusText.Text = "ติดต่อ Agent Service ไม่ได้ - ลองใหม่อีกครั้ง";
            UnlockButton.IsEnabled = true;
        }
    }
}
