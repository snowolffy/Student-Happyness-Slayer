using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class ForceChangePasswordWindow : UserControl
{
    private readonly ApiClient _apiClient;

    /// <summary>
    /// UI-3a (2026-11-03): เดิม (ก่อน UI-2) เปิด DashboardWindow เอง ตอนเป็น Window แยก - ตอน UI-2
    /// เปลี่ยนให้แค่รายงานผลผ่าน Window.DialogResult กลับไปให้ MainWindow.ConnectButton_Click (เรียก
    /// ShowDialog()) อ่านต่อ ตอนนี้ (UI-3a) ย้ายมาฝังใน DialogHost แทน Window ของตัวเองไปเลย - รายงานผล
    /// ผ่าน <see cref="ResultChosen"/> (true = เปลี่ยนรหัสสำเร็จ, false = ผู้ใช้กด Esc/ยกเลิก) แทน
    /// DialogResult ให้ผู้เปิด (DialogHost.ShowAsync หรือ caller อื่นในอนาคต) เป็นคนปิด dialog เอง
    /// </summary>
    public ForceChangePasswordWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    /// <summary>true = เปลี่ยนรหัสสำเร็จแล้ว - ไม่ raise เลยถ้าผู้ใช้กด Esc (DialogHost ปิดเองด้วยผล
    /// null/default ซึ่งตรงกับ false พอดีอยู่แล้ว ไม่ต้อง raise ซ้ำ)</summary>
    public event System.Action<bool>? ResultChosen;

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

            // UI-3a (2026-11-03): ไม่เปิด DashboardWindow เอง และไม่ปิด Window ของตัวเองแล้ว (ดู
            // comment บน constructor) - แค่ raise ResultChosen(true) ให้ MainWindow (ผู้เปิดผ่าน
            // DialogHost.ShowAsync) เป็นคนปิด dialog แล้วเปิด Dashboard ต่อ
            ResultChosen?.Invoke(true);
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ChangePasswordButton.IsEnabled = true;
        }
    }
}
