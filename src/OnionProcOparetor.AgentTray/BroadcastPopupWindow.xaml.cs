using System.Windows;
using System.Windows.Threading;

namespace OnionProcOparetor.AgentTray;

public partial class BroadcastPopupWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public BroadcastPopupWindow(string? title, string message)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "ข้อความจากครู" : title;
        MessageText.Text = message;

        // ปิดอัตโนมัติหลัง 20 วิ ถ้า user ไม่ปิดเอง (กันหน้าจอค้าง popup ถ้าไม่มีใครอยู่หน้าเครื่อง)
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
