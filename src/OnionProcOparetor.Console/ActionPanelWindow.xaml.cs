using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

/// <summary>
/// Panel รวม action ทั้งหมดที่ทำกับเครื่อง client ได้ - ใช้ทั้ง 2 โหมด:
/// - Single: เปิดจากปุ่ม ACTIONS ต่อแถวใน ClientsGrid (ทำกับเครื่องเดียว มี Settings/Delete ให้ด้วย)
/// - Bulk: เปิดจากปุ่ม ACTIONS บน header (multi-select) - ไม่มี Settings/Delete (เสี่ยงเกินไปถ้าทำ
///   เป็น bulk) แต่เพิ่มตัวเลือกว่าจะ apply กับ "เครื่องที่เลือกไว้" หรือ "ทุกเครื่อง" แทน BroadcastMessageWindow
///   ที่มี pattern เดียวกันอยู่แล้ว
/// แทนที่ ContextMenu เดิม (ClientActionsButton_Click) และปุ่มแยกบน header (Toggle/Shutdown/Restart Selected)
/// </summary>
public partial class ActionPanelWindow : Window
{
    /// <summary>ต้องตรงกับฝั่ง Agent (PowerActionPayload.MinDelaySeconds) - validate ทั้ง 2 ฝั่ง</summary>
    public const int MinDelaySeconds = 1;

    /// <summary>ต้องตรงกับฝั่ง Agent (PowerActionPayload.MaxDelaySeconds) - validate ทั้ง 2 ฝั่ง</summary>
    public const int MaxDelaySeconds = 120;

    /// <summary>ค่าเดิมที่ Agent เคย hardcode ไว้ - คงเป็น default เพื่อไม่เปลี่ยนพฤติกรรมที่ครูคุ้นเคย</summary>
    public const int DefaultDelaySeconds = 10;

    private readonly ApiClient _apiClient;
    private readonly bool _isBulk;
    private readonly ClientMachineDto? _singleClient;
    private readonly List<ClientMachineDto> _selectedClients;
    private readonly List<ClientMachineDto> _allClients;

    /// <summary>true ถ้ามี action ใดๆ ที่แก้ข้อมูลไปแล้ว - DashboardWindow ใช้ตัดสินใจว่าต้อง reload หรือไม่</summary>
    public bool DataChanged { get; private set; }

    /// <summary>
    /// ไม่ null ถ้าผู้ใช้กด VIEW SCREEN - DashboardWindow (เจ้าของ panel นี้) เป็นคนต่อ RfbClient จริง
    /// เองทั้งหมด (ดู DashboardWindow.ShowVncViewAsync) panel นี้แค่ "ส่งสัญญาณ" กลับไปแล้วปิดตัวเองเท่านั้น
    /// - single-client mode เท่านั้น
    /// </summary>
    public ClientMachineDto? ViewScreenRequested { get; private set; }

    /// <summary>
    /// ไม่ null ถ้าผู้ใช้กด VIEW RUNNING PROCESSES... - DashboardWindow เป็นคนเปิด RunningProcessesWindow
    /// จริงทั้งหมด (ดู DashboardWindow.RunActionPanelAsync) panel นี้แค่ "ส่งสัญญาณ" กลับไปแล้วปิดตัวเองเท่านั้น
    /// - single-client mode เท่านั้น (เหตุผลเดียวกับ ViewScreenRequested)
    /// </summary>
    public ClientMachineDto? ViewProcessesRequested { get; private set; }

    /// <summary>
    /// ไม่ null = ครูกด action ที่ต้องมีหน้ารายงานผล DashboardWindow จะเปิด ActionReportWindow
    /// ต่อทันทีหลัง panel นี้ปิดลง (ดู DashboardWindow.OpenActionPanelAsync)
    ///
    /// ทำไมไม่เปิดหน้า report เองตรงนี้: panel นี้ถูกเปิดด้วย ShowDialog() (modal) ถ้าเปิด
    /// หน้าต่างใหม่ก่อนปิดตัวเองจะกลายเป็นซ้อนกัน 2 ชั้น ซึ่งโจทย์ห้ามไว้ ("ปิด ActionPanelWindow
    /// แล้วเปิดหน้านี้ต่อ ไม่ซ้อนกัน") - ฝากไว้ให้ Dashboard เปิดแทนจึงไม่มีการซ้อนกันเลย
    /// </summary>
    public ActionReportRequest? PendingReport { get; private set; }

    /// <summary>Single-client mode - เปิดจากปุ่ม ACTIONS ต่อแถว</summary>
    public ActionPanelWindow(ApiClient apiClient, ClientMachineDto client)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);
        _apiClient = apiClient;
        _isBulk = false;
        _singleClient = client;
        _selectedClients = new List<ClientMachineDto> { client };
        _allClients = _selectedClients;

        DelaySecondsBox.Text = DefaultDelaySeconds.ToString();
        HeaderText.Text = $"Actions - {client.MachineName}";
        TargetPanel.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Visible;
        ConfigRuleButton.Visibility = Visibility.Visible;
        DeleteButton.Visibility = Visibility.Visible;
        ViewScreenButton.Visibility = Visibility.Visible;
        ViewProcessesButton.Visibility = Visibility.Visible;
        ToggleButton.Content = client.IsEnabled ? "DISABLE ENFORCEMENT" : "ENABLE ENFORCEMENT";
    }

    /// <summary>
    /// Bulk mode - เปิดจากปุ่ม ACTIONS บน header (multi-select) selectedClients ต้องมีอย่างน้อย 1 เครื่อง
    /// (caller เช็คมาก่อนแล้ว) allClients ใช้ตอนเลือก "ทุกเครื่อง" แทน "เครื่องที่เลือกไว้"
    /// </summary>
    public ActionPanelWindow(ApiClient apiClient, List<ClientMachineDto> selectedClients, List<ClientMachineDto> allClients)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowCenteringHelper.CenterOverOwnerOrScreen(this);
        _apiClient = apiClient;
        _isBulk = true;
        _selectedClients = selectedClients;
        _allClients = allClients;

        DelaySecondsBox.Text = DefaultDelaySeconds.ToString();
        TargetPanel.Visibility = Visibility.Visible;
        ApplyToSelectedRadio.Content = $"เครื่องที่เลือกไว้ ({selectedClients.Count} เครื่อง)";
        ApplyToAllRadio.Content = $"ทุกเครื่อง ({allClients.Count} เครื่อง)";
        ApplyToSelectedRadio.IsChecked = true;

        ApplyToSelectedRadio.Checked += (_, _) => HeaderText.Text = $"Applying to {_selectedClients.Count} selected machines";
        ApplyToAllRadio.Checked += (_, _) => HeaderText.Text = $"Applying to all {_allClients.Count} machines";
        HeaderText.Text = $"Applying to {selectedClients.Count} selected machines";

        SettingsButton.Visibility = Visibility.Collapsed;
        ConfigRuleButton.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = Visibility.Collapsed;
        ViewScreenButton.Visibility = Visibility.Collapsed;
        ViewProcessesButton.Visibility = Visibility.Collapsed;
        ToggleButton.Content = "TOGGLE ENFORCEMENT";
    }

    private List<ClientMachineDto> CurrentTargets =>
        _isBulk && ApplyToAllRadio.IsChecked == true ? _allClients : _selectedClients;

    private bool Confirm(string message, string title) => ConfirmWindow.Show(message, title, this);

    /// <summary>
    /// Toggle enforcement - เป็น action เดียวที่ใช้หน้า report แบบ "immediate" (BatchId = null)
    /// เพราะไม่ได้ผ่านระบบ command/ack เลย: มันคือ POST /api/clients/{id}/toggle ที่ Server
    /// แก้ DB ให้เสร็จในคำขอเดียว เครื่อง client จะรู้ตัวตอน poll รอบถัดไปเอง ไม่มี ack ราย
    /// เครื่องให้รอ ผลจึงเป็นที่สิ้นสุดทันทีที่ HTTP response กลับมา (ดู ActionReportRequest)
    /// </summary>
    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = CurrentTargets;

        if (_isBulk && !Confirm($"ยืนยัน toggle สถานะของ {targets.Count} เครื่องที่เลือกไว้?", "ยืนยัน Toggle"))
        {
            return;
        }

        DataChanged = true;

        // จับคู่ ClientGuid → Id ไว้ก่อน เพราะ ToggleClientAsync ใช้ Id (int) ไม่ใช่ ClientGuid
        var idByGuid = targets.ToDictionary(c => c.ClientGuid, c => c.Id);

        OpenReportFor("Toggle Enforcement", targets, batchId: null, async row =>
        {
            var success = await _apiClient.ToggleClientAsync(idByGuid[row.ClientGuid]);

            return success
                ? (true, "เปลี่ยนสถานะ enforcement แล้ว (เครื่องจะรับค่าใหม่ตอน poll รอบถัดไป)")
                : (false, "Server ปฏิเสธคำขอ toggle");
        });
    }

    /// <summary>
    /// สร้าง ActionReportRequest แล้วปิด panel นี้ทันที - DashboardWindow จะเปิดหน้า report ต่อ
    /// ใช้ร่วมกันทุก action ที่มีหน้ารายงานผล (Toggle/Lock/Unlock/Shutdown/Restart) เป็น pattern กลาง
    /// </summary>
    private void OpenReportFor(
        string actionLabel,
        List<ClientMachineDto> targets,
        string? batchId,
        Func<ActionReportRow, Task<(bool Ok, string? Message)>> sendAsync)
    {
        PendingReport = new ActionReportRequest
        {
            ActionLabel = actionLabel,
            BatchId = batchId,
            SendAsync = sendAsync,
            Rows = targets
                .Select(c => new ActionReportRow { ClientGuid = c.ClientGuid, MachineName = c.MachineName })
                .ToList()
        };

        DialogResult = DataChanged;
        Close();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        var settingsWindow = new ClientSettingsWindow(_apiClient, _singleClient) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            DataChanged = true;
            StatusText.Text = "บันทึก Settings สำเร็จ";
        }
    }

    private void ConfigRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        var ruleOverridesWindow = new RuleOverridesWindow(_apiClient, _singleClient) { Owner = this };
        if (ruleOverridesWindow.ShowDialog() == true)
        {
            DataChanged = true;
            StatusText.Text = "อัปเดต Rule override สำเร็จ";
        }
    }

    /// <summary>ปิด panel แล้วส่งสัญญาณกลับ DashboardWindow ให้เปิดแท็บ SCREEN VIEW สำหรับเครื่องนี้ (ดู ViewScreenRequested)</summary>
    private void ViewScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        ViewScreenRequested = _singleClient;
        DialogResult = DataChanged;
        Close();
    }

    /// <summary>ปิด panel แล้วส่งสัญญาณกลับ DashboardWindow ให้เปิดหน้าต่าง running processes สำหรับเครื่องนี้ (ดู ViewProcessesRequested)</summary>
    private void ViewProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        ViewProcessesRequested = _singleClient;
        DialogResult = DataChanged;
        Close();
    }

    // Lock ไม่ต้อง confirm (ทำคืนง่าย - ครูกด Unlock จาก panel นี้ได้ทันที)
    private void LockButton_Click(object sender, RoutedEventArgs e) =>
        SendToTargets("LockWorkstation", null, "Lock Workstation");

    private void UnlockButton_Click(object sender, RoutedEventArgs e) =>
        SendToTargets("UnlockWorkstation", null, "Unlock Workstation");

    private void RestartButton_Click(object sender, RoutedEventArgs e) => SendPowerAction("Restart");
    private void ShutdownButton_Click(object sender, RoutedEventArgs e) => SendPowerAction("Shutdown");

    // Tailscale lock/unlock - เหมือน Lock/Unlock Workstation ด้านบนทุกประการ (ไม่ต้อง confirm, ทำคืนง่าย)
    // แค่คนละ commandType/payload - ดู CommandProcessor.HandleSetTailscaleLockdownAsync
    private void TailscaleLockButton_Click(object sender, RoutedEventArgs e) =>
        SendToTargets("SetTailscaleLockdown", new { locked = true }, "Tailscale Lock");

    private void TailscaleUnlockButton_Click(object sender, RoutedEventArgs e) =>
        SendToTargets("SetTailscaleLockdown", new { locked = false }, "Tailscale Unlock");

    /// <summary>
    /// on-demand เท่านั้น (ไม่มี badge ถาวรบน dashboard - ตัดสินใจร่วมกับ user 2026-08-25) เปิด
    /// ActionReportWindow แสดงผล 1 บรรทัดต่อเครื่องเหมือน action อื่นๆ ทุกประการ - message ที่ Agent
    /// ส่งกลับ (ดู CommandProcessor.HandleGetTailscaleStatusAsync) มีทั้งล็อกอยู่ไหม/ติดตั้งหรือยัง/tailnet IP
    /// </summary>
    private void TailscaleStatusButton_Click(object sender, RoutedEventArgs e) =>
        SendToTargets("GetTailscaleStatus", null, "Tailscale Status");

    /// <summary>
    /// เก็บ authkey จาก TailscaleLoginWindow ครั้งเดียว (โมเดลตาม BroadcastMessageWindow) แล้วส่งไปยัง
    /// ทุกเครื่องใน target ปัจจุบัน - รองรับกรณี authkey เดียวกันใช้ enroll ได้หลายเครื่องพร้อมกัน (เช่น
    /// reusable/tagged key จาก Tailscale admin console สำหรับทั้ง lab)
    /// </summary>
    private void TailscaleLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var loginWindow = new TailscaleLoginWindow { Owner = this };
        if (loginWindow.ShowDialog() == true)
        {
            SendToTargets("RunTailscaleLogin", new { authKey = loginWindow.AuthKey }, "Tailscale Login");
        }
    }

    /// <summary>
    /// กันพิมพ์อย่างอื่นนอกจากตัวเลขลงช่องหน่วงเวลาตั้งแต่แรก (ชั้นที่ 1 ของ 3 ชั้น:
    /// input filter → TryResolveDelaySeconds ตอนกดปุ่ม → clamp ซ้ำฝั่ง Agent)
    /// ยังต้องมีชั้นที่ 2 อยู่ดีเพราะ paste (Ctrl+V) ไม่ผ่าน PreviewTextInput
    /// </summary>
    private void DelaySecondsBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// อ่านค่าหน่วงเวลาจากช่องกรอก - คืน null ถ้าค่าใช้ไม่ได้จริงๆ (caller ต้องยกเลิก action ทันที
    /// ห้ามส่ง command ต่อ) ถ้าอยู่นอกช่วง 1-120 จะ "บีบ" ให้อยู่ในช่วงแล้วเขียนค่าที่บีบแล้ว
    /// กลับลงช่องให้ user เห็นด้วย (ไม่ใช่บีบเงียบๆ แล้วส่งค่าที่ user ไม่ได้ตั้งใจ)
    ///
    /// ตัดสินใจแยก 2 กรณีต่างกันโดยตั้งใจ:
    /// - กรอกไม่ใช่ตัวเลข/ว่าง = "ไม่รู้ว่าครูตั้งใจอะไร" → ยกเลิก ให้แก้ก่อน (fail-safe)
    /// - กรอกตัวเลขแต่นอกช่วง = "รู้ว่าครูตั้งใจอะไร แค่เกินขอบ" → บีบเข้าขอบ + แจ้งให้เห็น
    /// </summary>
    private int? TryResolveDelaySeconds()
    {
        var raw = DelaySecondsBox.Text.Trim();

        if (!int.TryParse(raw, out var seconds))
        {
            System.Windows.MessageBox.Show(
                $"กรุณากรอกเวลาหน่วงเป็นตัวเลขระหว่าง {MinDelaySeconds} ถึง {MaxDelaySeconds} วินาที\n\n" +
                $"ค่าที่กรอกไว้ตอนนี้: '{raw}'",
                "เวลาหน่วงไม่ถูกต้อง", MessageBoxButton.OK, MessageBoxImage.Warning);

            DelaySecondsBox.Focus();
            DelaySecondsBox.SelectAll();
            return null;
        }

        var clamped = Math.Clamp(seconds, MinDelaySeconds, MaxDelaySeconds);

        if (clamped != seconds)
        {
            DelaySecondsBox.Text = clamped.ToString();

            System.Windows.MessageBox.Show(
                $"เวลาหน่วงต้องอยู่ระหว่าง {MinDelaySeconds} ถึง {MaxDelaySeconds} วินาที\n\n" +
                $"ค่าที่กรอก ({seconds}) ถูกปรับเป็น {clamped} วินาทีให้แล้ว กรุณากดปุ่มอีกครั้งเพื่อยืนยัน",
                "ปรับเวลาหน่วงให้อยู่ในช่วงที่รองรับ", MessageBoxButton.OK, MessageBoxImage.Warning);

            // คืน null โดยตั้งใจ - บังคับให้ครูกดยืนยันอีกครั้งหลังเห็นค่าที่ถูกปรับแล้ว
            // (ไม่ส่งค่าที่ครูไม่ได้พิมพ์เองออกไปดับเครื่องนักเรียนทันทีในคลิกเดียว)
            return null;
        }

        return clamped;
    }

    /// <summary>Shutdown/Restart - ต้อง confirm เสมอ (destructive, ทำคืนไม่ได้ทันที ต่างจาก Lock/Toggle)</summary>
    private void SendPowerAction(string commandType)
    {
        var targets = CurrentTargets;

        var delaySeconds = TryResolveDelaySeconds();
        if (delaySeconds is null)
        {
            return; // ค่าใช้ไม่ได้ - ไม่ปล่อยให้หลุดไปถึง command เด็ดขาด
        }

        if (!Confirm(
            $"ยืนยัน {commandType} เครื่อง {targets.Count} เครื่อง?\n\n" +
            $"แต่ละเครื่องจะเตือน user ล่วงหน้า {delaySeconds} วินาทีก่อนดำเนินการจริง การกระทำนี้ย้อนกลับไม่ได้ทันที",
            $"ยืนยัน {commandType} {targets.Count} เครื่อง"))
        {
            return;
        }

        SendToTargets(commandType, new { delaySeconds = delaySeconds.Value }, commandType);
    }

    /// <summary>
    /// action ที่ผ่านระบบ command/ack จริง (Lock/Unlock/Shutdown/Restart) - สร้าง BatchId ใหม่
    /// ต่อการกด 1 ครั้ง ผูกทุกเครื่องในชุดนั้นไว้ด้วยกัน แล้วเปิดหน้า report ให้ครูดูผลราย
    /// เครื่องแบบ real-time (ตัวหน้า report เป็นคนยิงคำสั่งจริง ไม่ใช่ที่นี่ - ดู ActionReportRequest)
    /// </summary>
    private void SendToTargets(string commandType, object? payload, string actionLabel)
    {
        var targets = CurrentTargets;
        var batchId = Guid.NewGuid().ToString("N");

        OpenReportFor(actionLabel, targets, batchId, async row =>
        {
            var (commandId, error) = await _apiClient.SendCommandTrackedAsync(
                row.ClientGuid, commandType, payload, batchId);

            if (commandId is null)
            {
                return (false, error);
            }

            // ต้องเซ็ตก่อน return - หน้า report ใช้ CommandId จับคู่ผลที่เข้ามาทาง SignalR
            row.CommandId = commandId;
            return (true, null);
        });
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singleClient is null)
        {
            return;
        }

        if (!Confirm($"ยืนยันการลบเครื่อง '{_singleClient.MachineName}'? การกระทำนี้ย้อนกลับไม่ได้", "ยืนยันการลบ"))
        {
            return;
        }

        var success = await _apiClient.DeleteClientAsync(_singleClient.Id);
        StatusText.Text = success ? "ลบเครื่องสำเร็จ" : "ลบเครื่องไม่สำเร็จ";

        if (success)
        {
            DataChanged = true;
            DialogResult = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = DataChanged;
        Close();
    }
}
