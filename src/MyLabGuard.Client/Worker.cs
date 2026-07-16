using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Options;
using MyLabGuard.Client.Models;
using MyLabGuard.Client.Services;

namespace MyLabGuard.Client;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServerClient _serverClient;
    private readonly ServerSettings _settings;
    private readonly string _clientGuid;
    private readonly string _machineName;

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
        _logger.LogInformation("MyLabGuard.Client เริ่มทำงาน - ClientGuid: {Guid}, Machine: {Machine}",
            _clientGuid, _machineName);

        // เริ่ม WMI watcher แยก thread ต่างหาก (มันทำงานแบบ event-driven ของตัวเอง)
        StartProcessWatcher();

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

            await CheckAndActOnProcess(executablePath);
        }
        catch (Exception ex)
        {
            // กัน exception จาก event handler ทำให้ watcher ตายทั้งตัว
            _logger.LogWarning(ex, "เกิดข้อผิดพลาดระหว่างประมวลผล process event");
        }
    }

    private async Task CheckAndActOnProcess(string executablePath)
    {
        var result = PublisherChecker.Check(executablePath);

        if (!result.HasPublisher)
        {
            return; // หา publisher ไม่เจอเลย ไม่มีอะไรให้เทียบ
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

            // ถ้ากฎกำหนดว่าต้อง signed เท่านั้น แต่ match นี้มาจาก metadata อย่างเดียว ให้ข้าม
            if (rule.RequireSignedMatch && !result.IsSignedMatch)
            {
                _logger.LogInformation(
                    "พบ publisher '{Publisher}' ตรงกับกฎ '{Rule}' แต่มาจาก metadata เท่านั้น (ไม่ signed) - ข้ามเพราะกฎกำหนด RequireSignedMatch",
                    result.PublisherName, rule.Name);
                continue;
            }

            // match! รัน action ตามที่กำหนด
            await ExecuteRuleAction(executablePath, result, rule);
            return; // match กฎแรกที่เจอแล้วหยุด ไม่ต้องเช็คกฎถัดไป
        }
    }

    private async Task ExecuteRuleAction(string executablePath, PublisherCheckResult result, RuleDto rule)
    {
        var actionTaken = "Logged only";

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
                actionTaken = $"Ran action: {rule.ActionCommand}";
                _logger.LogInformation("Rule '{Rule}' matched - รัน action: {Command} {Args}",
                    rule.Name, rule.ActionCommand, rule.ActionArguments);
            }
            catch (Exception ex)
            {
                actionTaken = $"Action failed: {ex.Message}";
                _logger.LogError(ex, "รัน action command ของกฎ '{Rule}' ไม่สำเร็จ", rule.Name);
            }
        }
        else
        {
            _logger.LogInformation("Rule '{Rule}' matched - ไม่มี action command กำหนดไว้ (log อย่างเดียว)", rule.Name);
        }

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