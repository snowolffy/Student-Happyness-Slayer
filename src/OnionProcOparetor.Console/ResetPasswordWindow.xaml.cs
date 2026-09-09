using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ResetPasswordWindow : Window
{
    public string? NewPassword { get; private set; }

    public ResetPasswordWindow(string username)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);
        TitleText.Text = $"Reset Password - {username}";
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var password = NewPasswordBox.Password;

        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
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