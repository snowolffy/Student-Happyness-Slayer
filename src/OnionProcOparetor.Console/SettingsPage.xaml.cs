using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// หน้าตั้งค่าใน shell (WP6) - เดิมคือ SettingsWindow ค่าที่ตั้งได้และการตรวจค่า (SettingsService.Save)
/// เหมือนเดิมทุกอย่าง ผลการบันทึก/error ไปที่ toast
/// </summary>
public partial class SettingsPage : UserControl
{
    private ApiClient? _apiClient;

    public SettingsPage()
    {
        InitializeComponent();

        // เติมใหม่จากค่าที่บันทึกไว้จริงทุกครั้งที่สลับมาหน้านี้ - ค่าบางตัว (ธีม/sidebar) ถูกเปลี่ยนจาก
        // ที่อื่นได้ระหว่างที่ไม่ได้อยู่หน้านี้ และของที่พิมพ์ค้างไว้โดยไม่กดบันทึกไม่ควรดูเหมือนถูกบันทึกแล้ว
        // ใช้ IsVisibleChanged ไม่ใช่ Loaded - Loaded ของเนื้อหาแท็บยิงตั้งแต่เปิด Dashboard ครั้งเดียว
        // (ดู ProgramReviewPage) ไม่ได้ยิงทุกครั้งที่สลับกลับมา
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                LoadFromSettings();
            }
        };
    }

    /// <summary>เรียกครั้งเดียวจาก DashboardWindow หลัง InitializeComponent</summary>
    public void Initialize(ApiClient apiClient) => _apiClient = apiClient;

    private void LoadFromSettings()
    {
        var settings = SettingsService.Current;
        VncLimitBox.Text = settings.Vnc.MaxConcurrentSessions.ToString();
        KeepLiveViewOpenCheckBox.IsChecked = settings.Vnc.KeepLiveViewOpenOnTabLeave;
        ShowTailscaleActionsCheckBox.IsChecked = settings.Ui.ShowTailscaleActions;
        PollIntervalBox.Text = settings.Connection.DashboardPollIntervalSeconds.ToString();
        ServerHostBox.Text = settings.Connection.ServerHost;
        ServerPortBox.Text = settings.Connection.ServerPort;
        ThaiTimeRadio.IsChecked = !settings.Ui.ShowTimesInUtc;
        UtcTimeRadio.IsChecked = settings.Ui.ShowTimesInUtc;
    }

    private void ChangeMyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null)
        {
            return;
        }

        var dialog = new ChangeMyPasswordWindow(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ToastService.Success("เปลี่ยนรหัสผ่านของคุณแล้ว");
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        ToastService.Info("คืนค่าที่บันทึกไว้แล้ว");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VncLimitBox.Text.Trim(), out var vncLimit))
        {
            ToastService.Error("จำนวนหน้าจอที่เปิดดูพร้อมกันต้องเป็นตัวเลข");
            return;
        }

        if (!int.TryParse(PollIntervalBox.Text.Trim(), out var pollInterval))
        {
            ToastService.Error("รอบรีเฟรชอัตโนมัติต้องเป็นตัวเลข (วินาที)");
            return;
        }

        var current = SettingsService.Current;
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
            // จากที่อื่น (ปุ่มบน sidebar) ถ้าสร้าง UiSettings ใหม่เปล่า ๆ ตรงนี้ กดบันทึกหน้านี้ทีเดียว
            // ค่าพวกนั้นจะถูกล้างกลับเป็น default ทันที
            Ui = new UiSettings
            {
                Theme = current.Ui.Theme,
                SidebarCollapsed = current.Ui.SidebarCollapsed,
                ShowTailscaleActions = ShowTailscaleActionsCheckBox.IsChecked ?? false,
                ShowTimesInUtc = UtcTimeRadio.IsChecked == true
            }
        };

        var (success, error) = SettingsService.Save(updated);
        if (!success)
        {
            ToastService.Error(error ?? "บันทึกการตั้งค่าไม่สำเร็จ");
            return;
        }

        ToastService.Success("บันทึกการตั้งค่าแล้ว");
    }
}
