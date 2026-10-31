namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ชุด Allow-list สำเร็จรูป/กำหนดเอง สำหรับ "โหมดสอบ" (WP2-2, 2026-10-31) - ตัวเลือกที่ครูกดเลือกได้
/// ตอนเปิดโหมดสอบ (ดู ExamSession) แต่ละชุดมีโดเมนประกอบของตัวเอง (ดู AllowListPresetDomain) เช่น
/// "Google Forms" ต้องมี accounts.google.com + gstatic.com ด้วย ไม่งั้นหน้าเว็บพัง (ดู roadmap ขั้น 2)
///
/// Admin แก้ชุดได้ที่ Website Control › ชุด Allow-list (WebsiteAllowListTab) ครูแค่กดเลือกชุดตอนเปิด
/// โหมดสอบ - ไม่มีการจำกัดสิทธิ์แก้ไว้ใน model นี้โดยตรง ดู AllowListPresetEndpoints (GET เปิดให้ Teacher
/// อ่านได้ ส่วนแก้ไข/ลบ/เพิ่มต้อง Admin เท่านั้น เหมือน WebsiteRule)
/// </summary>
public class AllowListPreset
{
    public int Id { get; set; }

    /// <summary>ชื่อชุด เช่น "Google Forms"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>true = ชุดสำเร็จรูปที่ seed มาตอน migration (Google Forms/Classroom/Quizizz/Kahoot/
    /// Scratch/Canva) - แค่ metadata ไว้แสดงผล ("ชุดสำเร็จรูป" บน wireframe con-website-allow) ไม่ได้
    /// กันการแก้ไข/ลบ - ครูทำสำเนาหรือลบชุดสำเร็จรูปได้ตามปกติเหมือนชุดที่สร้างเอง</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
