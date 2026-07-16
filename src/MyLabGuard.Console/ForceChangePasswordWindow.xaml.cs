using System.Windows;
using MyLabGuard.Console.Services;

namespace MyLabGuard.Console;

public partial class ForceChangePasswordWindow : Window
{
    private readonly ApiClient _apiClient;

    public ForceChangePasswordWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            return;
        }

        if (newPassword != confirmPassword)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = "Password ทั้งสองช่องไม่ตรงกัน";
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        StatusText.Text = "กำลังเปลี่ยน password...";

        var (success, message) = await _apiClient.ChangePasswordAsync(newPassword);

        if (success)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
            StatusText.Text = "เปลี่ยน password สำเร็จ กำลังเปิด Dashboard...";

            var dashboard = new DashboardWindow(_apiClient);
            dashboard.Show();
            Close();
        }
        else
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusText.Text = message;
            ChangePasswordButton.IsEnabled = true;
        }
    }
}