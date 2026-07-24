namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// Envelope กลางของทุก message ที่ได้รับจาก OnionProcOparetor.Agent ผ่าน named pipe
/// (ต้องตรงกับ AgentTrayPipeMessage ฝั่ง Agent เป๊ะๆ) Type บอกว่าควรทำอะไร:
/// - "Broadcast" — โชว์ popup ข้อความ (ใช้ Message/Title)
/// - "ShowLockScreen" — แสดงหน้าจอล็อกเต็มจอ (Message/Title ไม่ใช้)
/// - "HideLockScreen" — ปิดหน้าจอล็อก (Message/Title ไม่ใช้)
/// </summary>
public class AgentTrayPipeMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Title { get; set; }
}
