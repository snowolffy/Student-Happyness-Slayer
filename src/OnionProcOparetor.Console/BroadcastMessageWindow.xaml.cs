using System.Windows;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class BroadcastMessageWindow : Window
{
    public string MessageTitle { get; private set; } = string.Empty;
    public string MessageText { get; private set; } = string.Empty;
    public bool SendToAll { get; private set; }

    public BroadcastMessageWindow(int selectedCount)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);

        if (selectedCount > 0)
        {
            SendToSelectedRadio.Content = $"ส่งไปเครื่องที่เลือกไว้ ({selectedCount} เครื่อง)";
            SendToSelectedRadio.IsChecked = true;
        }
        else
        {
            SendToSelectedRadio.IsEnabled = false;
            SendToSelectedRadio.Content = "ส่งไปเครื่องที่เลือกไว้ (ยังไม่ได้เลือกเครื่องไหน)";
            SendToAllRadio.IsChecked = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var message = MessageInputBox.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            StatusText.Text = "กรุณากรอกข้อความ";
            // SetResourceReference ไม่ใช่ FindResource: FindResource คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ซึ่งจะค้างอยู่กับธีมเดิมตลอดไปเมื่อผู้ใช้สลับธีม - SetResourceReference ผูกเป็น DynamicResource จริงเหมือนที่เขียนใน XAML จึงตามธีมใหม่ให้เอง
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        MessageTitle = TitleInputBox.Text.Trim();
        MessageText = message;
        SendToAll = SendToAllRadio.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
