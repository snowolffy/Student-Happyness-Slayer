namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/RaiseHandQueueEntryDto.cs - ตรงกับ
/// RaiseHandQueueEntryResponse ฝั่ง Server (WP4-2, 2026-11-01)</summary>
public class RaiseHandQueueEntryDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? StudentName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime RaisedAt { get; set; }
    public int QueuePosition { get; set; }

    public string StudentDisplayText => string.IsNullOrEmpty(StudentName) ? "—" : StudentName;
}
