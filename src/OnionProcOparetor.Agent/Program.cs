using OnionProcOparetor.Agent;
using OnionProcOparetor.Agent.Services;

// ---- Diagnostic เขียนไฟล์ตรงๆ ตั้งแต่บรรทัดแรกสุด (ก่อน WebApplication.CreateBuilder) ----
// จับค่า Environment.CurrentDirectory "ดิบ" ที่ Windows SCM ตั้งให้ตอน service เริ่ม
// (sc.exe create ไม่มีทาง set working directory ได้เลย - ปกติจะได้ %SystemRoot%\System32
// เสมอ ไม่ว่า .exe จะอยู่ที่ไหนก็ตาม) เพื่อยืนยัน/ปัดตกสมมติฐานเรื่อง working directory ผิด
StartupDiagnostics.WriteLine("==================================================");
StartupDiagnostics.WriteLine($"OnionProcOparetor.Agent starting - PID {Environment.ProcessId}");
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
});

// ---- ผูก HttpClient สำหรับคุยกับ Server กลาง ----
builder.Services.AddHttpClient<ServerClient>((serviceProvider, client) =>
{
    client.BaseAddress = new Uri(resolvedBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ---- State แบบ in-memory ที่ Worker เขียน, local API อ่าน ----
builder.Services.AddSingleton<ClientState>();

// ---- รองรับ Windows Service host ----
builder.Services.AddWindowsService();

// ---- ผูก Worker เป็น background service หลัก ----
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

StartupDiagnostics.WriteLine("Host build เสร็จแล้ว กำลัง Run()...");

// ---- Local API endpoints (สำหรับ AgentTray เท่านั้น) ----

app.MapGet("/status", (ClientState state, Microsoft.Extensions.Options.IOptions<ServerSettings> settings) =>
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
        // ตรงนี้ได้เลยว่า agent ตัวที่กำลังรันอยู่จริงใช้ BaseUrl อะไร ไม่ต้องเดาจากไฟล์ config
        serverBaseUrl = settings.Value.BaseUrl
    });
});

app.MapGet("/logs/recent", (ClientState state) =>
{
    return Results.Ok(state.GetRecentLogs());
});

app.Run();
