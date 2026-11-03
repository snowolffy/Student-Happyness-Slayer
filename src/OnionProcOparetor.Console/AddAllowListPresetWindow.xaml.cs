using System;
using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

public partial class AddAllowListPresetWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): true = สร้างชุดแล้ว (อ่าน PresetName ต่อ) false = ยกเลิก</summary>
    public event Action<bool>? ResultChosen;

    public string PresetName { get; private set; } = string.Empty;

    public AddAllowListPresetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameInputBox.Focus();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInputBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusText.Text = "กรุณากรอกชื่อชุด";
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger.Default");
            return;
        }

        PresetName = name;
        ResultChosen?.Invoke(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ResultChosen?.Invoke(false);
    }
}
