namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// Envelope กลางของทุก message ที่ได้รับจาก OnionProcOparetor.Agent ผ่าน named pipe
/// (ต้องตรงกับ AgentTrayPipeMessage ฝั่ง Agent เป๊ะๆ) Type บอกว่าควรทำอะไร:
/// - "Broadcast" — โชว์ popup ข้อความ (ใช้ Message/Title)
/// - "ShowLockScreen" — แสดงหน้าจอล็อกเต็มจอ (Message/Title ไม่ใช้)
/// - "HideLockScreen" — ปิดหน้าจอล็อก (Message/Title ไม่ใช้)
/// - "StartVncSession" — start winvnc.exe ในนี้เอง (ใช้ RequestingConsoleSessionId) - ต้องทำจาก
///   AgentTray เพราะรันใน user session จริง ต่างจาก Agent (Session 0 - capture หน้าจอไม่ได้เลย)
/// - "StopVncSession" — หยุด winvnc.exe (Message/Title/RequestingConsoleSessionId ไม่ใช้) - Agent
///   ส่งเองอัตโนมัติตอนไม่มี Console ต่อเข้ามาเลย/reconnect ไม่ทันเวลา (lazy-start)
/// </summary>
public class AgentTrayPipeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Title { get; set; }
    public string? RequestingConsoleSessionId { get; set; }
}
