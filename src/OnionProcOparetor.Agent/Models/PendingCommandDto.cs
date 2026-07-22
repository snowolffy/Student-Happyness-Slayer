namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// Command ที่ยังไม่ถูก deliver ซึ่งได้จาก field pendingCommands ของ GET /api/poll/{clientGuid}
/// (fallback path - เจอผ่าน poll เสมอไม่ว่า SignalR จะ push ถึงหรือไม่ก็ตาม)
/// </summary>
public class PendingCommandDto
{
    public int Id { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
