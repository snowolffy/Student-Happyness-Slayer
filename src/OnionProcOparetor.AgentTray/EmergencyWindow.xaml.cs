using System.Windows;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// "รหัสฉุกเฉิน" (WP3-4, 2026-10-31; ต่อสายเข้า StudentLoginWindow ใน WP3-4b) - ดู comment เต็มใน
/// EmergencyWindow.xaml
/// </summary>
public partial class EmergencyWindow : Window
{
    private readonly ClientApiClient _apiClient;

    /// <summary>ยิงตอนปลดสำเร็จ (WP3-4b, 2026-10-31) - StudentLoginWindow ฟังไว้เพื่อปิดตัวเองทันที (ดูที่นั่น) เมนู tray ไม่ได้ฟัง event นี้ (ไม่มีหน้าต่างให้ปิด)</summary>
    public event Action? UnlockSucceeded;

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
            UnlockSucceeded?.Invoke();
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
