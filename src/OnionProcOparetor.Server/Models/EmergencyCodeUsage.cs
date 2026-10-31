namespace OnionProcOparetor.Server.Models;

/// <summary>
/// บันทึกย้อนหลังว่าเครื่องไหนถูกปลดด้วยรหัสฉุกเฉินเมื่อไร (WP3-4, 2026-10-31) - Agent เก็บไว้ใน
/// memory ตอน Server ล่ม (ไม่ persist ฝั่ง client - disk freeze) แล้วส่งขึ้นมาตอนกลับมาออนไลน์
/// (POST /api/emergency-code/usage) ปลดแบบไม่ระบุชื่อเสมอ (ไม่มี StudentId - ตอนนั้นยังไม่มีใคร login)
/// ยอมรับการสูญถ้า Agent restart/reboot ก่อนส่งขึ้นสำเร็จ (ตามที่ roadmap บอกไว้ตรงๆ)
/// </summary>
public class EmergencyCodeUsage
{
    public int Id { get; set; }

    public string ClientGuid { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    /// <summary>เวลาที่ Agent ปลดจริง (เวลาเครื่อง - อาจคลาดเคลื่อนจาก Server เล็กน้อย)</summary>
    public DateTime UsedAtUtc { get; set; }

    /// <summary>เวลาที่ Server ได้รับรายงานนี้ (หลัง Server กลับมาออนไลน์แล้ว)</summary>
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
