using System.Windows;

namespace OnionProcOparetor.Console;

public partial class ConfirmWindow : Window
{
    private ConfirmWindow(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Drop-in replacement สำหรับ MessageBox.Show(message, title, YesNo, Warning) เดิม - themed ตาม
    /// design system ของแอพ (Card/ButtonDanger/ButtonSecondary) แทนที่จะเป็น native OS dialog ที่ดูเป็น
    /// คนละแอพ ตรงจุดเดียวที่ครูต้องหยุดคิดก่อนทำ action ที่ทำคืนไม่ได้
    /// </summary>
    public static bool Show(string message, string title, Window? owner = null)
    {
        var window = new ConfirmWindow(message, title) { Owner = owner };
        return window.ShowDialog() == true;
    }
}
