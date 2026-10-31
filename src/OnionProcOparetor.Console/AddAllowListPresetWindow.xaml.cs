using System.Windows;
using System.Windows.Controls;

namespace OnionProcOparetor.Console;

public partial class AddAllowListPresetWindow : Window
{
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
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
