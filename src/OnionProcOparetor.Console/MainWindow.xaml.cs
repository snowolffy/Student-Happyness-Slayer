using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class MainWindow : Window
{
    // ไม่ใช่ readonly โดยเจตนา - สร้างใหม่ทุกครั้งที่กด Connect (ดู ConnectButton_Click) เพราะ HttpClient
    // ห้ามแก้ BaseAddress ซ้ำหลังส่ง request แรกไปแล้ว (throw InvalidOperationException ทันที) การ reuse
    // instance เดิมข้าม Connect attempt เดิมทำให้กด Connect ครั้งที่ 2 (ไม่ว่าจะกรอกรหัสถูกหรือผิด) crash
    // ทั้งโปรแกรมเสมอ เพราะ exception เกิดใน SetServer ก่อนเช็ค password ด้วยซ้ำ (ยืนยันจาก stack trace จริง
    // ใน Event Log ไม่ได้เดา) - instance ที่ login สำเร็จค่อยถูกส่งต่อให้ ForceChangePasswordWindow/DashboardWindow
    private ApiClient _apiClient = new();

    public MainWindow()
    {
        InitializeComponent();

        // เติมค่า default จาก Settings แทน hardcode "localhost"/"8787" ใน XAML เดิม
        var connection = SettingsService.Current.Connection;
        ServerIpBox.Text = connection.ServerHost;
        PortBox.Text = connection.ServerPort;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var serverIp = ServerIpBox.Text.Trim();
        var port = PortBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        // หมายเหตุ: ไม่เช็ค password ว่างเปล่าที่นี่โดยเจตนา
        // เพราะ built-in "Administrator" ใช้ password ว่างเปล่าเป็นค่า default ตอนยังไม่เคยเปลี่ยน
        // (server จะบังคับให้เปลี่ยน password หลัง login สำเร็จอยู่แล้วผ่าน hasDefaultPassword flag)
        if (string.IsNullOrEmpty(serverIp) || string.IsNullOrEmpty(username))
        {
            StatusText.Text = "กรุณากรอก Server IP และ Username";
            return;
        }

        var serverAddress = string.IsNullOrEmpty(port) ? serverIp : $"{serverIp}:{port}";

        ConnectButton.IsEnabled = false;
        // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Offline");
        StatusText.Text = "กำลังเชื่อมต่อ...";

        _apiClient = new ApiClient();
        _apiClient.SetServer(serverAddress);
        var (success, message) = await _apiClient.LoginAsync(username, password);

        if (success)
        {
            SaveConnectionAsDefault(serverIp, port);

            if (_apiClient.HasDefaultPassword)
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กรุณาเปลี่ยน password ก่อนใช้งาน...";

                var forceChangeWindow = new ForceChangePasswordWindow(_apiClient);
                forceChangeWindow.Show();
                Close();
            }
            else
            {
                StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Status.Online");
                StatusText.Text = "เชื่อมต่อสำเร็จ กำลังเปิด Dashboard...";

                var dashboard = new DashboardWindow(_apiClient);
                dashboard.Show();
                Close();
            }
        }
        else
        {
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// จำ Server IP/Port ที่ login สำเร็จไว้เป็นค่า default ครั้งถัดไปที่เปิด Console (ดู Settings > DEFAULT
    /// SERVER HOST/PORT) - ไม่ critical ถ้าบันทึกไม่สำเร็จ (เช่น เขียนไฟล์ไม่ได้) เลยไม่โชว์ error ให้ผู้ใช้
    /// ตรงนี้ แค่ล้มเหลวเงียบๆ แล้วครั้งถัดไปยังใช้ค่าเดิมที่เคยบันทึกไว้ (หรือ default ของ ConsoleSettings)
    /// </summary>
    private static void SaveConnectionAsDefault(string serverHost, string serverPort)
    {
        var current = SettingsService.Current;
        var updated = new ConsoleSettings
        {
            Vnc = current.Vnc,
            Ui = current.Ui,
            Connection = new ConnectionSettings
            {
                ServerHost = serverHost,
                ServerPort = string.IsNullOrEmpty(serverPort) ? current.Connection.ServerPort : serverPort,
                DashboardPollIntervalSeconds = current.Connection.DashboardPollIntervalSeconds
            }
        };

        SettingsService.Save(updated);
    }

    private void ServerIpBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {

    }
}
