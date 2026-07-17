using System.Text;
using System.Windows;
using System.Windows.Threading;
using OnionProcOparetor.AgentTray.Services;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray;

public partial class LogTerminalWindow : Window
{
    private readonly ClientApiClient _apiClient;
    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isPaused;

    public LogTerminalWindow(ClientApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshLogsAsync();

        Loaded += async (_, _) =>
        {
            await RefreshLogsAsync();
            _autoRefreshTimer.Start();
        };

        // หยุด timer ตอนปิดหน้าต่าง กันเรียก API ต่อทั้งที่ไม่มีใครดูอยู่แล้ว
        Closed += (_, _) => _autoRefreshTimer.Stop();
    }

    private async Task RefreshLogsAsync()
    {
        if (_isPaused) return;

        List<LogEntryDto> logs = await _apiClient.GetRecentLogsAsync();

        var sb = new StringBuilder();
        foreach (LogEntryDto log in logs)
        {
            sb.AppendLine($"[{log.Timestamp}] {log.ProcessPath} -> {log.MatchedRuleName} -> {log.ActionTaken}");
        }

        LogTerminalText.Text = sb.ToString();
        LogScrollViewer.ScrollToBottom();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        PauseButton.Content = _isPaused ? "RESUME" : "PAUSE";
        AutoRefreshStatusText.Text = _isPaused ? "auto-refresh: paused" : "auto-refresh: on";
    }
}