using System.Windows;
using MyLabGuard.Console.Services;

namespace MyLabGuard.Console;

public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var serverAddress = ServerAddressBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(serverAddress) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอกข้อมูลให้ครบ";
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "กำลังเชื่อมต่อ...";

        _apiClient.SetServer(serverAddress);
        var (success, message) = await _apiClient.LoginAsync(username, password);

        if (success)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
            StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Dashboard...";

            var dashboard = new DashboardWindow(_apiClient);
            dashboard.Show();
            Close();
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
        }
    }
}