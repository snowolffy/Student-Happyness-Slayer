using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// แสดงโปรแกรมที่กำลังรันอยู่และมีหน้าต่างบน desktop จริงของเครื่อง client ที่ระบุ (เปิดจาก
/// ActionPanelWindow → VIEW RUNNING PROCESSES...) ยิงคำสั่ง "GetRunningProcesses" ไปให้ Agent
/// (ส่งต่อไป AgentTray สแกนจริงเพราะ Agent เอง Session 0 มองไม่เห็นหน้าต่างของ user session เลย)
/// แล้วรอผลผ่าน LabHubClient event เดียวกับ pattern ที่ DashboardWindow.RequestVncSessionAsync ใช้
/// (TaskCompletionSource + timeout + subscribe/unsubscribe ชั่วคราว)
/// </summary>
public partial class RunningProcessesWindow : Window
{
    private static readonly TimeSpan ProcessListTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ClientMachineDto _client;

    public RunningProcessesWindow(ApiClient apiClient, LabHubClient labHubClient, ClientMachineDto client)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _client = client;

        MachineNameText.Text = $"Running Processes - {client.MachineName}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "กำลังขอรายการโปรแกรมที่รันอยู่จากเครื่องนี้...";
        ProcessesGrid.ItemsSource = null;
        EmptyText.Visibility = Visibility.Collapsed;

        var (success, processes, error) = await RequestRunningProcessesAsync();

        if (!success)
        {
            StatusText.Text = $"ดึงรายการไม่สำเร็จ: {error}";
            return;
        }

        ProcessesGrid.ItemsSource = processes;
        EmptyText.Visibility = processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"พบ {processes.Count} โปรแกรมที่มีหน้าต่างอยู่บน desktop";
    }

    /// <summary>
    /// ส่งคำสั่ง GetRunningProcesses แล้วรอผลผ่าน LabHubClient event - pattern เดียวกับ
    /// DashboardWindow.RequestVncSessionAsync (TCS + timeout + subscribe/unsubscribe ชั่วคราว)
    /// กรองด้วย ClientGuid เสมอเพราะ Console ตัวเดียวกันอาจมี request ค้างรออยู่หลายเครื่องพร้อมกัน
    /// </summary>
    private async Task<(bool Success, List<RunningProcessDto> Processes, string? Error)> RequestRunningProcessesAsync()
    {
        var tcs = new TaskCompletionSource<(bool, string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReady(string clientGuid, string json)
        {
            if (clientGuid == _client.ClientGuid)
            {
                tcs.TrySetResult((true, json, null));
            }
        }

        void OnFailed(string clientGuid, string reason)
        {
            if (clientGuid == _client.ClientGuid)
            {
                tcs.TrySetResult((false, null, reason));
            }
        }

        _labHubClient.ProcessListReady += OnReady;
        _labHubClient.ProcessListFailed += OnFailed;

        try
        {
            var sent = await _apiClient.SendCommandAsync(_client.ClientGuid, "GetRunningProcesses",
                new { requestingConsoleSessionId = _labHubClient.SessionId });

            if (!sent)
            {
                return (false, new List<RunningProcessDto>(), "ส่ง command GetRunningProcesses ไปยัง Server ไม่สำเร็จ (HTTP)");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(ProcessListTimeout));
            if (completed != tcs.Task)
            {
                return (false, new List<RunningProcessDto>(),
                    $"รอ Agent ตอบกลับเกิน {ProcessListTimeout.TotalSeconds} วิ (Agent อาจ offline หรือไม่มี user login อยู่หน้าเครื่อง)");
            }

            var (ok, json, reason) = await tcs.Task;
            if (!ok || json is null)
            {
                return (false, new List<RunningProcessDto>(), reason ?? "สแกน process บนเครื่องนี้ไม่สำเร็จ");
            }

            var list = JsonSerializer.Deserialize<List<RunningProcessDto>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (true, list ?? new List<RunningProcessDto>(), null);
        }
        finally
        {
            _labHubClient.ProcessListReady -= OnReady;
            _labHubClient.ProcessListFailed -= OnFailed;
        }
    }

    private void CreateRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RunningProcessDto process })
        {
            var dialog = new AddEditRuleWindow(_apiClient, process) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = $"สร้างกฎจาก '{process.ProcessName}' สำเร็จ - ดูได้ที่แท็บ RULES";
            }
        }
    }
}
