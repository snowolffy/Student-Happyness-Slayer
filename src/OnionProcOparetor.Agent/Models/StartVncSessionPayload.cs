namespace OnionProcOparetor.Agent.Models;

/// <summary>Payload ของ command "StartVncSession" - ระบุว่า Console instance ไหน (session group บน SignalR) ที่ต้องส่งผลลัพธ์กลับไป</summary>
public class StartVncSessionPayload
{
    public string RequestingConsoleSessionId { get; set; } = string.Empty;
}
