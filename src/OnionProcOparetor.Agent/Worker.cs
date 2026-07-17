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

    public Worker(ILogger<Worker> logger, ServerClient serverClient, IOptions<ServerSettings> settings, ClientState state)
    {
        _logger = logger;
        _serverClient = serverClient;
        _settings = settings.Value;
        _state = state;
        _clientGuid = ClientIdentity.GetOrCreateClientGuid();
        _machineName = ClientIdentity.GetMachineName();
        _state.ClientGuid = _clientGuid;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OnionProcOparetor.Agent เริ่มทำงาน - ClientGuid: {Guid}, Machine: {Machine}",
            _clientGuid, _machineName);

        // เริ่ม WMI watcher แยก thread ต่างหาก (มันทำงานแบบ event-driven ของตัวเอง) - จับ process ใหม่
        StartProcessWatcher();

        // เริ่ม periodic full-scan แยก thread ต่างหากด้วย - จับ process ที่รันอยู่ก่อนแล้ว
        _ = RunPeriodicProcessScanAsync(stoppingToken);

        // Loop หลัก: poll server เป็นระยะๆ เพื่ออัพเดต rules + enabled state
        while (!stoppingToken.IsCancellationRequested)
        {
            var polled = await _serverClient.PollAsync(_clientGuid, _machineName, stoppingToken);
            if (polled is not null)
            {
                _lastKnownState = polled;
                _state.UpdatePollResult(polled.Enabled, polled.Rules.Count, succeeded: true);
                _logger.LogInformation("Poll สำเร็จ - Enabled: {Enabled}, Rules: {Count} ข้อ",
                    polled.Enabled, polled.Rules.Count);
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
    /// Loop แยกต่างหาก สแกน process ที่รันอยู่ทั้งหมดทุก ProcessScanIntervalSeconds วินาที
    /// จับกรณีที่ WMI event พลาดไป (process เปิดค้างอยู่ก่อน service เริ่ม, หรือก่อน rule ถูกเพิ่ม/เปิดใช้งาน)
    /// </summary>
    private async Task RunPeriodicProcessScanAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanRunningProcessesAsync();
            }
            catch (Exception ex)
            {
                // กัน exception จาก scan loop ทำให้ loop นี้ตายไปเฉยๆ (ไม่กระทบ poll loop หลัก)
                _logger.LogWarning(ex, "เกิดข้อผิดพลาดระหว่าง periodic process scan");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ProcessScanIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
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
                var psi = new ProcessStartInfo
                {
                    FileName = rule.ActionCommand,
                    Arguments = rule.ActionArguments ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                actionParts.Add($"Ran command: {rule.ActionCommand}");
                _logger.LogInformation("Rule '{Rule}' matched - รัน action: {Command} {Args}",
                    rule.Name, rule.ActionCommand, rule.ActionArguments);
            }
            catch (Exception ex)
            {
                actionParts.Add($"Command failed: {ex.Message}");
                _logger.LogError(ex, "รัน action command ของกฎ '{Rule}' ไม่สำเร็จ", rule.Name);
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
}

/// <summary>ตั้งค่าที่ผูกกับ appsettings.json ส่วน "ServerSettings"</summary>
public class ServerSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8787";
    public int PollIntervalSeconds { get; set; } = 30;
}
