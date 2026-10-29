using System.Windows;
using OnionProcOparetor.RemoteBridge.Services;

namespace OnionProcOparetor.RemoteBridge;

public partial class LoginWindow : Window
{
    private readonly ApiClient _apiClient;

    public LoginWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        // กากบาท = ซ่อน ไม่ใช่ปิด (ดู HideOnCloseWindowBehavior) - RemoteBridge ต้องอยู่ตลอด
        this.EnableHideOnClose();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var serverUrl = ServerUrlBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var remoteId = RemoteIdBox.Text.Trim();

        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "กรุณากรอก Server, Username และ Password ให้ครบ";
            return;
        }

        if (!_apiClient.SetServer(serverUrl))
        {
            StatusText.Text = "รูปแบบ Server ไม่ถูกต้อง (ตัวอย่าง: 192.168.1.10:8787)";
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Text = "กำลังเชื่อมต่อ...";

        var (success, message) = await _apiClient.LoginAsync(username, password);

        if (!success)
        {
            StatusText.Text = message;
            ConnectButton.IsEnabled = true;
            return;
        }

        var config = new RemoteBridgeConfig
        {
            ServerUrl = serverUrl,
            Username = username,
            RemoteId = remoteId,
            EncryptedPassword = ConfigStore.EncryptPassword(password),
            RemoteSecret = ConfigStore.GenerateRemoteSecret()
        };
        ConfigStore.Save(config);

        var statusWindow = new StatusWindow(_apiClient, config);
        statusWindow.Show();

        // ForceClose ไม่ใช่ Close() ธรรมดา - นี่คือปิดที่ "โค้ดตั้งใจ" (login เสร็จแล้วให้ StatusWindow
        // ทำงานแทน) ถ้าใช้ Close() เฉยๆ hide-on-close จะยกเลิกให้แล้วซ่อนไว้แทน ทำให้หน้า login
        // ค้างอยู่เบื้องหลัง แล้วครั้งถัดไปที่เปิดจาก tray จะเจอหน้า login ที่ซ่อนอยู่ก่อน StatusWindow
        this.ForceClose();
    }
}
