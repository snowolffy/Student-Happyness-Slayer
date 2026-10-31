namespace OnionProcOparetor.AgentTray.Models;

/// <summary>ผลลัพธ์จาก GET http://localhost:8788/status ของ Agent Service</summary>
public class ClientStatusDto
{
    public string MachineName { get; set; } = string.Empty;
    public string ClientGuid { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int RulesCount { get; set; }
    public DateTime? LastPollAt { get; set; }
    public bool LastPollSucceeded { get; set; }
    public bool Bypassed { get; set; }
    public bool UsingFallback { get; set; }

    /// <summary>null = ไม่มีโหมดสอบทำงานอยู่ (WP2-2, 2026-10-31) - ใช้ตั้งค่าป้ายมุมขวาล่างตอน AgentTray
    /// เพิ่งเริ่มทำงาน (เช่น user ล็อกอินใหม่กลางโหมดสอบ) ก่อนจะมี push message ครั้งถัดไปมาอัปเดต</summary>
    public DateTime? ExamModeExpiresAtUtc { get; set; }
}
