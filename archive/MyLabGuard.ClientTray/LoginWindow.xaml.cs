using System.Windows;
using Microsoft.Extensions.Configuration;
using MyLabGuard.ClientTray.Services;

namespace MyLabGuard.ClientTray;

public partial class LoginWindow : Window
{
    private readonly ClientApiClient _apiClient;
    private readonly string _serverBaseUrl;

    public LoginWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        _serverBaseUrl = config["ServerSettings:BaseUrl"] ?? "http://localhost:8787";
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
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "กำลังตรวจสอบ...";

        // ใช้ username คงที่ "admin" ไปก่อน (ตรงกับ default admin ที่ตั้งไว้ฝั่ง Server)
        // ถ้าต้องการหลาย admin username ทีหลังค่อยเพิ่มช่องกรอก username ได้
        var (success, message) = await _apiClient.LoginToServerAsync(_serverBaseUrl, username, password);

        if (success)
        {
            var statusWindow = new StatusWindow(_apiClient);
            statusWindow.Show();
            Close();
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = message;
            LoginButton.IsEnabled = true;
        }
    }
}