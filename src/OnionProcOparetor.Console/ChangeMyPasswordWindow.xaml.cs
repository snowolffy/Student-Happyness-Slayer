using System;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// เปลี่ยน password ของ admin ที่ login อยู่ตอนนี้ (ตัวเอง) - เปิดจาก SettingsWindow ได้ทุกเมื่อ
/// (ต่างจาก ForceChangePasswordWindow ที่บังคับเฉพาะตอน login ครั้งแรกด้วย default password ว่างเปล่า)
/// ใช้ ApiClient.ChangePasswordAsync ตัวเดียวกัน - Server เช็ค admin.Id == id เองอยู่แล้ว (ดู
/// Program.cs's /api/admin/users/{id}/change-password) เท่ากับ "เปลี่ยนได้เฉพาะของ account ที่ login
/// อยู่จริงเท่านั้น" ไม่ต้องเช็คซ้ำฝั่งนี้อีก
/// </summary>
public partial class ChangeMyPasswordWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = เปลี่ยนรหัสสำเร็จ false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    private readonly ApiClient _apiClient;

    public ChangeMyPasswordWindow(ApiClient apiClient)
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
            // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = "Password ต้องมีความยาวอย่างน้อย 8 ตัวอักษร";
            return;
        }

        if (newPassword != confirmPassword)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = "Password ทั้งสองช่องไม่ตรงกัน";
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Offline");
        StatusText.Text = "กำลังเปลี่ยน password...";

        var (success, message) = await _apiClient.ChangePasswordAsync(newPassword);

        if (success)
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
            StatusText.Text = "เปลี่ยน password สำเร็จ";
            ResultChosen?.Invoke(true);
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ChangePasswordButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
