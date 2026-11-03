using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

/// <summary>
/// ถามข้อความทับเฉพาะครั้งก่อนสั่ง LockWorkstation (WP3-5, 2026-10-31) - ตรงตาม wireframe
/// docs/wireframes/con-dialogs.dc.html dialog "ล็อกหน้าจอ"
/// UI-3b (2026-11-03): แปลงจาก Window เป็น UserControl - ดู ShowViaHost
/// </summary>
public partial class LockScreenMessageWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = กดล็อก (อ่าน Message ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    /// <summary>null = เว้นว่าง - ผู้เรียกเติม default message เอง/ปล่อยให้ปลายทางเติม</summary>
    public string? Message { get; private set; }

    public LockScreenMessageWindow()
    {
        InitializeComponent();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        var text = MessageInputBox.Text.Trim();
        Message = string.IsNullOrEmpty(text) ? null : text;
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }

    /// <summary>UI-3b (2026-11-03): เหตุผลและ pattern เดียวกับ AddEditRuleWindow.ShowViaHost - owner
    /// ที่เรียกจริง (ActionPanelWindow) ยังไม่ถูกย้ายโดย UI-3c เลยเดินสาย fallback เสมอตอนนี้</summary>
    public static async Task<bool> ShowViaHost(Window? owner, LockScreenMessageWindow content)
    {
        if (owner is MainWindow mainWindow)
        {
            content.ResultChosen += r => mainWindow.DialogHost.CloseTop(r);
            return await mainWindow.DialogHost.ShowAsync<bool>(content);
        }

        var window = new Window
        {
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Style = (Style)Application.Current.FindResource("AppDialogWindow"),
            Content = content,
        };
        var result = false;
        content.ResultChosen += r => { result = r; window.Close(); };
        window.ShowDialog();
        return result;
    }
}
