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
        // WindowStartupLocation="Manual" + WindowCenteringHelper - กลไกเดียวกับหน้าต่างย่อยอื่น
        // ทั้งหมด (2026-09-10) ของเดิมใช้ CenterOwner ของ WPF เอง ซึ่งวางเยื้องจากกึ่งกลางจริง
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);
        _apiClient = apiClient;
        _labHubClient = labHubClient;
        _client = client;

        MachineNameText.Text = $"Running Processes - {client.MachineName}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    /// <summary>
    /// try/catch คลุมทั้ง method กัน exception จาก async void handler (Loaded/RefreshButton_Click)
    /// ที่ไม่มี caller คอย await เลย (JSON malform, LabHubClient ยังไม่พร้อม, ฯลฯ) ให้จบแค่ status
    /// message สีแดงในหน้าต่างนี้แทนที่จะหลุดไปเป็น unhandled exception - ตรงกับหลักการเดิมของ
    /// โปรเจกต์นี้ (fail-secure/fail-visible ไม่ fail-crash)
    ///
    /// หมายเหตุ: นี่ไม่ใช่สาเหตุของบั๊ก "กด VIEW RUNNING PROCESSES แล้ว Console หายทั้งตัว" ที่เจอจริง
    /// (2026-08-25 field test) - รอบแรกเข้าใจผิดว่าใช่ ตัวการจริงคือ XamlParseException ตอนเปิดหน้าต่าง
    /// จาก StaticResource ที่อ้างถึง style ซึ่งประกาศไว้ใน DashboardWindow.xaml เท่านั้น (ดู
    /// Theme/Controls.xaml ที่ย้าย 3 style นั้นมาไว้เป็น global แทน) try/catch นี้ยังมีประโยชน์อยู่
    /// (ป้องกันความเสี่ยงจริงของ async void ทั่วไป) แค่ไม่ใช่ตัวที่แก้บั๊กที่ report มา
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

    /// <summary>
    /// บล็อกไฟล์นี้ถาวรทุกเครื่องโดยผูกกับ SHA-256 (2026-08-31)
    ///
    /// ทางนี้มีอยู่เพราะ CREATE RULE ข้างล่างสร้างกฎที่ผูกกับชื่อ publisher ซึ่งใช้ไม่ได้เลยกับ
    /// โปรแกรมที่ไม่มี publisher - และนั่นคือกลุ่มที่ครูอยากบล็อกมากที่สุดพอดี (เกม portable/โหลด
    /// ที่ไม่ได้เซ็นและไม่มี metadata) ผูกกับ hash แทนจึงใช้ได้ทุกกรณี รวมถึงโปรแกรมที่
    /// Program Control เคยเห็นแล้วแต่ตัดสินไว้ไม่ตรงใจครู (endpoint เป็น upsert ทับให้เลย)
    ///
    /// ถามยืนยันก่อนเสมอ - ผลของปุ่มนี้กระทบทุกเครื่องในห้องถาวร ไม่ใช่แค่เครื่องที่กำลังดูอยู่
    /// </summary>
    private async void BlockByHashButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: RunningProcessDto process })
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(process.Sha256))
            {
                StatusText.Text = $"อ่านไฟล์ของ '{process.ProcessName}' ไม่ได้ จึงผูกคำตัดสินกับไฟล์นี้ไม่ได้";
                return;
            }

            var confirmed = ConfirmWindow.Show(
                $"'{process.ProcessName}' จะถูกปิดทันทีที่เปิดบนเครื่องใดก็ตามในห้อง\n\n" +
                "ผูกกับลายนิ้วมือของไฟล์ ไม่ใช่ชื่อ เปลี่ยนชื่อไฟล์หรือย้ายที่แล้วยังโดนอยู่\n" +
                "ยกเลิกได้ทีหลังที่หน้า PROGRAMS",
                "บล็อกโปรแกรมนี้ทุกเครื่อง",
                this);

            if (!confirmed)
            {
                return;
            }

            StatusText.Text = $"กำลังบล็อก '{process.ProcessName}' ...";

            // 2 = ProgramAction.Block (ตรงกับ enum ฝั่ง Server)
            var (success, message) = await _apiClient.SetManualProgramVerdictAsync(process, _client.MachineName, 2);

            StatusText.Text = success
                ? $"บล็อก '{process.ProcessName}' แล้ว - ทุกเครื่องจะเริ่มบล็อกภายในรอบ poll ถัดไป (ดู/ยกเลิกได้ที่หน้า PROGRAMS)"
                : $"บล็อกไม่สำเร็จ: {message}";
        }
        catch (Exception ex)
        {
            // เหตุผลเดียวกับ try/catch ตัวอื่นในไฟล์นี้ - async void handler ไม่มีใคร await
            StatusText.Text = $"บล็อกไม่สำเร็จ: {ex.Message}";
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
