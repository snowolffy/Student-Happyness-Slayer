using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Options;
using OnionProcOparetor.Agent.Models;
using OnionProcOparetor.Agent.Services;

namespace OnionProcOparetor.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServerClient _serverClient;
    private readonly ServerSettings _settings;
    private readonly string _clientGuid;
    private readonly string _machineName;

    // ทุกกี่วินาทีที่จะ scan process ที่รันอยู่ทั้งหมด (แยกอิสระจาก PollIntervalSeconds)
    // เหตุผล: WMI __InstanceCreationEvent จับได้แค่ process ที่เพิ่ง "สร้างใหม่" เท่านั้น
    // ถ้า process เปิดค้างอยู่ก่อนแล้ว (ก่อน service เริ่ม หรือก่อน rule ถูกเพิ่ม/เปิดใช้งาน)
    // จะไม่โดนตรวจจับเลย ต้องมี periodic full-scan มาเสริมด้วย
    private const int ProcessScanIntervalSeconds = 10;

    // เก็บ (PID, StartTime) ที่เคย action (kill/log) ไปแล้วในรอบ scan ล่าสุด กัน action ซ้ำรัวๆ ทุก 10 วิ
    // ใช้คู่ (PID, StartTime) แทน PID เดี่ยวๆ เพราะ Windows recycle เลข PID ได้หลัง process เดิมตายไป
    private readonly HashSet<(int Pid, DateTime StartTime)> _alreadyActionedProcesses = new();

    // เก็บ rules + enabled state ล่าสุดที่ poll มาได้ ใช้เป็น cache
    // สำคัญมาก: ถ้า server ติดต่อไม่ได้ Worker จะใช้ค่านี้ต่อไป (fail-secure)
    private PollResponse _lastKnownState = new() { Enabled = true, Rules = new() };

    private ManagementEventWatcher? _processWatcher;

    private readonly ClientState _state;
    private readonly CommandProcessor _commandProcessor;
    private readonly LabHubConnection _labHubConnection;

    public Worker(
        ILogger<Worker> logger,
        ServerClient serverClient,
        IOptions<ServerSettings> settings,
        ClientState state,
        CommandProcessor commandProcessor,
        LabHubConnection labHubConnection)
    {
        _logger = logger;
        _serverClient = serverClient;
        _settings = settings.Value;
        _state = state;
        _commandProcessor = commandProcessor;
        _labHubConnection = labHubConnection;
        _clientGuid = ClientIdentity.GetOrCreateClientGuid();
        _machineName = ClientIdentity.GetMachineName();
        _state.ClientGuid = _clientGuid;

        _logger.LogInformation("Agent จะใช้ Server BaseUrl: {BaseUrl}", _settings.BaseUrl);

        if (Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var serverUri) &&
            (serverUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             serverUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("ServerSettings.BaseUrl ยังชี้ไปที่ localhost/127.0.0.1 ซึ่งจะทำให้ agent เรียกตัวเองแทน server จริง หากต้องการเชื่อมจากเครื่องอื่น ให้ตั้งค่า ServerSettings:BaseUrl เป็น IP/hostname ของ server");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OnionProcOparetor.Agent เริ่มทำงาน - ClientGuid: {Guid}, Machine: {Machine}",
            _clientGuid, _machineName);

        // เริ่ม WMI watcher แยก thread ต่างหาก (มันทำงานแบบ event-driven ของตัวเอง) - จับ process ใหม่
        StartProcessWatcher();

        // เริ่ม periodic full-scan แยก thread ต่างหากด้วย - จับ process ที่รันอยู่ก่อนแล้ว
        _ = RunPeriodicProcessScanAsync(stoppingToken);

        // เริ่มต่อ SignalR แบบ fire-and-forget - เป็นชั้นเสริมคู่ขนานกับ poll loop เดิม ไม่แทนที่
        // มี retry loop ของตัวเองอยู่แล้วข้างใน ไม่ throw ออกมาแม้ server จะยังไม่พร้อม/offline
        _ = _labHubConnection.StartAsync(_clientGuid, stoppingToken);

        // Loop หลัก: poll server เป็นระยะๆ เพื่ออัพเดต rules + enabled state
        while (!stoppingToken.IsCancellationRequested)
        {
            var polled = await _serverClient.PollAsync(_clientGuid, _machineName, stoppingToken);
            if (polled is not null)
            {
                _lastKnownState = polled;
                _state.UpdatePollResult(polled.Enabled, polled.Rules.Count, succeeded: true);
                _state.SetPollIntervalOverride(polled.PollIntervalOverrideSeconds);
                _logger.LogInformation("Poll สำเร็จ - Enabled: {Enabled}, Rules: {Count} ข้อ",
                    polled.Enabled, polled.Rules.Count);

                await ProcessPendingCommandsAsync(polled.PendingCommands, stoppingToken);
            }
            else
            {
                // poll ไม่สำเร็จ - รายงานสถานะไปที่ Tray ว่า poll ล้มเหลว
                // แต่ enabled/rulesCount ยังคงใช้ค่าล่าสุดที่มี (fail-secure)
                _state.UpdatePollResult(_lastKnownState.Enabled, _lastKnownState.Rules.Count, succeeded: false);
            }
            // ถ้า polled เป็น null (server ติดต่อไม่ได้) จะไม่แตะ _lastKnownState เลย
            // เท่ากับใช้ค่าล่าสุดที่เคยมีต่อไป = fail-secure

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
        }

        _processWatcher?.Stop();
        _processWatcher?.Dispose();
    }

    /// <summary>
    /// วน process command ที่ได้จาก pendingCommands ของ poll response (fallback path คู่ขนานกับ SignalR
    /// ReceiveCommand) - เรียก CommandProcessor เดียวกับที่ SignalR ใช้ (มี dedup + ack ในตัวอยู่แล้ว
    /// ไม่ต้อง ack ซ้ำที่นี่อีก)
    /// </summary>
    private async Task ProcessPendingCommandsAsync(List<PendingCommandDto> pendingCommands, CancellationToken ct)
    {
        foreach (var command in pendingCommands)
        {
            await _commandProcessor.ProcessPendingCommandAsync(command.Id, command.CommandType, command.PayloadJson, ct);
        }
    }

    /// <summary>
    /// เรียกตอน service กำลังจะหยุด (ก่อน ExecuteAsync ถูกยกเลิกเสร็จสมบูรณ์) - ปิด SignalR connection
    /// ให้เรียบร้อยคู่กับตอน start ใน ExecuteAsync (ไม่กระทบ poll loop ที่หยุดผ่าน stoppingToken ตามปกติอยู่แล้ว)
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _labHubConnection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Loop แยกต่างหาก สแกน process ที่รันอยู่ทั้งหมดทุก ProcessScanIntervalSeconds วินาที
    /// จับกรณีที่ WMI event พลาดไป (process เปิดค้างอยู่ก่อน service เริ่ม, หรือก่อน rule ถูกเพิ่ม/เปิดใช้งาน)
    /// </summary>
    private async Task RunPeriodicProcessScanAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var polled = await _serverClient.PollAsync(_clientGuid, _machineName, stoppingToken);
            if (polled is not null)
            {
                _lastKnownState = polled;
                _state.UpdatePollResult(polled.Enabled, polled.Rules.Count, succeeded: true);
                _state.SetPollIntervalOverride(polled.PollIntervalOverrideSeconds);
                _logger.LogInformation("Poll สำเร็จ - Enabled: {Enabled}, Rules: {Count} ข้อ",
                    polled.Enabled, polled.Rules.Count);

                await ProcessPendingCommandsAsync(polled.PendingCommands, stoppingToken);
            }
            else
            {
                _state.UpdatePollResult(_lastKnownState.Enabled, _lastKnownState.Rules.Count, succeeded: false);
            }

            // ใช้ override จาก ClientState (sync จาก DB ทุก poll หรือถูก command "UpdateSettings" เขียนทับ
            // ทันทีก็ได้ - ดู CommandProcessor.HandleUpdateSettingsAsync) ไม่งั้นใช้ค่า default จาก appsettings.json
            var effectiveIntervalSeconds = _state.PollIntervalOverrideSeconds ?? _settings.PollIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(effectiveIntervalSeconds), stoppingToken);
        }
    }

    private async Task ScanRunningProcessesAsync()
    {
        // ถ้า enforcement ปิดอยู่ (global หรือ per-machine) ไม่ต้อง scan เลย
        if (!_lastKnownState.Enabled)
        {
            return;
        }

        var currentProcesses = Process.GetProcesses();
        var currentProcessKeys = new HashSet<(int Pid, DateTime StartTime)>();

        try
        {
            foreach (var process in currentProcesses)
            {
                try
                {
                    DateTime startTime;
                    try
                    {
                        // StartTime อ่านไม่ได้เหมือนกันสำหรับบาง system process (สิทธิ์ไม่พอ)
                        startTime = process.StartTime;
                    }
                    catch
                    {
                        continue; // อ่านไม่ได้ ข้ามไปเลย
                    }

                    var processKey = (process.Id, startTime);
                    currentProcessKeys.Add(processKey);

                    // ข้าม (PID, StartTime) ที่เคย action ไปแล้วในรอบก่อนหน้า
                    if (_alreadyActionedProcesses.Contains(processKey))
                    {
                        continue;
                    }

                    string? executablePath;
                    try
                    {
                        // อ่าน MainModule.FileName อาจ throw Win32Exception ได้ถ้าเป็น system
                        // process ที่สิทธิ์ไม่พอ
                        executablePath = process.MainModule?.FileName;
                    }
                    catch
                    {
                        continue; // อ่านไม่ได้ ข้ามไปเลย ไม่ถือเป็น error ร้ายแรง
                    }

                    if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    {
                        continue;
                    }

                    var actioned = await CheckAndActOnProcess(executablePath, process.Id);
                    if (actioned)
                    {
                        _alreadyActionedProcesses.Add(processKey);
                    }
                }
                catch (Exception ex)
                {
                    // กัน exception จาก process ตัวใดตัวหนึ่งทำให้ scan รอบนี้หยุดกลางคัน
                    _logger.LogWarning(ex, "เกิดข้อผิดพลาดระหว่างตรวจสอบ process ระหว่าง periodic scan");
                }
            }
        }
        finally
        {
            foreach (var process in currentProcesses)
            {
                process.Dispose();
            }
        }

        // เคลียร์ (PID, StartTime) ที่ตายไปแล้วออกจาก cache (ไม่งั้น HashSet จะโตขึ้นเรื่อยๆ ไม่มีที่สิ้นสุด)
        _alreadyActionedProcesses.RemoveWhere(key => !currentProcessKeys.Contains(key));
    }

    private void StartProcessWatcher()
    {
        try
        {
            var query = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");

            _processWatcher = new ManagementEventWatcher(query);
            _processWatcher.EventArrived += OnProcessCreated;
            _processWatcher.Start();

            _logger.LogInformation("WMI process watcher เริ่มทำงานแล้ว");
        }
        catch (Exception ex)
        {
            // WMI ล้มเหลว (เช่น permission ไม่พอ) - log ไว้แต่ไม่ crash service ทั้งตัว
            _logger.LogError(ex, "เริ่ม WMI process watcher ไม่สำเร็จ");
        }
    }

    private async void OnProcessCreated(object sender, EventArrivedEventArgs e)
    {
        try
        {
            // ถ้า enforcement ปิดอยู่ (global หรือ per-machine) ไม่ต้องเช็คอะไรเลย
            if (!_lastKnownState.Enabled)
            {
                return;
            }

            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processId = (uint)targetInstance["ProcessId"];
            var executablePath = targetInstance["ExecutablePath"] as string;

            // บาง process ไม่มี ExecutablePath (เช่น system process บางตัว) ข้ามไปเลย
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            var actioned = await CheckAndActOnProcess(executablePath, (int)processId);
            if (actioned)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)processId);
                    _alreadyActionedProcesses.Add(((int)processId, proc.StartTime));
                }
                catch
                {
                    // process อาจตายไปแล้วก่อนที่จะอ่าน StartTime ทัน
                }
            }
        }
        catch (Exception ex)
        {
            // กัน exception จาก event handler ทำให้ watcher ตายทั้งตัว
            _logger.LogWarning(ex, "เกิดข้อผิดพลาดระหว่างประมวลผล process event");
        }
    }

    /// <summary>
    /// เช็ค process ตัวนี้กับทุก rule ที่เปิดใช้งานอยู่ คืน true ถ้ามี rule match และ action ไปแล้ว
    /// </summary>
    private async Task<bool> CheckAndActOnProcess(string executablePath, int processId)
    {
        var result = PublisherChecker.Check(executablePath);

        if (!result.HasPublisher)
        {
            return false; // หา publisher ไม่เจอเลย ไม่มีอะไรให้เทียบ
        }

        // เทียบกับทุก rule ที่เปิดใช้งานอยู่
        foreach (var rule in _lastKnownState.Rules.Where(r => r.IsEnabled))
        {
            var nameMatches = string.Equals(
                result.PublisherName?.Trim(),
                rule.PublisherName?.Trim(),
                StringComparison.OrdinalIgnoreCase);

            if (!nameMatches)
            {
                continue;
            }

            // [OPTIONAL NARROWING] ถ้ากฎกำหนด ProcessNameContains ไว้ ต้องเช็คว่าชื่อไฟล์มีคำนี้อยู่ด้วย
            if (!string.IsNullOrWhiteSpace(rule.ProcessNameContains))
            {
                var fileName = Path.GetFileName(executablePath);
                var containsMatch = fileName.Contains(rule.ProcessNameContains, StringComparison.OrdinalIgnoreCase);

                if (!containsMatch)
                {
                    _logger.LogInformation(
                        "พบ publisher '{Publisher}' ตรงกับกฎ '{Rule}' แต่ชื่อไฟล์ '{FileName}' ไม่มีคำว่า '{Filter}' - ข้ามเพราะกฎกำหนด ProcessNameContains",
                        result.PublisherName, rule.Name, fileName, rule.ProcessNameContains);
                    continue;
                }
            }

            // ถ้ากฎกำหนดว่าต้อง signed เท่านั้น แต่ match นี้มาจาก metadata อย่างเดียว ให้ข้าม
            if (rule.RequireSignedMatch && !result.IsSignedMatch)
            {
                _logger.LogInformation(
                    "พบ publisher '{Publisher}' ตรงกับกฎ '{Rule}' แต่มาจาก metadata เท่านั้น (ไม่ signed) - ข้ามเพราะกฎกำหนด RequireSignedMatch",
                    result.PublisherName, rule.Name);
                continue;
            }

            // match! รัน action ตามที่กำหนด
            await ExecuteRuleAction(executablePath, processId, result, rule);
            return true; // match กฎแรกที่เจอแล้วหยุด ไม่ต้องเช็คกฎถัดไป
        }

        return false;
    }

    private async Task ExecuteRuleAction(string executablePath, int processId, PublisherCheckResult result, RuleDto rule)
    {
        var actionParts = new List<string>();

        // ---- ส่วนที่ 1: Kill process (ถ้ากฎกำหนดไว้) ----
        if (rule.KillProcess)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                process.Kill();
                actionParts.Add("Killed process");
                _logger.LogInformation("Rule '{Rule}' matched - kill process สำเร็จ (PID: {Pid})", rule.Name, processId);
            }
            catch (ArgumentException)
            {
                // process อาจปิดตัวเองไปแล้วก่อนที่เราจะ kill ทัน - ไม่ถือเป็น error ร้ายแรง
                actionParts.Add("Process already exited");
                _logger.LogInformation("Rule '{Rule}' matched - process (PID: {Pid}) ปิดตัวเองไปแล้วก่อน kill", rule.Name, processId);
            }
            catch (Exception ex)
            {
                actionParts.Add($"Kill failed: {ex.Message}");
                _logger.LogError(ex, "Kill process ของกฎ '{Rule}' ไม่สำเร็จ (PID: {Pid})", rule.Name, processId);
            }
        }

        // ---- ส่วนที่ 2: รัน action command (ถ้ากำหนดไว้) ----
        if (!string.IsNullOrWhiteSpace(rule.ActionCommand))
        {
            try
            {
                var psi = BuildActionCommandProcessStartInfo(rule.ActionCommand, rule.ActionArguments);

                // log ก่อนรันเสมอเพื่อ audit trail (ActionCommand ตั้งได้จาก Console โดยครู - ต้องเห็นได้
                // ชัดเจนว่ากำลังจะรันอะไรจริงๆ ก่อนที่ Process.Start จะถูกเรียก ไม่ใช่แค่ log ผลหลังรันเสร็จ)
                _logger.LogInformation(
                    "Rule '{Rule}' matched - กำลังจะรัน action command: \"{FileName}\" {Arguments}",
                    rule.Name, psi.FileName, psi.Arguments);

                Process.Start(psi);
                actionParts.Add($"Ran command: {rule.ActionCommand}");
                _logger.LogInformation("Rule '{Rule}' matched - รัน action สำเร็จ: {Command} {Args}",
                    rule.Name, rule.ActionCommand, rule.ActionArguments);
            }
            catch (Exception ex)
            {
                actionParts.Add($"Command failed: {ex.Message}");
                _logger.LogError(ex, "รัน action command ของกฎ '{Rule}' ไม่สำเร็จ ({Command})", rule.Name, rule.ActionCommand);
            }
        }

        if (actionParts.Count == 0)
        {
            actionParts.Add("Logged only");
            _logger.LogInformation("Rule '{Rule}' matched - ไม่มี action กำหนดไว้ (log อย่างเดียว)", rule.Name);
        }

        var actionTaken = string.Join(" + ", actionParts);

        var logEntry = new LogEntryDto
        {
            ClientGuid = _clientGuid,
            MachineName = _machineName,
            ProcessPath = executablePath,
            DetectedPublisher = result.PublisherName,
            WasSignedMatch = result.IsSignedMatch,
            MatchedRuleId = rule.Id,
            MatchedRuleName = rule.Name,
            ActionTaken = actionTaken
        };

        _state.AddLog(logEntry);
        await _serverClient.SendLogAsync(logEntry, CancellationToken.None);
    }

    /// <summary>
    /// สร้าง ProcessStartInfo สำหรับรัน ActionCommand ของกฎ - แก้บั๊กที่ custom command เป็น script
    /// (.bat/.cmd/.ps1) ไม่เคยรันจริงเลย: ก่อนหน้านี้เรียก Process.Start ตรงด้วย UseShellExecute=false
    /// เสมอ ซึ่งเบื้องหลังเรียก CreateProcess ตรงๆ - รันได้แค่ native executable (.exe/.com) เท่านั้น
    /// ไฟล์ script ไม่มี PE header ให้ CreateProcess โหลด ต้องผ่าน interpreter ของมันเอง (cmd.exe /c
    /// หรือ powershell.exe -File) ไม่งั้นจะโดน Win32Exception "not a valid Win32 application" ทันที
    /// (ถูก catch ไว้เงียบๆ แค่ log เป็น "Command failed" ในไฟล์ log ของ Agent เอง - ไม่มีอะไร error
    /// กลับไปให้ครูเห็นที่ Console เลย เท่ากับ "ไม่ถูกรันจริง" จากมุมมองของคนตั้งกฎ)
    /// UI ฝั่ง Console (NewRuleActionBox) ระบุ tooltip ไว้ชัดว่ารับ "path คำสั่ง/สคริปต์" ทั้งคู่
    /// จึงต้อง dispatch ตาม extension แทนที่จะรันตรงเสมอ
    /// </summary>
    private static ProcessStartInfo BuildActionCommandProcessStartInfo(string actionCommand, string? actionArguments)
    {
        var userArguments = actionArguments ?? string.Empty;
        var extension = Path.GetExtension(actionCommand).ToLowerInvariant();

        switch (extension)
        {
            case ".bat":
            case ".cmd":
                return new ProcessStartInfo
                {
                    FileName = Environment.ExpandEnvironmentVariables("%ComSpec%"),
                    Arguments = $"/c \"{actionCommand}\" {userArguments}".TrimEnd(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            case ".ps1":
                return new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{actionCommand}\" {userArguments}".TrimEnd(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            default:
                // .exe/.com หรือ path อื่นที่ CreateProcess รันตรงได้อยู่แล้ว - พฤติกรรมเดิม ไม่เปลี่ยน
                return new ProcessStartInfo
                {
                    FileName = actionCommand,
                    Arguments = userArguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
        }
    }
}

/// <summary>ตั้งค่าที่ผูกกับ appsettings.json ส่วน "ServerSettings"</summary>
public class ServerSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8787";
    public int PollIntervalSeconds { get; set; } = 30;
}
