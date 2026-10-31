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

    // Threshold สำหรับสลับไป/กลับจาก FallbackBaseUrl (Tailscale) - ตัวเลขคงที่ตัดสินใจร่วมกับผู้ใช้แล้ว
    // ไม่เปิดให้ตั้งค่าผ่าน installer/config เพราะเป็นค่าที่ผ่านการคุยเรื่อง flapping/false-positive มาแล้ว
    // (แนวทางเดียวกับ ExponentialBackoffRetryPolicy.Delays ใน LabHubConnection.cs ที่ก็ hardcode เช่นกัน)
    private const int FailuresBeforeFallback = 5;
    private const int SuccessesBeforeRecovery = 7;

    // ตัดสินใจร่วมกับ user 2026-08-25: N=5/M=7 เท่าเดิม (กัน false-positive จากเน็ตสะดุดแป๊บเดียว) แต่
    // นับให้ครบเร็วขึ้นมาก - แยก "ช่วงปกติ" (poll ทุก PollIntervalSeconds ตามปกติ ไม่เพิ่มโหลด) ออกจาก
    // "ช่วงสงสัยว่ามีปัญหา" (พอเจอ fail/probe-สำเร็จครั้งแรกที่ยังไม่ครบ N/M ให้ retry ถี่ๆ ทันที) - ใช้
    // ค่าเดียวกันทั้ง 2 ทิศทาง (สลับไป fallback และสลับกลับ primary) ตามที่ user ยืนยัน
    private const int FastRetryIntervalSeconds = 5;

    // เก็บ (PID, StartTime) ที่เคย action (kill/log) ไปแล้วในรอบ scan ล่าสุด กัน action ซ้ำรัวๆ ทุก 10 วิ
    // ใช้คู่ (PID, StartTime) แทน PID เดี่ยวๆ เพราะ Windows recycle เลข PID ได้หลัง process เดิมตายไป
    private readonly HashSet<(int Pid, DateTime StartTime)> _alreadyActionedProcesses = new();

    // เก็บ rules + enabled state ล่าสุดที่ poll มาได้ ใช้เป็น cache
    // สำคัญมาก: ถ้า server ติดต่อไม่ได้ Worker จะใช้ค่านี้ต่อไป (fail-secure)
    private PollResponse _lastKnownState = new() { Enabled = true, Rules = new() };

    private ManagementEventWatcher? _processWatcher;

    // นับ poll fail ติดกันบน primary (สำหรับตัดสินใจสลับไป fallback) และ probe สำเร็จติดกันบน primary
    // ระหว่างที่กำลังใช้ fallback อยู่ (สำหรับตัดสินใจสลับกลับ) - reset เป็น 0 ทุกครั้งที่มีการสลับ
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private bool _loggedFallbackNotConfigured;

    // ---- ช่วงเริ่มทำงาน: แข่ง LAN กับ Tailscale (WP5, ตกลงกับผู้ใช้แล้ว ดู docs/roadmap.md) ----
    // ปัญหาเดิม: ตอนบูต LAN มักยังไม่พร้อม Agent ต้อง poll LAN พลาดครบ FailuresBeforeFallback ครั้งก่อนจะ
    // ได้ลอง Tailscale เครื่องจึงขาดการติดต่อช่วงหนึ่งทุกครั้งที่เปิด ตอนนี้: จนกว่าจะติดต่อ Server ได้ครั้งแรก
    // ให้ใช้ StartupRouteRace (LAN ได้เปรียบ 2 วิ แล้วทางไหนตอบก่อนใช้ทางนั้น) แทน PollAsync ปกติ ถ้าได้
    // Tailscale ให้ probe LAN เบื้องหลัง พอ LAN ตอบครั้งแรกสลับกลับเลย (LAN แค่ยังไม่พร้อมตอนบูต ไม่ได้เสีย
    // จริง จึงไม่ใช้ M=7) หลังจากนั้นทุกอย่างใช้กติกา 5/7 เดิมไม่เปลี่ยน ถ้าไม่ได้ตั้ง FallbackBaseUrl ส่วนนี้
    // ไม่ทำงานเลย (_startupRacePending = false ตั้งแต่ต้น)
    private bool _startupRacePending;
    private int _startupRaceFailures;
    private bool _pollSoonToCorrectFallbackFlag;
    private volatile bool _bootPrimaryProbeActive;
    private volatile bool _bootPrimaryReachable;

    // ปลุก loop หลักให้ตื่นก่อนครบ delay (ใช้ตอน probe LAN เบื้องหลังเจอว่า LAN ตอบแล้ว) - ถ้าไม่มีใครปลุก
    // WaitAsync(timeout, ct) ทำงานเหมือน Task.Delay(timeout, ct) เดิมทุกอย่าง
    private readonly SemaphoreSlim _wakeMainLoop = new(0, 1);

    private readonly ClientState _state;
    private readonly CommandProcessor _commandProcessor;
    private readonly LabHubConnection _labHubConnection;
    private readonly AgentBypassState _bypassState;
    private readonly ProgramControlService _programControl;
    private readonly WebsiteControlService _websiteControl;
    private readonly BrowserHardeningService _browserHardening;
    private readonly ExamModeState _examMode;
    private readonly AgentTrayNotifier _agentTrayNotifier;

    // แจ้ง AgentTray เฉพาะตอน "สถานะเปลี่ยนจริง" เท่านั้น (ดู ScanRunningProcessesAsync) - ไม่ใช่ทุก
    // enforcement tick (10 วิ) เพราะป้ายฝั่ง AgentTray นับถอยหลังเองได้จากค่า ExpiresAtUtc ที่ได้รับ
    // ครั้งเดียว (ดู ExamModeBadgeWindow) ส่งซ้ำทุก tick จะยิง pipe + อาจ auto-recover สตาร์ท AgentTray
    // ขึ้นมาใหม่โดยไม่จำเป็นทุก 10 วิ ตลอดช่วงที่โหมดสอบทำงานอยู่ (ดู AgentTrayNotifier.SendAsync)
    private bool _lastNotifiedExamActive;
    private DateTime? _lastNotifiedExamExpiresAtUtc;

    // Sign-in (WP3-3, 2026-10-31) - เหตุผลเดียวกับ _lastNotifiedExamActive ข้างบน (ส่งให้ AgentTray
    // เฉพาะตอนสถานะเปลี่ยนจริง) ผสม bypass/enabled เข้ากับ ClientState.RequireLogin ดิบก่อนเทียบเสมอ
    private bool _lastNotifiedRequireLogin;
    private string? _lastNotifiedLoginScreenMessage;

    public Worker(
        ILogger<Worker> logger,
        ServerClient serverClient,
        IOptions<ServerSettings> settings,
        ClientState state,
        CommandProcessor commandProcessor,
        LabHubConnection labHubConnection,
        AgentBypassState bypassState,
        ProgramControlService programControl,
        WebsiteControlService websiteControl,
        BrowserHardeningService browserHardening,
        ExamModeState examMode,
        AgentTrayNotifier agentTrayNotifier)
    {
        _logger = logger;
        _serverClient = serverClient;
        _settings = settings.Value;
        _state = state;
        _commandProcessor = commandProcessor;
        _labHubConnection = labHubConnection;
        _bypassState = bypassState;
        _programControl = programControl;
        _websiteControl = websiteControl;
        _browserHardening = browserHardening;
        _examMode = examMode;
        _agentTrayNotifier = agentTrayNotifier;
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
        //
        // ถ้าตั้ง FallbackBaseUrl ไว้ (WP5) ยังไม่ต่อตอนนี้ - รอให้การแข่งตอนเริ่มทำงานเลือกเส้นทางได้ก่อน
        // แล้วค่อยต่อไปที่เดียวกับ HTTP poll (OnStartupRouteChosen) ไม่งั้น SignalR จะไปค้าง retry ที่ LAN
        // ทั้งที่ poll ได้ Tailscale ไปแล้ว
        if (string.IsNullOrWhiteSpace(_settings.FallbackBaseUrl))
        {
            _ = _labHubConnection.StartAsync(_clientGuid, stoppingToken);
        }
        else
        {
            _startupRacePending = true;
        }

        // Loop หลัก: poll server เป็นระยะๆ เพื่ออัพเดต rules + enabled state
        while (!stoppingToken.IsCancellationRequested)
        {
            // Bypass เปิดอยู่ - ห้ามยิง request ออกไปหา Server เลยแม้แต่ครั้งเดียว (ตัดขาดเงียบๆ ไม่ใช่
            // แค่ไม่ประมวลผลผลลัพธ์) วนแค่ delay เฉยๆ จนกว่าจะถูก toggle กลับ ดู BypassCoordinator
            if (_bypassState.IsBypassed)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                continue;
            }

            // probe LAN เบื้องหลัง (หลังบูตได้ Tailscale) เจอว่า LAN ตอบแล้ว - สลับกลับก่อน poll รอบนี้
            if (_bootPrimaryReachable)
            {
                _bootPrimaryReachable = false;
                if (_serverClient.IsUsingFallback)
                {
                    SwitchToPrimary(stoppingToken, "LAN เพิ่งพร้อมหลังบูต (probe เบื้องหลังตอบแล้ว)");
                }
            }

            PollResponse? polled;
            if (_startupRacePending)
            {
                var race = await _serverClient.RaceStartupPollAsync(
                    _settings.BaseUrl, _settings.FallbackBaseUrl!, _clientGuid, _machineName,
                    stoppingToken, _programControl.BlocklistVersion);
                polled = race.Response;
                if (polled is not null)
                {
                    OnStartupRouteChosen(race, stoppingToken);
                }
            }
            else
            {
                polled = await _serverClient.PollAsync(_clientGuid, _machineName, stoppingToken, _programControl.BlocklistVersion, _state.CurrentStudentCode);
            }

            if (polled is not null)
            {
                _lastKnownState = polled;
                _programControl.ApplyBlocklist(polled.BlocklistVersion, polled.BlockedHashes);

                // โหมดสอบ (WP2-2, 2026-10-31) - sync เฉพาะตอน poll สำเร็จเท่านั้น (Server ชนะเสมอตอน
                // ติดต่อได้ ครอบคลุมทั้งเริ่มใหม่/ยกเลิกก่อนเวลา/ไม่มีอะไรเปลี่ยน) ตอน poll ไม่สำเร็จ
                // (else branch ข้างล่าง) ตั้งใจไม่แตะ _examMode เลย - ให้มันหมดเวลาด้วยนาฬิกาตัวเองต่อไป
                // (ดู ExamModeState.IsActive) แม้ Server จะล่มระหว่างนั้นก็ตาม
                _examMode.UpdateFromServer(polled.ExamMode);

                // Sign-in (WP3-3, 2026-10-31) - cache ดิบจาก poll เหมือน ExamMode ด้านบน ตัดสินใจ "ต้อง
                // แจ้ง AgentTray ไหม" จริงอยู่ที่ ScanRunningProcessesAsync (ผสมกับ bypass/enabled อีกชั้น)
                _state.UpdateSignInSettings(polled.RequireLogin, polled.LoginScreenMessage, polled.LockScreenDefaultMessage, polled.AutoLogoutIdleMinutes);

                _state.UpdatePollResult(polled.Enabled, polled.Rules.Count, succeeded: true);
                _state.SetPollIntervalOverride(polled.PollIntervalOverrideSeconds);
                _logger.LogInformation("Poll สำเร็จ - Enabled: {Enabled}, Rules: {Count} ข้อ",
                    polled.Enabled, polled.Rules.Count);

                await ProcessPendingCommandsAsync(polled.PendingCommands, stoppingToken);

                // ส่งโปรแกรมที่ยังไม่รู้จักที่สะสมไว้ไปให้ Server จำแนกเป็นชุด - ทำตรงนี้ (หลัง poll
                // สำเร็จ) ไม่ใช่ตอนที่เจอ process โดยตั้งใจ ดู ProgramControlService.FlushPendingAsync
                await _programControl.FlushPendingAsync(stoppingToken);

                if (!_serverClient.IsUsingFallback)
                {
                    _consecutiveFailures = 0;
                }
            }
            else
            {
                // poll ไม่สำเร็จ - รายงานสถานะไปที่ Tray ว่า poll ล้มเหลว
                // แต่ enabled/rulesCount ยังคงใช้ค่าล่าสุดที่มี (fail-secure)
                _state.UpdatePollResult(_lastKnownState.Enabled, _lastKnownState.Rules.Count, succeeded: false);

                if (_startupRacePending)
                {
                    // ช่วงเริ่มทำงาน: ทั้ง LAN และ Tailscale ยังไม่ตอบ - ไม่นับ 5/7 (การแข่งลองทั้งสองทางอยู่แล้ว)
                    // แค่นับไว้เลือกจังหวะ retry ข้างล่าง
                    _startupRaceFailures++;
                }
                else if (_serverClient.IsUsingFallback)
                {
                    // fallback เองก็ติดต่อไม่ได้ตอนนี้ - ไม่ควรให้ recovery counter รอดจากตรงนี้
                    _consecutiveSuccesses = 0;
                }
                else
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= FailuresBeforeFallback)
                    {
                        if (!string.IsNullOrWhiteSpace(_settings.FallbackBaseUrl))
                        {
                            SwitchToFallback(stoppingToken);
                        }
                        else if (!_loggedFallbackNotConfigured)
                        {
                            _logger.LogWarning(
                                "Poll ล้มเหลวติดกัน {Failures} ครั้ง แต่ไม่ได้ตั้งค่า FallbackBaseUrl ไว้ - จะไม่สลับไปที่ไหน (fail-secure ด้วย _lastKnownState ต่อไป)",
                                _consecutiveFailures);
                            _loggedFallbackNotConfigured = true;
                        }
                    }
                }
            }
            // ถ้า polled เป็น null (server ติดต่อไม่ได้) จะไม่แตะ _lastKnownState เลย
            // เท่ากับใช้ค่าล่าสุดที่เคยมีต่อไป = fail-secure

            // กำลังใช้ fallback อยู่ - probe primary คู่ขนานไปด้วยทุกรอบ (ไม่กระทบ _lastKnownState/enforcement
            // เลย ใช้แค่นับ M ครั้งสำเร็จติดกันเพื่อตัดสินใจสลับกลับ primary เท่านั้น)
            //
            // ข้ามระหว่างที่ probe LAN เบื้องหลังของช่วงบูตยังทำงานอยู่ (WP5) - ตัวนั้นรับหน้าที่สลับกลับแทน
            if (_serverClient.IsUsingFallback && !_bootPrimaryProbeActive)
            {
                var primaryOk = await _serverClient.ProbePrimaryAsync(_settings.BaseUrl, _clientGuid, _machineName, stoppingToken);
                if (primaryOk)
                {
                    _consecutiveSuccesses++;
                    if (_consecutiveSuccesses >= SuccessesBeforeRecovery)
                    {
                        SwitchToPrimary(stoppingToken);
                    }
                }
                else
                {
                    _consecutiveSuccesses = 0;
                }
            }

            // เลือก interval ของรอบถัดไป: ถ้ากำลัง "สงสัยว่ามีปัญหา" อยู่ (นับ failure streak บน primary
            // หรือนับ recovery-probe streak บน fallback ค้างอยู่ ยังไม่ครบ N/M) ใช้ FastRetryIntervalSeconds
            // แทน PollIntervalSeconds ปกติ - เร่งนับให้ครบเร็วขึ้นโดยไม่ต้องลด N/M เอง (ลด false-positive
            // risk เท่าเดิม) พอ reset กลับ 0 (poll สำเร็จตอนอยู่ primary, หรือ probe primary fail ตอนอยู่
            // fallback) ก็กลับไปใช้ interval ปกติทันที ไม่เพิ่มโหลดตอนทุกอย่างปกติเลย
            var suspectingTrouble = (!_serverClient.IsUsingFallback && _consecutiveFailures > 0)
                || (_serverClient.IsUsingFallback && _consecutiveSuccesses > 0);
            var delaySeconds = suspectingTrouble ? FastRetryIntervalSeconds : ResolvePollIntervalSeconds();

            if (_startupRacePending)
            {
                // ช่วงเริ่มทำงานที่ทั้งสองทางยังไม่ตอบ: จังหวะเดียวกับที่ loop เดิมใช้ตอน Server ติดต่อไม่ได้ -
                // retry ถี่ (FastRetryIntervalSeconds) FailuresBeforeFallback รอบแรก แล้วกลับไปใช้ interval ปกติ
                // (เดิมเมื่อสลับไป fallback แล้ว fallback ก็ไม่ตอบ ก็ถอยไปใช้ interval ปกติเหมือนกัน)
                delaySeconds = _startupRaceFailures < FailuresBeforeFallback
                    ? FastRetryIntervalSeconds
                    : ResolvePollIntervalSeconds();
            }
            else if (_pollSoonToCorrectFallbackFlag)
            {
                // การแข่งตอนเริ่มยิงไปทั้งสองทาง: request ของทางที่แพ้อาจไปถึง Server ทีหลังแล้วเขียน
                // UsingFallback ผิด - poll อีกรอบเร็วๆ ให้ค่าบน Server ตรงกับทางที่ใช้อยู่จริง
                _pollSoonToCorrectFallbackFlag = false;
                delaySeconds = FastRetryIntervalSeconds;
            }

            // เหมือน Task.Delay(delay, stoppingToken) เดิม แต่ probe LAN เบื้องหลังปลุกให้ตื่นก่อนได้
            await _wakeMainLoop.WaitAsync(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _processWatcher?.Stop();
        _processWatcher?.Dispose();
    }

    /// <summary>
    /// interval จริงของ poll รอบถัดไป: ใช้ override จาก Server ถ้ามี ไม่งั้นใช้ค่าใน appsettings.json
    ///
    /// บั๊กจริงที่เจอ (2026-08-31, ตอนอ่านโค้ดรอบ hardening): ค่า override ถูกเก็บลง ClientState ทั้งจาก
    /// poll response (ExecuteAsync) และจาก UpdateSettings command (CommandProcessor) มาตลอด แต่ไม่มีโค้ด
    /// ตรงไหนอ่านมันกลับมาใช้คำนวณ delay เลยแม้แต่ที่เดียว - loop หลักใช้ _settings.PollIntervalSeconds
    /// จาก appsettings.json ตายตัวเสมอ เท่ากับฟีเจอร์ "Remote client setting" ทั้งอัน (ทั้งหน้า UI ฝั่ง
    /// Console, column ใน DB, command type, และ comment ที่ CommandProcessor.cs:315 ที่เขียนว่า
    /// "มีผลทันที ไม่ต้องรอ poll รอบถัดไป") ไม่เคยมีผลต่อพฤติกรรมจริงของ Agent เลยสักครั้ง
    ///
    /// clamp ไว้ด้วยเพราะค่านี้มาจาก Server: ต่ำเกินไปจะกลายเป็น hot loop ยิง request รัวใส่ Server จาก
    /// 40 เครื่องพร้อมกัน สูงเกินไปจะทำให้เครื่องดูเหมือน offline ตลอดเวลา (OnlineThresholdSeconds = 90)
    /// </summary>
    private int ResolvePollIntervalSeconds()
    {
        const int MinIntervalSeconds = 5;
        const int MaxIntervalSeconds = 3600;

        var interval = _state.PollIntervalOverrideSeconds ?? _settings.PollIntervalSeconds;
        if (interval <= 0)
        {
            interval = _settings.PollIntervalSeconds > 0 ? _settings.PollIntervalSeconds : 30;
        }

        return Math.Clamp(interval, MinIntervalSeconds, MaxIntervalSeconds);
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
    /// สลับ ServerClient (HTTP poll) + LabHubConnection (SignalR) ไปที่ FallbackBaseUrl พร้อมกัน หลังจาก
    /// primary ล้มเหลวติดกันครบ FailuresBeforeFallback ครั้ง - LabHubConnection ใช้ fire-and-forget
    /// (เหมือนตอน StartAsync เดิม) กัน reconnect ที่อาจช้าไปบล็อค poll loop หลัก
    /// </summary>
    private void SwitchToFallback(CancellationToken ct)
    {
        var fallbackUrl = _settings.FallbackBaseUrl!;
        _serverClient.SwitchTo(fallbackUrl, isFallback: true);
        _ = _labHubConnection.SwitchServerAsync(fallbackUrl, ct);
        _logger.LogWarning(
            "Primary server ({PrimaryUrl}) ติดต่อไม่ได้ {Failures} ครั้งติดกัน - สลับไปใช้ FallbackBaseUrl: {FallbackUrl}",
            _settings.BaseUrl, _consecutiveFailures, fallbackUrl);
        _consecutiveFailures = 0;
        _consecutiveSuccesses = 0;
        _loggedFallbackNotConfigured = false;
    }

    /// <summary>
    /// สลับกลับไปที่ primary หลังจาก probe สำเร็จติดกันครบ SuccessesBeforeRecovery ครั้งระหว่างที่ใช้
    /// fallback อยู่ (hysteresis กัน flapping) - เหมือน SwitchToFallback ในทางกลับกัน
    /// </summary>
    private void SwitchToPrimary(CancellationToken ct, string? reason = null)
    {
        _serverClient.SwitchTo(_settings.BaseUrl, isFallback: false);
        _ = _labHubConnection.SwitchServerAsync(_settings.BaseUrl, ct);
        if (reason is null)
        {
            _logger.LogInformation(
                "Primary server กลับมาใช้งานได้แล้ว (probe สำเร็จติดกัน {Successes} ครั้ง) - สลับกลับจาก fallback ไปใช้ primary: {PrimaryUrl}",
                _consecutiveSuccesses, _settings.BaseUrl);
        }
        else
        {
            _logger.LogInformation("{Reason} - สลับกลับจาก fallback ไปใช้ primary: {PrimaryUrl}", reason, _settings.BaseUrl);
        }
        _consecutiveFailures = 0;
        _consecutiveSuccesses = 0;
    }

    /// <summary>
    /// การแข่งตอนเริ่มทำงานได้ผลแล้ว (WP5) - ServerClient สลับ HTTP poll ไปทางที่ชนะเรียบร้อยใน
    /// RaceStartupPollAsync ตรงนี้ทำส่วนที่เหลือ: ต่อ SignalR ไปที่เดียวกัน ปิดช่วงเริ่มทำงาน (ต่อจากนี้ใช้
    /// กติกา 5/7 เดิม) และถ้าได้ Tailscale ให้เริ่ม probe LAN เบื้องหลังเพื่อสลับกลับทันทีที่ LAN พร้อม
    /// </summary>
    private void OnStartupRouteChosen(StartupRaceResult<PollResponse> race, CancellationToken ct)
    {
        _startupRacePending = false;
        _pollSoonToCorrectFallbackFlag = race.FallbackFired;

        var chosenUrl = _serverClient.ActiveBaseUrl;

        // ปกติ SignalR ยังไม่เคยต่อ (เลื่อนไว้รอผลแข่ง) แต่ถ้ามีคนต่อไปก่อนแล้วระหว่างรอ (BypassCoordinator
        // ต่อกลับตอนปิด bypass) ให้สลับแทน - ทั้งสองแบบ fire-and-forget เหมือนเดิม
        if (_labHubConnection.IsStarted)
        {
            _ = _labHubConnection.SwitchServerAsync(chosenUrl, ct);
        }
        else
        {
            _ = _labHubConnection.StartAsync(_clientGuid, ct, chosenUrl);
        }

        if (race.ViaFallback)
        {
            _logger.LogWarning(
                "ตอนเริ่มทำงาน LAN ({PrimaryUrl}) ไม่ตอบภายใน {HeadStart} วิ แต่ Tailscale ตอบก่อน - ใช้ FallbackBaseUrl: {FallbackUrl} ไปก่อน แล้วจะ probe LAN เบื้องหลังเพื่อสลับกลับ",
                _settings.BaseUrl, StartupRouteRace.PrimaryHeadStart.TotalSeconds, chosenUrl);
            _bootPrimaryProbeActive = true;
            _ = RunBootPrimaryProbeAsync(ct);
        }
        else
        {
            _logger.LogInformation("ตอนเริ่มทำงานติดต่อ Server ผ่าน LAN ได้: {PrimaryUrl}", chosenUrl);
        }
    }

    /// <summary>
    /// probe LAN เบื้องหลังหลังบูตได้ Tailscale - ไม่สลับเองจากตรงนี้ (กันแข่งกับ loop หลักที่กำลัง poll
    /// อยู่) แค่ตั้งธงแล้วปลุก loop หลักให้เป็นคนสลับ ไม่ probe ระหว่าง bypass (ห้ามยิงออกไปหา Server)
    /// </summary>
    private async Task RunBootPrimaryProbeAsync(CancellationToken ct)
    {
        try
        {
            var reachable = await _serverClient.WaitForPrimaryAsync(
                _settings.BaseUrl, _clientGuid, _machineName, () => _bypassState.IsBypassed, ct);
            if (reachable)
            {
                _bootPrimaryReachable = true;
                try
                {
                    _wakeMainLoop.Release();
                }
                catch (SemaphoreFullException)
                {
                    // ถูกปลุกค้างไว้อยู่แล้ว
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "probe LAN เบื้องหลังหลังบูตล้มเหลว - จะใช้กติกาสลับกลับปกติ (7 ครั้ง) แทน");
        }
        finally
        {
            _bootPrimaryProbeActive = false;
        }
    }

    /// <summary>
    /// เรียกตอน service กำลังจะหยุด (ก่อน ExecuteAsync ถูกยกเลิกเสร็จสมบูรณ์) - ปิด SignalR connection
    /// ให้เรียบร้อยคู่กับตอน start ใน ExecuteAsync (ไม่กระทบ poll loop ที่หยุดผ่าน stoppingToken ตามปกติอยู่แล้ว)
    ///
    /// บั๊กจริงที่เจอ (2026-08-11, live-tested): ถ้า LabHubConnection กำลัง mid-connect อยู่พอดีตอนนั้น
    /// (เช่น server เข้าไม่ถึง/เครือข่ายมีปัญหา - ConnectWithRetryAsync ยัง await _connection.StartAsync
    /// ค้างอยู่) DisposeAsync() อาจใช้เวลานานผิดปกติกว่าจะ unwind - ทำให้ทั้ง service หยุดช้า/ไม่ตอบสนอง
    /// ต่อ SCM's stop request ภายในเวลาที่ควร ยืนยันจาก uninstall จริงที่ค้างอยู่ที่ StopServices เกิน
    /// นาทีนึงโดยไม่ขยับ (Windows Installer log ยืนยัน service ยังอยู่ใน STATE=RUNNING ไม่ใช่แม้แต่
    /// STOP_PENDING) - ผลกระทบร้ายแรงกว่าแค่ "ช้า": ถ้า SCM/msiexec ตัดสินใจ DeleteService() ไปก่อนที่
    /// service จะหยุดจริงๆ (เพราะรอนานเกินไป) จะเกิด "phantom service" ค้างอยู่ใน Service Control
    /// Manager (registry key ถูกลบไปแล้วแต่ SCM ยังจำ entry เดิมไว้จนกว่าจะ reboot) ทำให้ install ครั้งถัดไป
    /// สร้าง service ใหม่ไม่ได้/start ไม่ได้เลย (WIN32_EXIT_CODE 1067) - ตรงกับบั๊กที่เจอจริงพอดี
    ///
    /// แก้ด้วยการจำกัดเวลารอ DisposeAsync() ไว้ไม่เกิน 5 วิ - ถ้าเกิน ปล่อยผ่านไปเลย (ไม่ throw, ไม่ block
    /// service shutdown ต่อไป) service กำลังจะปิดตัวอยู่แล้ว connection ที่ยังค้างจะถูกทิ้งไปพร้อม process
    /// เองตอน exit จริง ไม่มีผลเสียอะไรเพิ่มจากการไม่รอให้ dispose "สวยงาม" ครบ
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var disposeTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _labHubConnection.DisposeAsync().AsTask().WaitAsync(disposeTimeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("LabHubConnection.DisposeAsync ไม่เสร็จภายใน 5 วิ (อาจ mid-connect อยู่) - ข้ามไปเลย กัน service shutdown ค้าง");
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Loop แยกต่างหาก สแกน process ที่รันอยู่ทั้งหมดทุก ProcessScanIntervalSeconds วินาที
    /// จับกรณีที่ WMI event พลาดไป (process เปิดค้างอยู่ก่อน service เริ่ม, หรือก่อน rule ถูกเพิ่ม/เปิดใช้งาน)
    ///
    /// บั๊กที่เจอ (2026-08-18): เดิม loop นี้ก็อป logic ของ main poll loop ใน ExecuteAsync มาทั้งดุ้น
    /// (เรียก _serverClient.PollAsync ซ้ำ) แทนที่จะเรียก ScanRunningProcessesAsync() ตามชื่อ method -
    /// ผลคือ process ที่เปิดค้างอยู่ก่อน service เริ่ม/ก่อน rule เปิดใช้งาน ไม่เคยถูกสแกนเลยจริงๆ จะโดน
    /// action ก็ต่อเมื่อถูกเปิดใหม่ผ่าน WMI watcher เท่านั้น ไม่ต้องเช็ค bypass/poll ที่นี่อีก เพราะ
    /// ExecuteAsync's main loop ทำหน้าที่ poll+update _lastKnownState อยู่แล้ว ที่นี่แค่อ่านค่าล่าสุดมาสแกน
    /// (เหมือน OnProcessCreated ที่ก็ไม่เช็ค bypass เพราะสแกน local ล้วนๆ ไม่ยิง request ออกไปหา Server)
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
                _logger.LogWarning(ex, "เกิดข้อผิดพลาดระหว่าง periodic full-scan ของ process ที่รันอยู่");
            }

            await Task.Delay(TimeSpan.FromSeconds(ProcessScanIntervalSeconds), stoppingToken);
        }
    }

    private async Task ScanRunningProcessesAsync()
    {
        // โหมดสอบ (WP2-2, 2026-10-31) - ต้องผ่าน bypass/enforcement gate เดียวกันกับ website rules
        // ปกติ (ตั้งใจ: ครูเปิด bypass เพื่อ "แก้ไขเครื่องชั่วคราว" ต้องปลดทุกอย่างจริง ไม่เหลือ
        // allow-list ค้างอยู่ที่ใครงงได้) _examMode.IsActive คำนวณจากนาฬิกาเครื่องนี้เองทุกครั้งที่อ่าน
        // (ดู ExamModeState) จึงหมดเวลาเองได้แม้ poll รอบต่อๆ มาจะล้มเหลวเพราะ Server ล่ม
        var examActive = !_bypassState.IsBypassed && _lastKnownState.Enabled && _examMode.IsActive;

        // Website Control (2026-09-11): apply ทุกรอบ scan โดยไม่มี early-return แบบ process rules
        // ข้างล่าง เพราะ registry key ที่เคยเขียนไว้จะ "อยู่ต่อ" ในเซสชันเดียวกันจนกว่าจะมีคนลบจริง
        // (ต่างจาก process kill ที่แค่ไม่ทำอะไรต่อก็พอ) ต้องส่ง empty list เข้าไปเองเมื่อ bypass/ปิด
        // enforcement เพื่อให้ WebsiteControlService เคลียร์ URLBlocklist ออกจริง ไม่ใช่แค่หยุดยุ่งเฉยๆ
        //
        // โหมดสอบ: บล็อกทุกเว็บ ("*") ยกเว้นโดเมนที่อนุญาต - แทนที่กฎ WebsiteRule ปกติไปเลยทั้งชุด
        // ตามที่ wireframe nf-exam ระบุ ("ทุกเว็บถูกบล็อก ยกเว้นที่เลือกไว้ข้างล่าง")
        string[] websitePatterns;
        string[]? allowPatterns;
        if (examActive)
        {
            websitePatterns = new[] { "*" };
            allowPatterns = _examMode.AllowedDomains.ToArray();
        }
        else
        {
            websitePatterns = (_bypassState.IsBypassed || !_lastKnownState.Enabled)
                ? Array.Empty<string>()
                : _lastKnownState.WebsiteRules.Select(r => r.UrlPattern).ToArray();
            allowPatterns = null;
        }
        _websiteControl.Apply(websitePatterns, allowPatterns);

        // แจ้ง AgentTray เฉพาะตอน "สถานะเปลี่ยนจริง" (เริ่ม/จบ/ExpiresAt เปลี่ยนเพราะยกเลิกก่อนเวลา) -
        // ดู comment ที่ field _lastNotifiedExamActive ว่าทำไมไม่ส่งทุก tick
        var currentExpiresAtUtc = examActive ? _examMode.ExpiresAtUtc : null;
        if (examActive != _lastNotifiedExamActive || currentExpiresAtUtc != _lastNotifiedExamExpiresAtUtc)
        {
            _lastNotifiedExamActive = examActive;
            _lastNotifiedExamExpiresAtUtc = currentExpiresAtUtc;
            await _agentTrayNotifier.SendExamModeStatusAsync(currentExpiresAtUtc, CancellationToken.None);
        }

        // "ต้องล็อกอินไหม" (WP3-3, 2026-10-31) - เหตุผลเดียวกับ examActive ข้างบน (ครูเปิด bypass เพื่อ
        // "แก้ไขเครื่องชั่วคราว" ต้องใช้เครื่องได้ทันทีโดยไม่ต้องให้นักเรียน login ก่อน) ส่งแจ้ง AgentTray
        // เฉพาะตอนสถานะเปลี่ยนจริง (เริ่ม/เลิกบังคับ/ข้อความเปลี่ยน) เหมือน ExamModeStatus
        var effectiveRequireLogin = !_bypassState.IsBypassed && _lastKnownState.Enabled && _state.RequireLogin;
        var currentLoginScreenMessage = _state.LoginScreenMessage;
        if (effectiveRequireLogin != _lastNotifiedRequireLogin || currentLoginScreenMessage != _lastNotifiedLoginScreenMessage)
        {
            _lastNotifiedRequireLogin = effectiveRequireLogin;
            _lastNotifiedLoginScreenMessage = currentLoginScreenMessage;
            await _agentTrayNotifier.SendSignInStatusAsync(effectiveRequireLogin, currentLoginScreenMessage, CancellationToken.None);
        }

        // ป้องกันการเลี่ยง (WP2-1, 2026-10-31) - เหตุผล apply ทุกรอบ/เคลียร์ตอน bypass หรือ enforcement
        // ปิดเหมือนกับ WebsiteControlService ข้างบนทุกอย่าง (ดูคอมเมนต์ยาวด้านบน) ส่ง null = เคลียร์ policy
        var browserHardening = (_bypassState.IsBypassed || !_lastKnownState.Enabled)
            ? null
            : _lastKnownState.BrowserHardening;
        _browserHardening.Apply(browserHardening);

        // Bypass เปิดอยู่ = ครูตั้งใจให้เครื่องนี้หยุด enforce ชั่วคราว (ดู AgentBypassState) ต้องไม่ scan/kill
        // ต่อด้วย _lastKnownState เก่าที่ค้างมาจากก่อน bypass - ไม่งั้น toggle "BYPASS" ที่ AgentTray
        // จะกัน "คำสั่งใหม่" ได้ (ตัด SignalR/poll) แต่กัน "rule ที่ทำงานอยู่" ไม่ได้จริง ซึ่งขัดจุดประสงค์
        // ของปุ่มนี้ที่มีไว้ให้ทำงานกับ process ที่ปกติจะโดน rule เล่นงานโดยไม่ถูกยุ่ง
        //
        // ถ้า enforcement ปิดอยู่ (global หรือ per-machine) ก็ไม่ต้อง scan process เลยเหมือนกัน (website
        // blocklist ถูกจัดการไปแล้วข้างบนแยกจากเงื่อนไขนี้)
        if (_bypassState.IsBypassed || !_lastKnownState.Enabled)
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
            // เหตุผลเดียวกับใน ScanRunningProcessesAsync - bypass ต้องกันทั้ง rule ที่กำลัง enforce อยู่
            // ไม่ใช่แค่คำสั่งใหม่จาก Server เท่านั้น ไม่งั้น process ที่เพิ่งเปิดระหว่าง bypass จะยังโดน
            // WMI watcher นี้ kill อยู่ดีทั้งที่ครูกด BYPASS ไปแล้ว
            if (_bypassState.IsBypassed || !_lastKnownState.Enabled)
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

        // เดิมตรงนี้ return false ทันทีถ้าไม่มี publisher - ซึ่งแปลว่าโปรแกรมที่ "ไม่ได้เซ็นและไม่มี
        // CompanyName" (เกม portable/โหลดมาแทบทั้งหมด) มองไม่เห็นเลยแม้แต่ใน log ตอนนี้ยังข้ามการ
        // เทียบ rule เหมือนเดิม (ไม่มี publisher ก็ไม่มีอะไรให้เทียบจริงๆ) แต่ต้องผ่าน Program
        // Control ก่อนเสมอ ซึ่งไม่ได้พึ่ง publisher เลย - ดู ProgramControlService
        if (!result.HasPublisher)
        {
            return await _programControl.InspectAsync(executablePath, processId, result);
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

        // ไม่มีกฎไหน match - ให้ Program Control ตรวจต่อ (กฎของครูมาก่อนเสมอ ตัวนี้เป็นตาข่ายรับ
        // ของที่ครูยังไม่รู้จัก ไม่ใช่ตัวแทน)
        return await _programControl.InspectAsync(executablePath, processId, result);
    }

    private async Task ExecuteRuleAction(string executablePath, int processId, PublisherCheckResult result, RuleDto rule)
    {
        var actionParts = new List<string>();

        // ---- ส่วนที่ 1: Kill process (ถ้ากฎกำหนดไว้) ----
        if (rule.KillProcess)
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                // kill ทั้ง tree ไม่ใช่ PID เดียว (2026-08-31, จาก audit) - เกมสมัยใหม่ส่วนใหญ่เปิดผ่าน
                // launcher ที่ spawn ตัวเกมจริงเป็น process ลูกคนละ PID แล้ว launcher ก็จบไป การ kill
                // เฉพาะตัวที่ match จึงฆ่าได้แค่ตัวห่อ ปล่อยตัวเกมจริงรันต่อ (และตัวเกมจริงอาจไม่ match
                // rule เดียวกันด้วยซ้ำ เพราะ publisher/ชื่อไฟล์คนละตัว) - CommandProcessor ก็ใช้
                // entireProcessTree อยู่แล้วในที่อื่น ตรงนี้ตกหล่นไปที่เดียว
                process.Kill(entireProcessTree: true);
                actionParts.Add("Killed process tree");
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

    // Tailscale fallback address (optional) - null/ว่าง = ปิดใช้งาน feature นี้ ดู Worker's
    // consecutive-failure/-success counters สำหรับ threshold ที่ตัดสินใจสลับไป/กลับจากที่อยู่นี้
    public string? FallbackBaseUrl { get; set; }
}
