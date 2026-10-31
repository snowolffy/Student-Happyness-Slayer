using System.Windows;

namespace OnionProcOparetor.RosterManager;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>เปิดเป็น modal แล้วคืน true ถ้ากด "ยืนยัน" - owner ควรส่งมาเสมอให้จอกึ่งกลางหน้าต่างหลัก</summary>
    public static bool Ask(Window? owner, string title, string message, string confirmText = "ยืนยัน", bool danger = false)
    {
        var window = new ConfirmWindow { Owner = owner };
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.ConfirmButton.Content = confirmText;
        if (danger)
        {
            window.ConfirmButton.SetResourceReference(StyleProperty, "ButtonDanger");
        }

        return window.ShowDialog() == true;
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
}
