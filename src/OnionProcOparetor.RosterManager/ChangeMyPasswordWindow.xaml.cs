using System.Windows;
using OnionProcOparetor.RosterManager.Services;

namespace OnionProcOparetor.RosterManager;

public partial class ChangeMyPasswordWindow : Window
{
    private readonly ApiClient _apiClient;

    public ChangeMyPasswordWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (newPassword.Length < 6)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = "รหัสผ่านต้องมีอย่างน้อย 6 ตัว";
            return;
        }

        if (newPassword != confirm)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = "รหัสผ่านทั้งสองช่องไม่ตรงกัน";
            return;
        }

        SaveButton.IsEnabled = false;
        var (success, message) = await _apiClient.ChangeMyPasswordAsync(newPassword);

        if (success)
        {
            ToastService.Success("เปลี่ยนรหัสผ่านสำเร็จ");
            var roster = new RosterWindow(_apiClient);
            roster.Show();
            Close();
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            SaveButton.IsEnabled = true;
        }
    }
}
