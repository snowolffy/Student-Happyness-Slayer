using System.Windows;
using OnionProcOparetor.RemoteBridge.Services;

namespace OnionProcOparetor.RemoteBridge;

public partial class StatusWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly RemoteBridgeConfig _config;

    public StatusWindow(ApiClient apiClient, RemoteBridgeConfig config)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _config = config;

        this.EnableHideOnClose();

        RemoteIdText.Text = string.IsNullOrEmpty(_config.RemoteId) ? "(ไม่ได้ตั้งชื่อ)" : _config.RemoteId;
        ServerText.Text = _config.ServerUrl;
        UsernameText.Text = _config.Username;

        FunnelCommandText.Text = $"tailscale funnel {_config.LocalListenerPort}";
        RemoteSecretText.Text = _config.RemoteSecret;
        TriggerUrlExampleText.Text = $"<funnel-url>/trigger?secret={_config.RemoteSecret}&locked=true";

        _ = EnsureLoggedInAsync();
    }

    /// <summary>
    /// เปิดโปรแกรมรอบถัดๆ ไปจะมาจาก config ที่บันทึกไว้ ไม่ผ่านหน้า LoginWindow เลย - ต้อง login
    /// เองที่นี่ก่อนใช้งานได้ (เช่นตอนเปิดเครื่องใหม่ หรือ token เดิมหมดอายุไปแล้วตั้งแต่ก่อนปิดโปรแกรม)
    /// </summary>
    private async Task EnsureLoggedInAsync()
    {
        if (_apiClient.IsLoggedIn)
        {
            StatusText.Text = "เชื่อมต่ออยู่";
            LockButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(_config.EncryptedPassword))
        {
            StatusText.Text = "ไม่มี password ที่บันทึกไว้ - กรุณา login ใหม่ผ่านหน้า Login";
            return;
        }

        StatusText.Text = "กำลังเชื่อมต่อ...";
        if (!_apiClient.SetServer(_config.ServerUrl))
        {
            StatusText.Text = "Server ที่บันทึกไว้มีรูปแบบไม่ถูกต้อง - กรุณา login ใหม่ผ่านหน้า Login";
            return;
        }

        string password;
        try
        {
            password = ConfigStore.DecryptPassword(_config.EncryptedPassword);
        }
        catch
        {
            // ถอดรหัสไม่ได้ (เช่น config ถูกก็อปมาจากเครื่อง/user account อื่น - DPAPI ผูกกับ user
            // account ของเครื่องที่เข้ารหัสไว้เท่านั้น) ไม่มีทางกู้ password กลับมาได้ ต้อง login ใหม่
            StatusText.Text = "ถอดรหัส password ไม่สำเร็จ - กรุณา login ใหม่ผ่านหน้า Login";
            return;
        }

        var (success, message) = await _apiClient.LoginAsync(_config.Username, password);
        StatusText.Text = success ? "เชื่อมต่ออยู่" : $"เชื่อมต่อไม่สำเร็จ: {message}";
        LockButton.IsEnabled = success;
        UnlockButton.IsEnabled = success;
    }

    private async void LockButton_Click(object sender, RoutedEventArgs e) => await BroadcastAsync(locked: true);

    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await BroadcastAsync(locked: false);

    private async Task BroadcastAsync(bool locked)
    {
        LockButton.IsEnabled = false;
        UnlockButton.IsEnabled = false;
        StatusText.Text = "กำลังดึงรายชื่อเครื่อง...";

        var (clientsOk, machines, clientsMessage) = await _apiClient.GetClientsAsync();
        if (!clientsOk || machines is null)
        {
            StatusText.Text = clientsMessage;
            LockButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
            return;
        }

        var actionLabel = locked ? "Lock" : "Unlock";
        StatusText.Text = $"กำลังส่งคำสั่ง {actionLabel} ไป {machines.Count} เครื่อง...";

        var (successCount, failCount) = await _apiClient.BroadcastLockAsync(locked, machines.Select(m => m.ClientGuid));
        StatusText.Text = $"{actionLabel} สำเร็จ {successCount} เครื่อง, ล้มเหลว {failCount} เครื่อง";

        LockButton.IsEnabled = true;
        UnlockButton.IsEnabled = true;
    }
}
