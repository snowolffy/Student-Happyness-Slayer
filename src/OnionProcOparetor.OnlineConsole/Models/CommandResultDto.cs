namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>
/// Ported from OnionProcOparetor.Console/Models/CommandResultDto.cs (identical). ExecutionStatus is a
/// string (not an enum) on purpose - there's no shared contracts library between this project and
/// Server, so comparing by name avoids silent value drift if Server's enum ever gets a member inserted
/// in the middle.
/// </summary>
public class CommandResultDto
{
    public int CommandId { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string? BatchId { get; set; }

    /// <summary>"Pending" | "Succeeded" | "Failed" | "TimedOut"</summary>
    public string ExecutionStatus { get; set; } = "Pending";

    public string? ExecutionMessage { get; set; }
    public DateTime? ExecutedAt { get; set; }

    /// <summary>"Pending" | "Delivered" | "Failed"</summary>
    public string DeliveryStatus { get; set; } = "Pending";
}
