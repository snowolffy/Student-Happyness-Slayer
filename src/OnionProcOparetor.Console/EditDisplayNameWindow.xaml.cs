using System;
using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

/// <summary>แก้ชื่อที่แสดงของบัญชีหนึ่ง (WP4b, 2026-10-30) - เรียกจากแท็บ "ผู้ใช้" ของ DashboardWindow
/// เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้ (Server เป็นคนตัดสิน ที่นี่แค่เก็บชื่อใหม่มาส่ง)</summary>
public partial class EditDisplayNameWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = บันทึกแล้ว (อ่าน NewDisplayName ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    public string? NewDisplayName { get; private set; }

    public EditDisplayNameWindow(string username, string currentDisplayName)
    {
        InitializeComponent();
        TitleText.Text = $"แก้ชื่อที่แสดง - {username}";
        DisplayNameBox.Text = currentDisplayName;
        DisplayNameBox.SelectAll();
        Loaded += (_, _) => DisplayNameBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = DisplayNameBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            StatusText.Text = "ต้องกรอกชื่อที่แสดง";
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        NewDisplayName = name;
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
