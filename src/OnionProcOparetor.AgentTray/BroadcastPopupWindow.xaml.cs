using System.Runtime.InteropServices;
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

    /// <summary>นักเรียนกำลังเลือกข้อความ = ยังอ่าน/คัดลอกอยู่ - ยกเลิกปิดอัตโนมัติ 20 วิ ไม่งั้น popup หายกลางคันตอนลากเลือก</summary>
    private void MessageText_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _autoCloseTimer.Stop();

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop(); // กดคัดลอกแล้ว ไม่ปิดเองให้
        var original = "คัดลอก";
        CopyButton.IsEnabled = false;
        CopyButton.Content = TryCopy(MessageText.Text) ? "คัดลอกแล้ว" : "คัดลอกไม่สำเร็จ";
        await Task.Delay(1500);
        CopyButton.Content = original;
        CopyButton.IsEnabled = true;
    }

    /// <summary>คลิปบอร์ดอาจถูกโปรแกรมอื่นล็อกชั่วขณะ (CLIPBRD_E_CANTOPEN) - ลองซ้ำสั้น ๆ แล้วยอมแพ้เงียบ ๆ (คืน false) ไม่ให้ popup พัง</summary>
    private static bool TryCopy(string text)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(60);
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
