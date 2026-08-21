using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly ActivityLogDbContext _db;

    public ActivityLogService(ActivityLogDbContext db)
    {
        _db = db;
    }

    public async Task LogAdminActionAsync(
        AdminUser admin,
        string eventType,
        string description,
        string? targetClientGuid = null,
        string? targetMachineName = null,
        string? detailJson = null)
    {
        _db.ActivityLogEntries.Add(new ActivityLogEntry
        {
            Category = ActivityLogCategory.AdminAction,
            Severity = ActivityLogSeverity.Info,
            EventType = eventType,
            Description = description,
            ActorAdminUserId = admin.Id,
            ActorUsername = admin.Username,
            TargetClientGuid = targetClientGuid,
            TargetMachineName = targetMachineName,
            DetailJson = detailJson
        });
        await _db.SaveChangesAsync();
    }

    public async Task LogAgentConnectivityAsync(
        string eventType,
        string description,
        string clientGuid,
        string? machineName)
    {
        _db.ActivityLogEntries.Add(new ActivityLogEntry
        {
            Category = ActivityLogCategory.AgentConnectivity,
            Severity = ActivityLogSeverity.Info,
            EventType = eventType,
            Description = description,
            TargetClientGuid = clientGuid,
            TargetMachineName = machineName
        });
        await _db.SaveChangesAsync();
    }
}
