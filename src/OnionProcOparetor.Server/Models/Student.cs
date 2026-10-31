namespace OnionProcOparetor.Server.Models;

/// <summary>
/// บัญชีนักเรียน (WP3-1, 2026-10-31 - ขั้น 3 Sign-in) - ล็อกอินที่ระดับแอปบนจอล็อกของ AgentTray
/// ไม่ใช่บัญชี Windows จริง (ชนกับ disk freeze - ดู docs/roadmap.md หัวข้อ "Sign-in และบัญชี")
///
/// Admin เป็นคนจัดการบัญชีทั้งหมดผ่าน Roster Manager (WP3-2) หรือ Console (WP3-5) - endpoint อยู่ที่
/// Endpoints/RosterEndpoints.cs (CRUD/นำเข้า/เลื่อนชั้น) และ Endpoints/StudentAuthEndpoints.cs (login
/// ของตัวนักเรียนเอง)
///
/// เก็บแค่ password hash เหมือน AdminUser เด็ดขาด ไม่เก็บ password จริง (ดู PasswordHasher - PBKDF2 เดิม)
/// </summary>
public class Student
{
    public int Id { get; set; }

    /// <summary>รหัสนักเรียน - ไม่ซ้ำกัน ใช้ login ได้ (ดู AppDbContext.OnModelCreating)</summary>
    public string StudentCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>เมลโรงเรียน - ไม่ซ้ำกันถ้ามี (nullable unique - ไม่ใช่ทุกคนมีเมล) ใช้ login แทนรหัสนักเรียนได้</summary>
    public string? SchoolEmail { get; set; }

    /// <summary>ชั้น/ห้องเรียน เช่น "ม.2/3" - string ธรรมดา ไม่ใช่ FK (ไม่มี concept ตารางชั้นเรียนแยกตาราง
    /// ในระบบนี้ ต่างจาก Models/Room.cs ที่เป็นห้องแล็บคอมพิวเตอร์ของ Console)</summary>
    public string ClassName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>true = ต้องตั้งรหัสผ่านใหม่ก่อนใช้งานอื่นได้ทั้งหมด (หลังนำเข้าครั้งแรกหรือหลัง Teacher/Admin รีเซ็ต)</summary>
    public bool MustChangePassword { get; set; } = true;

    /// <summary>จำนวนครั้งที่กรอกรหัสผ่านผิดติดกัน - รีเซ็ตเป็น 0 ทุกครั้ง login สำเร็จ (ดู StudentAuthEndpoints)</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>true = บัญชีถูกล็อก (ครบจำนวนครั้งที่ตั้งไว้ใน SignInSettings.LockoutFailedAttempts) -
    /// ปลดได้ด้วยมือเท่านั้น (ครู/Admin) ไม่หมดอายุเองตามเวลา ต่างจาก LoginThrottle ของ AdminUser
    /// ที่ปลดเองอัตโนมัติ เพราะ roadmap ระบุ "ครูปลดได้" ไว้ชัดเจนสำหรับบัญชีนักเรียน</summary>
    public bool IsLocked { get; set; }

    /// <summary>true = ปิดใช้งานบัญชี (นักเรียนจบการศึกษาแล้ว หรือ Admin ปิดเอง) - login ไม่ได้อีก
    /// แต่ประวัติ (StudentSession) ยังอยู่ครบ ต่างจากการลบบัญชีทิ้งถาวร</summary>
    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
