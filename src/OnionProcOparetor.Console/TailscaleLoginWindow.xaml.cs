using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>โมเดลตาม BroadcastMessageWindow.xaml.cs - เก็บ authkey ที่ครูกรอก แล้วให้ TailscaleWindow อ่านหลัง ShowDialog()==true</summary>
public partial class TailscaleLoginWindow : Window
{
    public string AuthKey { get; private set; } = string.Empty;

    public TailscaleLoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var key = AuthKeyInputBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            StatusText.Text = "กรุณากรอก auth key";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger.Default");
            return;
        }

        AuthKey = key;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
