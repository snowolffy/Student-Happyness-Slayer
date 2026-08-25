using System.Windows;
using Microsoft.Extensions.Configuration;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

public partial class LoginWindow : Window
{
    private readonly ClientApiClient _apiClient;
    private readonly string _serverBaseUrl;
    private readonly string? _fallbackBaseUrl;

    public LoginWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        // กากบาท = ซ่อน ไม่ใช่ปิด + ไม่โผล่บน taskbar (ดู HideOnCloseWindowBehavior)
        this.EnableHideOnClose();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        _serverBaseUrl = config["ServerSettings:BaseUrl"] ?? "http://localhost:8787";
        _fallbackBaseUrl = config["ServerSettings:FallbackBaseUrl"];
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอก username และ password";
            return;
        }

        LoginButton.IsEnabled = false;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentNeutralBrush");
        StatusText.Text = "กำลังตรวจสอบ...";

        // ใช้ username คงที่ "admin" ไปก่อน (ตรงกับ default admin ที่ตั้งไว้ฝั่ง Server)
        var (success, message) = await _apiClient.LoginToServerAsync(_serverBaseUrl, _fallbackBaseUrl, username, password);

        if (success)
        {
            var statusWindow = new StatusWindow(_apiClient);
            statusWindow.Show();

            // ต้อง ForceClose ไม่ใช่ Close() ธรรมดา - นี่คือการปิดที่ "โค้ดตั้งใจ" (login เสร็จแล้ว
            // ให้ StatusWindow ทำงานแทน) ไม่ใช่ user กดกากบาท ถ้าใช้ Close() เฉยๆ hide-on-close
            // จะยกเลิกให้แล้วซ่อนไว้แทน ทำให้หน้า login ค้างอยู่ในระบบตลอดและตอนกด
            // "Open Status..." ครั้งถัดไปจะเจอหน้า login ที่ซ่อนอยู่ก่อน แทนที่จะเป็นหน้า Status
            this.ForceClose();
        }
        else
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
            StatusText.Text = message;
            LoginButton.IsEnabled = true;
        }
    }
}
