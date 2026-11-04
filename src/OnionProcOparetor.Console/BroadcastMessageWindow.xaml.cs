using System;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>UI-3b (2026-11-03): แปลงเป็น UserControl ฝังใน DialogHost ของ MainWindow (ดู
/// DashboardWindow.BroadcastMessageButton_Click) - logic เดิมเหมือนเก่าทุกอย่าง เปลี่ยนแค่วิธีรายงานผล
/// กลับจาก Window.DialogResult+Close() เป็น event ResultChosen</summary>
public partial class BroadcastMessageWindow : UserControl
{
    /// <summary>true = กดส่งข้อความสำเร็จ (อ่านผลจาก MessageTitle/MessageText/SendToAll ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    public string MessageTitle { get; private set; } = string.Empty;
    public string MessageText { get; private set; } = string.Empty;
    public bool SendToAll { get; private set; }

    public BroadcastMessageWindow(int selectedCount)
    {
        InitializeComponent();

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

        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
