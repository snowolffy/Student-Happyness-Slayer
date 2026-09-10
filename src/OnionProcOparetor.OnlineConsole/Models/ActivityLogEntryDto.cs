namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/ActivityLogEntryDto.cs (identical - GET /api/activitylog)</summary>
public class ActivityLogEntryDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>"AdminAction" | "AgentConnectivity" | "SystemEvent"</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>"Info" | "Warning" | "Error" | "Critical"</summary>
    public string Severity { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int? ActorAdminUserId { get; set; }
    public string? ActorUsername { get; set; }

    public string? TargetClientGuid { get; set; }
    public string? TargetMachineName { get; set; }

    public string? DetailJson { get; set; }
}

/// <summary>รูปแบบหน้าเดียวที่ GET /api/activitylog คืนมา</summary>
public class ActivityLogPageDto
{
    public List<ActivityLogEntryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
