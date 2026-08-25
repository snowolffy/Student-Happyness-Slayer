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

    /// <summary>
    /// บั๊กจริงที่เจอ (2026-08-25, field test): กด "VIEW RUNNING PROCESSES..." แล้วทั้ง Console
    /// crash ทันที - สาเหตุคือ handler นี้ (และ Loaded ด้านบน) เป็น "async void" ทั้งคู่ ไม่มี try/catch
    /// เลยสักจุดตลอด chain (RefreshAsync -> RequestRunningProcessesAsync -> JsonSerializer.Deserialize)
    /// exception ที่โยนออกมาจาก async void event handler ใน WPF จะไม่มีใครจับได้เลย (ไม่ใช่ async Task
    /// ที่ caller await ได้) กลายเป็น unhandled exception ที่ terminate ทั้ง process ทันที - แอปนี้ไม่มี
    /// Application.DispatcherUnhandledException global handler ด้วย เลยไม่มีชั้นไหนกันไว้เลยจริงๆ
    ///
    /// แก้ด้วย try/catch คลุมทั้ง method (ไม่ใช่แค่จุดที่คิดว่าจะพังจุดเดียว) - ให้ exception อะไรก็ตาม
    /// (JSON malform, LabHubClient ยังไม่พร้อม, ฯลฯ) จบแค่ status message สีแดงในหน้าต่างนี้ ไม่ลาม
    /// ไปทำทั้ง Console ตาย - ตรงกับหลักการเดิมของโปรเจกต์นี้ (fail-secure/fail-visible ไม่ fail-crash)
    /// </summary>
    private async Task RefreshAsync()
    {
        try
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
        catch (Exception ex)
        {
            StatusText.Text = $"เกิดข้อผิดพลาดขณะดึงรายการโปรแกรม: {ex.Message}";
        }
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

            List<RunningProcessDto>? list;
            try
            {
                list = JsonSerializer.Deserialize<List<RunningProcessDto>>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                // แยก catch เฉพาะจุดนี้ (นอกเหนือจาก try/catch ครอบทั้ง RefreshAsync ด้านนอก) เพื่อให้
                // error message เจาะจงว่าเป็นปัญหา JSON shape ไม่ตรงกัน ไม่ใช่ network/timeout ทั่วไป
                return (false, new List<RunningProcessDto>(), $"รูปแบบข้อมูลที่ได้จาก Agent ไม่ถูกต้อง: {ex.Message}");
            }

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
        try
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
        catch (Exception ex)
        {
            // เหตุผลเดียวกับ try/catch ใน RefreshAsync ด้านบน - event handler ของ WPF ไม่มีใครจับ
            // exception ให้เลยถ้าไม่ทำเอง กัน Console ทั้งตัว crash จากปุ่มนี้ด้วยเหมือนกัน
            StatusText.Text = $"เปิดหน้าสร้างกฎไม่สำเร็จ: {ex.Message}";
        }
    }
}
