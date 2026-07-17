using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

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

        // หมายเหตุ: ไม่เช็ค password ว่างเปล่าที่นี่โดยเจตนา
        // เพราะ built-in "Administrator" ใช้ password ว่างเปล่าเป็นค่า default ตอนยังไม่เคยเปลี่ยน
        // (server จะบังคับให้เปลี่ยน password หลัง login สำเร็จอยู่แล้วผ่าน hasDefaultPassword flag)
        if (string.IsNullOrEmpty(serverAddress) || string.IsNullOrEmpty(username))
        {
            StatusText.Text = "กรุณากรอก Server address และ Username";
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "กำลังเชื่อมต่อ...";

        _apiClient.SetServer(serverAddress);
        var (success, message) = await _apiClient.LoginAsync(username, password);

        if (success)
        {
            if (_apiClient.HasDefaultPassword)
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                StatusText.Text = "เชื่อมต่อสำเร็จ กรุณาเปลี่ยน password ก่อนใช้งาน...";

                var forceChangeWindow = new ForceChangePasswordWindow(_apiClient);
                forceChangeWindow.Show();
                Close();
            }
            else
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Dashboard...";

                var dashboard = new DashboardWindow(_apiClient);
                dashboard.Show();
                Close();
            }
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
        }
    }
}
