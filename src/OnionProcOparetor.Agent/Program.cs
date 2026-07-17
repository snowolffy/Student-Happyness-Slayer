using OnionProcOparetor.Agent;
using OnionProcOparetor.Agent.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Local API ฟังแค่ localhost เท่านั้น (ไม่เปิดให้เครื่องอื่นในเครือข่ายเข้าถึง) ----
// ใช้สำหรับ AgentTray ของเครื่องเดียวกันเรียกดูสถานะเท่านั้น
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 8788);
});

// ---- ผูก ServerSettings เข้ากับ appsettings.json ----
builder.Services.Configure<ServerSettings>(
    builder.Configuration.GetSection("ServerSettings"));

// ---- ผูก HttpClient สำหรับคุยกับ Server กลาง ----
builder.Services.AddHttpClient<ServerClient>((serviceProvider, client) =>
{
    var settings = builder.Configuration.GetSection("ServerSettings").Get<ServerSettings>()
        ?? new ServerSettings();
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ---- State แบบ in-memory ที่ Worker เขียน, local API อ่าน ----
builder.Services.AddSingleton<ClientState>();

// ---- รองรับ Windows Service host ----
builder.Services.AddWindowsService();

// ---- ผูก Worker เป็น background service หลัก ----
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// ---- Local API endpoints (สำหรับ AgentTray เท่านั้น) ----

app.MapGet("/status", (ClientState state) =>
{
    return Results.Ok(new
    {
        machineName = state.MachineName,
        clientGuid = state.ClientGuid,
        isEnabled = state.IsEnabled,
        rulesCount = state.RulesCount,
        lastPollAt = state.LastPollAt,
        lastPollSucceeded = state.LastPollSucceeded
    });
});

app.MapGet("/logs/recent", (ClientState state) =>
{
    return Results.Ok(state.GetRecentLogs());
});

app.Run();
