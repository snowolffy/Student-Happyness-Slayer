using System.Windows;
using System.Windows.Media;
using OnionProcOparetor.AgentTray.Services;

namespace OnionProcOparetor.AgentTray;

public partial class StatusWindow : Window
{
    private readonly ClientApiClient _apiClient;

    public StatusWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await RefreshDataAsync();
    }

    private async Task RefreshDataAsync()
    {
        var status = await _apiClient.GetStatusAsync();

        if (status is null)
        {
            MachineNameText.Text = "ไม่สามารถเชื่อมต่อ Onion ProcOparetor Agent Service ได้";
            EnabledDot.Fill = (Brush)FindResource("AccentNeutralBrush");
            EnabledText.Text = "Unknown";
            return;
        }

        MachineNameText.Text = status.MachineName;
        RulesCountText.Text = status.RulesCount.ToString();
        LastPollText.Text = status.LastPollAt.HasValue
            ? status.LastPollAt.Value.ToLocalTime().ToString("HH:mm:ss")
            : "-";

        if (status.IsEnabled)
        {
            EnabledDot.Fill = (Brush)FindResource("AccentGreenBrush");
            EnabledText.Text = "Enabled";
        }
        else
        {
            EnabledDot.Fill = (Brush)FindResource("AccentRedBrush");
            EnabledText.Text = "Disabled";
        }

        var logs = await _apiClient.GetRecentLogsAsync();
        LogsListView.ItemsSource = logs;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDataAsync();
    }

    private void OpenTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        var terminalWindow = new LogTerminalWindow(_apiClient);
        terminalWindow.Show();
    }
    
}
