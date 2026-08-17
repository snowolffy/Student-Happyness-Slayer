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

        // กากบาท = ซ่อน ไม่ใช่ปิด + ไม่โผล่บน taskbar (ดู HideOnCloseWindowBehavior)
        this.EnableHideOnClose();

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

        // ผูกกับ "มองเห็นอยู่ไหม" แทน "ปิดไปแล้วหรือยัง" เพราะตอนนี้กากบาท = ซ่อน ไม่ใช่ปิด
        // (Closed จะไม่ถูกเรียกอีกต่อไปตอน user กดกากบาท) ถ้ายังผูกกับ Closed เหมือนเดิม
        // timer จะยิง API ทุก 3 วิต่อไปเรื่อยๆ ตลอดอายุของ Tray ทั้งที่ไม่มีใครดูอยู่แล้ว
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _autoRefreshTimer.Start();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        };

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