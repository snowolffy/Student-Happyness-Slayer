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
            // WP4 (2026-10-30): เก็บชื่อที่แสดง ณ ตอนบันทึกไว้คู่กับ username เสมอ - ถ้ายังไม่ได้ตั้ง
            // DisplayName (ไม่ควรเกิดหลัง migration แต่เผื่อไว้) ใช้ Username แทนไม่ให้ Audit Log ว่างเปล่า
            ActorDisplayName = string.IsNullOrWhiteSpace(admin.DisplayName) ? admin.Username : admin.DisplayName,
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

    public async Task LogStudentActionAsync(
        Student? student,
        string eventType,
        string description,
        string? targetClientGuid = null,
        string? targetMachineName = null,
        string? studentCodeOverride = null)
    {
        _db.ActivityLogEntries.Add(new ActivityLogEntry
        {
            Category = ActivityLogCategory.StudentAction,
            Severity = ActivityLogSeverity.Info,
            EventType = eventType,
            Description = description,
            ActorStudentId = student?.Id,
            ActorStudentCode = student?.StudentCode ?? studentCodeOverride,
            TargetClientGuid = targetClientGuid,
            TargetMachineName = targetMachineName
        });
        await _db.SaveChangesAsync();
    }
}
