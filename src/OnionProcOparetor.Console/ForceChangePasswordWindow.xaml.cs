using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ForceChangePasswordWindow : Window
{
    private readonly ApiClient _apiClient;

    /// <summary>
    /// UI-2 (2026-11-03): เดิมหน้านี้เปิด DashboardWindow เอง (ตอน DashboardWindow ยังเป็น Window แยก)
    /// ตอนนี้ DashboardWindow เป็น UserControl ที่ฝังอยู่ใน MainWindow (host window เดียวตลอด process)
    /// แล้ว เลยย้ายหน้าที่ "เปิด Dashboard" ไปให้ MainWindow ทำต่อเอง - หน้าต่างนี้แค่รายงานผลผ่าน
    /// DialogResult (true = เปลี่ยนรหัสสำเร็จ) ให้ MainWindow.ConnectButton_Click ที่เรียก ShowDialog()
    /// อ่านต่อ (ดูที่นั่น)
    /// </summary>
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
            StatusText.Text = "เปลี่ยน password สำเร็จ กำลังเปิด Dashboard...";

            // UI-2 (2026-11-03): ไม่เปิด DashboardWindow เองแล้ว (ดู comment บน constructor) - แค่ปิดตัวเอง
            // แบบสำเร็จ ให้ MainWindow (ผู้เปิดหน้าต่างนี้ด้วย ShowDialog) เป็นคนเปิด Dashboard ต่อ
            DialogResult = true;
            Close();
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ChangePasswordButton.IsEnabled = true;
        }
    }
}
