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

    // ---- Sign-in (WP3-3, 2026-10-31) - เหตุผลเดียวกับ ExamModeExpiresAtUtc ด้านบน (pull ตอนเริ่ม
    // ทำงาน ก่อนจะมี push "SignInStatus" ครั้งถัดไปมาอัปเดต) ----
    public bool RequireLogin { get; set; }
    public string? LoginScreenMessage { get; set; }
    public string? LockScreenDefaultMessage { get; set; }
    public int AutoLogoutIdleMinutes { get; set; } = 15;
    public string? CurrentStudentCode { get; set; }
    public string? CurrentStudentName { get; set; }

    /// <summary>true = เครื่องนี้ตั้งค่า secret รหัสฉุกเฉินไว้แล้ว (WP3-4b, 2026-10-31) - StudentLoginWindow ใช้เปิด/ปิดลิงก์ "รหัสฉุกเฉิน"</summary>
    public bool EmergencyCodeConfigured { get; set; }

    /// <summary>true = เครื่องนี้มีรายการค้างอยู่ในคิวยกมือตอนนี้ (WP4-2, 2026-11-01 - แก้ตาม feedback ก่อน
    /// merge) RaiseHandBadgeWindow poll ค่านี้ทุก ~5 วิ (เหมือน StudentLoginWindow poll emergencyCodeConfigured)
    /// แล้วปิดตัวเองเมื่อกลายเป็น false</summary>
    public bool RaisedHand { get; set; }

    /// <summary>ตำแหน่งในคิวปัจจุบัน (1 = คิวแรก) - มีค่าเฉพาะตอน RaisedHand เป็น true</summary>
    public int? RaiseHandQueuePosition { get; set; }
}
