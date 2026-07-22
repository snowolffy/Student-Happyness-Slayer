using System.Windows;

namespace OnionProcOparetor.Console;

public partial class ResetPasswordWindow : Window
{
    public string? NewPassword { get; private set; }

    public ResetPasswordWindow(string username)
    {
        InitializeComponent();
        TitleText.Text = $"Reset Password - {username}";
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var password = NewPasswordBox.Password;

        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Default");
            return;
        }

        NewPassword = password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}