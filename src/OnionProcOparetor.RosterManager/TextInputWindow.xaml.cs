using System.Windows;

namespace OnionProcOparetor.RosterManager;

public partial class TextInputWindow : Window
{
    public TextInputWindow()
    {
        InitializeComponent();
    }

    public static string? Ask(Window? owner, string title, string message, string defaultValue = "")
    {
        var window = new TextInputWindow { Owner = owner, Title = title };
        window.MessageText.Text = message;
        window.InputBox.Text = defaultValue;
        window.InputBox.Focus();
        window.InputBox.SelectAll();

        return window.ShowDialog() == true ? window.InputBox.Text.Trim() : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
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
