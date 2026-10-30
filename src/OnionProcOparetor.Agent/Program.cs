using OnionProcOparetor.Agent;
using OnionProcOparetor.Agent.Services;

// ---- Diagnostic เขียนไฟล์ตรงๆ ตั้งแต่บรรทัดแรกสุด (ก่อน WebApplication.CreateBuilder) ----
// จับค่า Environment.CurrentDirectory "ดิบ" ที่ Windows SCM ตั้งให้ตอน service เริ่ม
// (sc.exe create ไม่มีทาง set working directory ได้เลย - ปกติจะได้ %SystemRoot%\System32
// เสมอ ไม่ว่า .exe จะอยู่ที่ไหนก็ตาม) เพื่อยืนยัน/ปัดตกสมมติฐานเรื่อง working directory ผิด
StartupDiagnostics.WriteLine("==================================================");
StartupDiagnostics.WriteLine($"OnionProcOparetor.Agent starting - PID {Environment.ProcessId}");
// FileVersion (ไม่ใช่ AssemblyVersion ที่ตั้งใจคงที่ตาม Directory.Build.props เสมอ) - เปลี่ยนทุกรอบ
// publish-all.ps1 (ดู comment ที่นั่น) log ไว้ตรงนี้เพื่อยืนยันได้ทันทีว่า instance ที่กำลังรันอยู่จริง
// เป็น build ล่าสุดที่ deploy ไปหรือเปล่า โดยไม่ต้องเดา/เชื่อแค่ log ว่า "install สำเร็จ"
StartupDiagnostics.WriteLine($"FileVersion จริงของ .exe ที่กำลังรันอยู่นี้: {System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion}");
StartupDiagnostics.WriteLine($"Environment.CurrentDirectory (raw, ตอน process เริ่ม): {Environment.CurrentDirectory}");
StartupDiagnostics.WriteLine($"AppContext.BaseDirectory (ตำแหน่ง .exe จริงเสมอ): {AppContext.BaseDirectory}");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // สำคัญที่สุด: บังคับ ContentRootPath ให้เป็นตำแหน่ง .exe จริงเสมอ แทนที่จะปล่อยให้
    // WebApplication.CreateBuilder ใช้ Directory.GetCurrentDirectory() (ซึ่งตอนรันเป็น
    // Windows Service มักจะเป็น %SystemRoot%\System32 ไม่ใช่ install path) - ถ้าไม่ fix ตรงนี้
    // appsettings.json จะหาไม่เจอ (config โหลดแบบ optional เงียบๆ ไม่ throw) แล้ว ServerSettings
    // จะ fallback ไปใช้ default ในโค้ด (BaseUrl = http://localhost:8787) แทนค่าจริงเสมอ
    ContentRootPath = AppContext.BaseDirectory
});

StartupDiagnostics.WriteLine($"builder.Environment.ContentRootPath (ที่ใช้หา appsettings.json จริง): {builder.Environment.ContentRootPath}");
var expectedAppSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
StartupDiagnostics.WriteLine($"คาดว่า appsettings.json อยู่ที่: {expectedAppSettingsPath} (มีอยู่จริงไหม: {File.Exists(expectedAppSettingsPath)})");

// ---- เสริม file logger เข้ากับ logging pipeline ปกติ (console/debug/eventlog ของ host เดิม) ----
// จำเป็นเพราะตอนรันเป็น Windows Service ไม่มี console attach ทำให้ log ผ่าน console/debug
// หายไปเงียบๆ ไฟล์นี้เปิดดูได้เสมอไม่ว่าจะรันแบบ console ตรงๆ หรือเป็น service จริง
var agentLogPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "OnionProcOparetor", "Agent", "logs", "agent.log");
builder.Logging.AddProvider(new FileLoggerProvider(agentLogPath));
StartupDiagnostics.WriteLine($"ILogger ทั้งหมด (รวม Worker/ServerClient) จะถูกเขียนไปที่ไฟล์นี้ด้วย: {agentLogPath}");

// ---- Local API ฟังแค่ localhost เท่านั้น (ไม่เปิดให้เครื่องอื่นในเครือข่ายเข้าถึง) ----
// ใช้สำหรับ AgentTray ของเครื่องเดียวกันเรียกดูสถานะเท่านั้น
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 8788);
});

// ---- ผูก ServerSettings เข้ากับ appsettings.json ----
var serverSettings = builder.Configuration.GetSection("ServerSettings").Get<ServerSettings>()
    ?? new ServerSettings();
StartupDiagnostics.WriteLine($"ServerSettings:BaseUrl ที่อ่านได้จาก appsettings.json: '{serverSettings.BaseUrl}'");

var envBaseUrlOverride = Environment.GetEnvironmentVariable("ONIONPROC_SERVER_BASE_URL");
StartupDiagnostics.WriteLine($"ENV ONIONPROC_SERVER_BASE_URL: '{envBaseUrlOverride}'");

var resolvedBaseUrl = envBaseUrlOverride;
if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
{
    resolvedBaseUrl = serverSettings.BaseUrl;
}

if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
{
    resolvedBaseUrl = "http://localhost:8787";
}

StartupDiagnostics.WriteLine($"resolvedBaseUrl สุดท้ายที่ agent จะใช้ poll จริง: '{resolvedBaseUrl}'");

builder.Services.Configure<ServerSettings>(options =>
{
    options.BaseUrl = resolvedBaseUrl;
    options.PollIntervalSeconds = serverSettings.PollIntervalSeconds;
    options.FallbackBaseUrl = serverSettings.FallbackBaseUrl;
});

// ---- ผูก HttpClient สำหรับคุยกับ Server กลาง ----
// BaseAddress ไม่ได้ set ตรงนี้อีกต่อไป (เดิม set ผ่าน closure ปิด resolvedBaseUrl) - ย้ายไปให้
// ServerClient's constructor เป็นคน seed เอง จาก IOptions<ServerSettings> เพื่อให้ ServerClient เป็น
// เจ้าของเดียวของ "กำลังชี้ไป URL ไหนอยู่" (จำเป็นสำหรับรองรับสลับไป FallbackBaseUrl ทีหลัง)
builder.Services.AddHttpClient<ServerClient>((serviceProvider, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ---- State แบบ in-memory ที่ Worker เขียน, local API อ่าน ----
builder.Services.AddSingleton<ClientState>();

// ---- Command processing (SignalR real-time + poll fallback ใช้ logic เดียวกัน) ----
// safety net: สตาร์ท AgentTray ใหม่ใน user session ถ้าติดต่อไม่ได้ (นักเรียนปิดทิ้ง/ไม่เคยรัน)
// singleton เพราะ AgentTrayRecoveryService เก็บ cooldown ไว้ใน state ของตัวเอง
builder.Services.AddSingleton<UserSessionLauncher>();
builder.Services.AddSingleton<AgentTrayRecoveryService>();

// ---- WP2b: shared secret ของ loopback API (8788) - สุ่มใหม่ทุกครั้งที่ Agent เริ่ม เก็บใน memory
// เท่านั้น ใช้ปิด /vnc/session-result และ /processes/list-result ด้านล่าง (ดู LoopbackSecret.cs
// ว่าทำไมแยกเป็นคลาสเดี่ยวๆ ไม่มี dependency อื่น - ต้องเป็น singleton เพราะต้องเป็นค่าเดียวกันตลอดอายุ
// Agent process ทั้งฝั่งที่ AgentTrayNotifier ส่งออกไป และฝั่งที่ endpoint ด้านล่างตรวจรับเข้ามา) ----
builder.Services.AddSingleton<LoopbackSecret>();

builder.Services.AddSingleton<AgentTrayNotifier>();

// รายงาน "ผลจริง" ของ command กลับ Server (SignalR ก่อน ไม่ได้ค่อย HTTP) - ต้องเป็น singleton
// เพราะ LabHubConnection มา set SignalRSender ให้ทีหลังตอน StartAsync (ดู CommandResultReporter
// เรื่องการตัด circular dependency ระหว่าง CommandProcessor กับ LabHubConnection)
builder.Services.AddSingleton<CommandResultReporter>();

builder.Services.AddSingleton<CommandProcessor>();

// ---- SignalR hub connection - ชั้นเสริมคู่ขนานกับ poll loop เดิม (ไม่แทนที่) ----
builder.Services.AddSingleton<LabHubConnection>();

// ---- Bypass toggle (Stable 1 เรื่องที่ 2) - state อยู่ใน memory ของ Agent process เท่านั้น
// (ไม่ persist) BypassCoordinator เป็นจุดเดียวที่ toggle จริง (ตัด/ต่อ LabHubConnection) เรียกจาก
// POST /bypass/set ข้างล่าง ----
// AddHttpClient() เปล่าๆ (ไม่ใช่ typed client) เพราะ BypassCoordinator ต้องเป็น Singleton (ถือ
// _activeStaffSessionToken + semaphore ข้ามทุก request) แต่ AddHttpClient<TClient>() ปกติ register
// TClient เป็น Transient - ใช้ IHttpClientFactory.CreateClient() ใน constructor แทน (WP2, 2026-10-30)
builder.Services.AddHttpClient();
builder.Services.AddSingleton<AgentBypassState>();
builder.Services.AddSingleton<BypassCoordinator>();

// ---- VNC session "หลังบ้าน" ฝั่ง Agent (port discovery + firewall + state เท่านั้น - ไม่ spawn
// winvnc.exe เองอีกต่อไป ย้ายไปที่ AgentTray แล้วเพราะ Session 0 isolation ทำให้ Agent capture หน้าจอ
// ไม่ได้เลย ดู comment เต็มใน VncSessionManager.cs) ไม่ใช่ IHostedService อีกต่อไปเช่นกัน (ไม่มี
// subprocess ให้ต้อง stop ตอน Agent หยุดแล้ว) ----
builder.Services.AddSingleton<VncSessionManager>();

// ---- รองรับ Windows Service host ----
builder.Services.AddWindowsService();

// ---- Program Control (2026-08-31) - ตาข่ายรับโปรแกรมที่ระบบ rule เดิมมองไม่เห็น ----
// singleton เพราะถือ blocklist + cache ผลจำแนกไว้ในหน่วยความจำข้ามทุกรอบ scan
builder.Services.AddSingleton<ProgramFingerprinter>();
builder.Services.AddSingleton<ProgramControlService>();
builder.Services.AddSingleton<WebsiteControlService>();

// ---- ผูก Worker เป็น background service หลัก ----
builder.Services.AddHostedService<Worker>();

// ---- Tamper protection (2026-08-31) ----
// เขียน heartbeat ให้ agent-watchdog.ps1 อ่าน + สร้าง Scheduled Task ของ watchdog คืนถ้าถูกลบ
// เป็น hosted service แยกจาก Worker โดยตั้งใจ: ถ้า Worker ตายเพราะบั๊กใดๆ heartbeat ต้องหยุด
// ตามไปด้วยเพื่อให้ watchdog เห็นและ reboot - ถ้ารวมไว้ใน Worker เดียวกันก็ยังได้ผลเหมือนกัน
// แต่แยกไว้ทำให้ทั้ง 2 เรื่องอ่านแยกกันได้ และปิดตัวใดตัวหนึ่งตอน debug ได้โดยไม่กระทบอีกตัว
builder.Services.AddHostedService<TamperProtectionService>();

// ---- เฝ้าดูว่าเส้นทางสำรอง (Tailscale) ยังใช้ได้จริง (2026-08-31) ----
// node key ของ Tailscale อยู่บนไดรฟ์ที่ถูก Reboot Restore Rx freeze เหมือนกัน = หายทุก reboot
// ถ้าไม่ได้ enroll ตอนที่ freeze ปิดอยู่ - service นี้ทำให้ "พังเงียบ" กลายเป็น "พังแล้วมีคนรู้"
// ปิดตัวเองทันทีถ้าไม่ได้ตั้ง FallbackBaseUrl ไว้ (ไม่ได้ใช้ Tailscale ก็ไม่ต้องเตือน)
builder.Services.AddHostedService<TailscaleHealthService>();

var app = builder.Build();

StartupDiagnostics.WriteLine("Host build เสร็จแล้ว กำลัง Run()...");

// ---- Local API endpoints (สำหรับ AgentTray เท่านั้น) ----

app.MapGet("/status", (ClientState state, AgentBypassState bypassState, ServerClient serverClient) =>
{
    return Results.Ok(new
    {
        machineName = state.MachineName,
        clientGuid = state.ClientGuid,
        isEnabled = state.IsEnabled,
        rulesCount = state.RulesCount,
        lastPollAt = state.LastPollAt,
        lastPollSucceeded = state.LastPollSucceeded,
        // เพิ่มไว้เพื่อ debug โดยตรง: ยิง GET http://localhost:8788/status แล้วดู serverBaseUrl
        // ตรงนี้ได้เลยว่า agent ตัวที่กำลังรันอยู่จริงใช้ BaseUrl อะไร (ไม่ใช่แค่ config ตอน startup -
        // อ่านจาก ServerClient.ActiveBaseUrl ที่เป็นค่าจริงหลังสลับไป fallback แล้วด้วย)
        serverBaseUrl = serverClient.ActiveBaseUrl,
        usingFallback = serverClient.IsUsingFallback,
        bypassed = bypassState.IsBypassed
    });
});

app.MapGet("/logs/recent", (ClientState state) =>
{
    return Results.Ok(state.GetRecentLogs());
});

// ---- AgentTray เรียกกลับมาที่นี่หลัง start winvnc.exe เอง (ต้องทำจาก AgentTray เพราะรันใน user
// session จริง - Agent เอง Session 0 capture หน้าจอไม่ได้เลย ดู comment เต็มใน VncSessionManager.cs) -
// loopback เท่านั้น (ConfigureKestrel ผูก 127.0.0.1 ไว้แล้วด้านบน) แต่ "loopback เท่านั้น" ไม่ได้แปลว่า
// "เฉพาะ AgentTray" - process อื่นบนเครื่องเดียวกัน (เช่นนักเรียนเขียน script เอง) ยิง POST ตรงมาที่นี่
// ได้เหมือนกันโดยไม่ต้องผ่าน AgentTray เลย แล้วใส่ PID ปลอมให้ Agent (รันเป็น SYSTEM) เปิด firewall
// rule ให้ได้ (WP2b, 2026-10-30) - ตอนนี้บังคับตรวจ LoopbackSecret ก่อนทุกครั้ง (401 ถ้าไม่มี/ผิด) ----
app.MapPost("/vnc/session-result", async (VncSessionResultRequest request, CommandProcessor commandProcessor, LoopbackSecret loopbackSecret, CancellationToken ct) =>
{
    if (!loopbackSecret.Matches(request.Secret))
    {
        return Results.Unauthorized();
    }

    await commandProcessor.HandleVncSessionResultAsync(
        request.RequestingConsoleSessionId, request.Success, request.Pid, request.Password, request.FailureReason, ct);
    return Results.Ok();
});

// ---- AgentTray เรียกกลับมาที่นี่หลังสแกนโปรแกรมที่มีหน้าต่างอยู่บน desktop เสร็จ (ต้องทำจาก
// AgentTray เพราะรันใน user session จริง - เหตุผลเดียวกับ /vnc/session-result ด้านบน) - loopback
// เท่านั้น รับ Processes เป็น JsonElement ดิบแล้ว forward เป็น raw JSON string ต่อไปเลย ไม่ต้องมี
// RunningProcessDto ฝั่ง Agent เพราะ Agent แค่ relay ผ่านมือ ไม่ได้อ่าน field ไหนเลย - เช็ค LoopbackSecret
// ก่อนเหมือนกับ /vnc/session-result ด้านบน (เหตุผลเดียวกัน WP2b) ----
app.MapPost("/processes/list-result", async (RunningProcessesResultRequest request, CommandProcessor commandProcessor, LoopbackSecret loopbackSecret, CancellationToken ct) =>
{
    if (!loopbackSecret.Matches(request.Secret))
    {
        return Results.Unauthorized();
    }

    var processListJson = request is { Success: true, Processes: not null }
        ? request.Processes.Value.GetRawText()
        : null;

    await commandProcessor.HandleRunningProcessesResultAsync(
        request.RequestingConsoleSessionId, request.Success, processListJson, request.FailureReason, ct);
    return Results.Ok();
});

// ---- AgentTray toggle "Bypass" จากหน้า Status ----
// WP2 (2026-10-30): เดิม login-gate ของ AgentTray + loopback ถือว่าพอแล้ว ("ไม่ต้อง auth เพิ่มที่นี่")
// แต่จริงๆ ไม่พอ - process อื่นบนเครื่องเดียวกันยิง POST ตรงมาที่ loopback นี้ได้เหมือนกัน ไม่ต้องผ่าน
// login ของ AgentTray เลย ตอนนี้บังคับแนบ staffSessionToken ที่ Server ออกให้ (ตรวจที่ BypassCoordinator)
app.MapPost("/bypass/set", async (BypassSetRequest request, BypassCoordinator coordinator) =>
{
    var result = await coordinator.SetBypassedAsync(request.Bypassed, request.StaffSessionToken);

    if (result.ErrorMessage is not null)
    {
        return Results.Json(
            new { bypassed = result.Bypassed, error = result.ErrorMessage },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new { bypassed = result.Bypassed });
});

app.Run();

internal sealed record VncSessionResultRequest(string RequestingConsoleSessionId, bool Success, int Pid, string? Password, string? FailureReason, string? Secret);
internal sealed record RunningProcessesResultRequest(string RequestingConsoleSessionId, bool Success, System.Text.Json.JsonElement? Processes, string? FailureReason, string? Secret);
internal sealed record BypassSetRequest(bool Bypassed, string? StaffSessionToken = null);
