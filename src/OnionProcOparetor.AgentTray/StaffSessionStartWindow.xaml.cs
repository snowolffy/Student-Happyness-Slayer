using System.Windows;
using Microsoft.Extensions.Configuration;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

/// <summary>
/// ครูล็อกอินด้วยบัญชี Console เพื่อเริ่ม Staff session (WP3-3, 2026-10-31) - ดู doc comment ใน .xaml
/// ใช้ ClientApiClient.LoginToServerAsync + SetBypassAsync เดิมจาก WP2 ทั้งชุด (bypass = staff session
/// ที่ Agent มองเห็น - ดู BypassCoordinator) ที่นี่แค่เพิ่ม UI เลือกเวลา (15/30/60 นาที) แล้วเริ่ม
/// bypass ทันทีหลัง login สำเร็จ ไม่ต้องกดปุ่ม BYPASS แยกอีกขั้นแบบ StatusWindow
/// </summary>
public partial class StaffSessionStartWindow : Window
{
    private readonly ClientApiClient _apiClient;
    private readonly string _serverBaseUrl;
    private readonly string? _fallbackBaseUrl;

    /// <summary>ยิงตอนเริ่ม Staff session สำเร็จจริง (bypass เปิดแล้ว) - App.xaml.cs ฟังไว้เปิด StaffSessionBadgeWindow + ซ่อนจอล็อกอินนักเรียนชั่วคราว</summary>
    public event Action<int>? StaffSessionStarted;

    public StaffSessionStartWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        _serverBaseUrl = config["ServerSettings:BaseUrl"] ?? "http://localhost:8787";
        _fallbackBaseUrl = config["ServerSettings:FallbackBaseUrl"];
    }

    private int SelectedMinutes => Minutes15.IsChecked == true ? 15 : Minutes60.IsChecked == true ? 60 : 30;

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอก username และ password";
            return;
        }

        StartButton.IsEnabled = false;
        StatusText.Text = "กำลังตรวจสอบ...";

        var (success, message) = await _apiClient.LoginToServerAsync(_serverBaseUrl, _fallbackBaseUrl, username, password, SelectedMinutes);
        if (!success)
        {
            StatusText.Text = message;
            StartButton.IsEnabled = true;
            return;
        }

        var (bypassed, agentReachable) = await _apiClient.SetBypassAsync(true);
        if (bypassed != true)
        {
            StatusText.Text = agentReachable
                ? "เริ่ม Staff session ไม่สำเร็จ (Agent ปฏิเสธ token - ลองใหม่อีกครั้ง)"
                : "ติดต่อ Agent บนเครื่องนี้ไม่ได้";
            StartButton.IsEnabled = true;
            return;
        }

        StaffSessionStarted?.Invoke(SelectedMinutes);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
