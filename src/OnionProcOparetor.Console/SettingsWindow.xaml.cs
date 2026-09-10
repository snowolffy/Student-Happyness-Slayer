using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class SettingsWindow : Window
{
    private readonly ApiClient _apiClient;

    public SettingsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var settings = SettingsService.Current;
        VncLimitBox.Text = settings.Vnc.MaxConcurrentSessions.ToString();
        KeepLiveViewOpenCheckBox.IsChecked = settings.Vnc.KeepLiveViewOpenOnTabLeave;
        ShowTailscaleActionsCheckBox.IsChecked = settings.Ui.ShowTailscaleActions;
        PollIntervalBox.Text = settings.Connection.DashboardPollIntervalSeconds.ToString();
        ServerHostBox.Text = settings.Connection.ServerHost;
        ServerPortBox.Text = settings.Connection.ServerPort;
    }

    private void ChangeMyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangeMyPasswordWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusText.Text = "เปลี่ยน password สำเร็จ";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VncLimitBox.Text.Trim(), out var vncLimit))
        {
            StatusText.Text = "VNC max concurrent sessions ต้องเป็นตัวเลข";
            return;
        }

        if (!int.TryParse(PollIntervalBox.Text.Trim(), out var pollInterval))
        {
            StatusText.Text = "Dashboard auto-refresh interval ต้องเป็นตัวเลข";
            return;
        }

        var updated = new ConsoleSettings
        {
            Vnc = new VncSettings
            {
                MaxConcurrentSessions = vncLimit,
                KeepLiveViewOpenOnTabLeave = KeepLiveViewOpenCheckBox.IsChecked ?? false
            },
            Connection = new ConnectionSettings
            {
                ServerHost = ServerHostBox.Text.Trim(),
                ServerPort = ServerPortBox.Text.Trim(),
                DashboardPollIntervalSeconds = pollInterval
            },
            // คัดลอกกลุ่ม Ui เดิมมาทั้งก้อนแล้วทับเฉพาะช่องที่หน้านี้แก้ - ธีมกับสถานะ sidebar ถูกตั้ง
            // จากที่อื่น (ปุ่มบน sidebar) ถ้าสร้าง UiSettings ใหม่เปล่า ๆ ตรงนี้ กดเซฟหน้านี้ทีเดียว
            // ค่าพวกนั้นจะถูกล้างกลับเป็น default ทันที
            Ui = new UiSettings
            {
                Theme = SettingsService.Current.Ui.Theme,
                SidebarCollapsed = SettingsService.Current.Ui.SidebarCollapsed,
                ShowTailscaleActions = ShowTailscaleActionsCheckBox.IsChecked ?? false
            }
        };

        var (success, error) = SettingsService.Save(updated);
        if (!success)
        {
            StatusText.Text = error ?? "บันทึก Settings ไม่สำเร็จ";
            return;
        }

        StatusText.Text = "บันทึก Settings สำเร็จ";
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
