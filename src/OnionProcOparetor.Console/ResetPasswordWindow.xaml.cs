using System;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ResetPasswordWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = ตั้งรหัสผ่านใหม่แล้ว (อ่าน NewPassword ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

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
            // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        NewPassword = password;
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}