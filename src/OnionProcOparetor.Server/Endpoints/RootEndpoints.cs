using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>endpoint สถานะของ service + SignalR hub</summary>
public static class RootEndpoints
{
    public static WebApplication MapRootEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new { service = "OnionProcOparetor.Server", status = "running" }));

        // ---- SignalR hub: real-time command push + live status (ชั้นเสริมคู่ขนานกับ poll เดิม) ----
        app.MapHub<LabHub>("/hubs/lab");

        return app;
    }
}
