using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using OnionProcOparetor.OnlineConsole.Components;
using OnionProcOparetor.OnlineConsole.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Same fix as OnionProcOparetor.Server/Program.cs: force ContentRootPath to where the .exe actually
    // lives. When the Service Control Manager starts a service, the working directory is NOT
    // guaranteed to be the app's own folder (often it's %SystemRoot%\System32) - without this,
    // appsettings.json/appsettings.Production.json silently fail to load, and the app falls back to
    // whatever defaults are baked into this file with no obvious error.
    ContentRootPath = AppContext.BaseDirectory
});

// WebApplicationBuilder only wires up the Static Web Assets manifest (what MapStaticAssets() below
// needs to find theme.css/vncInterop.js when running from source rather than `dotnet publish` output)
// when IWebHostEnvironment.IsDevelopment() is true - discovered live: this app normally runs in the
// default "Production" environment (no ASPNETCORE_ENVIRONMENT set) even for `dotnet run` testing, since
// Tailscale:BindMode=Tailnet needs the base appsettings.json, not appsettings.Development.json's
// Loopback override. Calling this unconditionally decouples "serve assets correctly" from "which
// environment name happens to be active" - harmless when actually running published output too.
builder.WebHost.UseStaticWebAssets();

// ==================== Tailscale gating (flow requirement #1) ====================
// OnionProcOparetor.Server intentionally binds ListenAnyIP (it needs 40 lab-LAN agents to reach it).
// OnlineConsole does the opposite on purpose: bind ONLY to this machine's own tailnet interface, so
// nobody who isn't connected to the tailnet can reach so much as the login page - enforced by the
// network stack before any authentication code runs at all, not by a check inside the app.
var bindMode = builder.Configuration.GetValue("Tailscale:BindMode", "Tailnet");
var listenPort = builder.Configuration.GetValue("OnlineConsole:Port", 8788);

IPAddress bindAddress;
if (string.Equals(bindMode, "Loopback", StringComparison.OrdinalIgnoreCase))
{
    // Dev-only escape hatch (see appsettings.Development.json) for working on this app from a machine
    // that isn't actually joined to a tailnet. Never set this in a real deployment's appsettings.json.
    bindAddress = IPAddress.Loopback;
}
else
{
    using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    var startupGateway = new TailscaleGatewayService(startupLoggerFactory.CreateLogger<TailscaleGatewayService>());
    var tailnetIp = startupGateway.ResolveOwnTailnetIpAsync().GetAwaiter().GetResult();

    if (tailnetIp is null || !IPAddress.TryParse(tailnetIp, out bindAddress!))
    {
        throw new InvalidOperationException(
            "หา Tailscale IP ของเครื่องนี้ไม่ได้ (tailscale ip -4 ไม่สำเร็จ) - ต้องติดตั้ง Tailscale และเชื่อมต่อ " +
            "tailnet อยู่แล้วก่อนรัน OnionProcOparetor.OnlineConsole (ต้องรันบนเครื่องเดียวกับ " +
            "OnionProcOparetor.Server เพื่อให้ Screen View/Live View เปิด TCP ตรงไปยัง Agent ในวง LAN ได้ - " +
            "ดู docs/architecture.md) ถ้ากำลังพัฒนาบนเครื่องที่ยังไม่ได้ต่อ tailnet ให้ตั้ง " +
            "Tailscale:BindMode=Loopback ใน appsettings.Development.json แทน");
    }
}

builder.WebHost.ConfigureKestrel(options => options.Listen(bindAddress, listenPort));

// ==================== Server API client ====================
// OnlineConsole is just another client of the existing OnionProcOparetor.Server REST/SignalR API - it
// is not a new backend. Unlike the WPF Console (which rebuilds ApiClient per login attempt because the
// user types the server address fresh each time and HttpClient.BaseAddress is immutable after first
// use), the Server address here is a fixed deployment setting, so one named HttpClient with a fixed
// BaseAddress is registered once.
//
// Registered as a NAMED client + an explicit AddScoped<ApiClient> factory - NOT AddHttpClient<ApiClient>()
// - discovered live, the hard way: AddHttpClient<TClient>() registers TClient as Transient (it only pools
// the underlying HttpMessageHandler, not the typed-client wrapper itself), so every component that
// injected ApiClient was getting its own throwaway instance with no login token on it, even within the
// same circuit. MainLayout would attach the session to one instance and Home.razor would then use a
// completely different, blank one - a 401 on every page load, every time. One ApiClient per scope (=
// one per Blazor circuit, or one per plain HTTP request for the non-Blazor endpoints below) is the whole
// point, so it has to be genuinely Scoped.
var serverBaseUrl = builder.Configuration.GetValue<string>("Server:BaseUrl") ?? "http://127.0.0.1:8787";
builder.Services.AddHttpClient("Server", client =>
{
    client.BaseAddress = new Uri(serverBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped(sp => new ApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Server")));

builder.Services.AddScoped<LabHubClient>();

// Scoped = one per Blazor circuit = one per logged-in browser tab. A singleton here would broadcast
// one admin's "ส่งคำสั่งไม่สำเร็จ" into every other admin's screen.
builder.Services.AddScoped<ToastService>();

builder.Services.AddSingleton<TailscaleGatewayService>();

// NOTE: SidebarState (a scoped service holding "is the nav drawer open") was removed rather than kept.
// It could never have worked: it was toggled from MainLayout, and layouts are rendered STATICALLY under
// this app's per-page interactivity model, so the click handler that flipped it had no circuit to run
// on. The drawer is now opened/closed client-side in wwwroot/js/app.js - see the comment block there.

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12); // matches Server's AuthToken lifetime (Server/Program.cs AddHours(12))
        options.SlidingExpiration = false;
        options.Cookie.Name = "OnlineConsoleAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Normally CookieSecurePolicy.Always, but this app is expected to be reached over plain http on
        // the tailnet interface (no local TLS cert to manage) - Tailscale's own WireGuard encryption is
        // the transport security boundary here, not a certificate on this app.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

// Per-page/component interactivity (not global): Login/ChangePassword stay static server-rendered pages
// with plain <form> posts (see AuthEndpoints.cs for why - SignInAsync needs a real response, which an
// already-streaming Blazor circuit can't give it), while every other page opts into
// @rendermode InteractiveServer individually.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Runs as a normal console app under `dotnet run`/double-click, but also works when the Service
// Control Manager starts it as a Windows Service (see the sc.exe create example in the deployment
// notes) - UseWindowsService() is a no-op outside that context, and automatically adds the Windows
// Event Log logger provider when it IS running as a real service, since there's no console to write to.
builder.Host.UseWindowsService();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Deliberately NOT UseExceptionHandler("/Error") pointing at a Razor component: re-executing the
    // request into another Razor Components endpoint tries to initialize a second
    // RemoteNavigationManager on top of the one already set up for the original request, and throws
    // ("RemoteNavigationManager already initialized") - discovered live, the hard way, when an actual
    // 401 from Server got completely hidden behind that second crash. A plain non-Blazor handler
    // sidesteps the conflict entirely.
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<h1>เกิดข้อผิดพลาด</h1><p>ลองรีเฟรชหน้านี้อีกครั้ง หรือกลับไปหน้า <a href=\"/\">Clients</a></p>");
    }));
}

app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
app.UseAntiforgery();

// MapStaticAssets(), not the older UseStaticFiles() - discovered live: this project's static assets
// (theme.css, vncInterop.js) are served through .NET's newer Static Web Assets manifest
// (OnionProcOparetor.OnlineConsole.staticwebassets.*.json in the build output), not a literal wwwroot/
// folder copied next to the .exe (there isn't one anymore) - UseStaticFiles() only knows how to look for
// the latter, so every asset 404'd and the page rendered as unstyled HTML.
app.MapStaticAssets();

AuthEndpoints.Map(app);
app.MapGet("/ws/vnc/{clientGuid}", VncBridgeEndpoint.HandleAsync).RequireAuthorization();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
