using System.Windows;

namespace OnionProcOparetor.Console;

/// <summary>
/// ถามข้อความทับเฉพาะครั้งก่อนสั่ง LockWorkstation (WP3-5, 2026-10-31) - ตรงตาม wireframe
/// docs/wireframes/con-dialogs.dc.html dialog "ล็อกหน้าจอ"
/// </summary>
public partial class LockScreenMessageWindow : Window
{
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
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
