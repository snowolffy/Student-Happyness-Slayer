using System.Windows;
using System.Windows.Media;
using MyLabGuard.ClientTray.Services;

namespace MyLabGuard.ClientTray;

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
            MachineNameText.Text = "ไม่สามารถเชื่อมต่อ MyLabGuard Service ได้";
            EnabledDot.Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0x55, 0x5A));
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
            EnabledDot.Fill = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
            EnabledText.Text = "Enabled";
        }
        else
        {
            EnabledDot.Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0x55, 0x5A));
            EnabledText.Text = "Disabled";
        }

        var logs = await _apiClient.GetRecentLogsAsync();
        LogsListView.ItemsSource = logs;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDataAsync();
    }
}