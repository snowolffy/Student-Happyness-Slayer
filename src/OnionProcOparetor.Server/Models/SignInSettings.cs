namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ตั้งค่า Sign-in นักเรียนทั้งระบบ (WP3-1, 2026-10-31) - เก็บเป็น single row เดียว (Id = 1) เหมือน
/// GlobalState.cs ตรงตาม wireframe docs/wireframes/nf-signin-settings.dc.html
/// </summary>
public class SignInSettings
{
    public int Id { get; set; }

    /// <summary>บังคับล็อกอินก่อนใช้เครื่องไหม - ดู RequireLoginRoomIdsCsv ถ้าเป็น SelectedRooms</summary>
    public RequireLoginMode RequireLoginMode { get; set; } = RequireLoginMode.Off;

    /// <summary>รายการ Room.Id ที่บังคับล็อกอิน คั่นด้วย "," - มีผลเฉพาะตอน RequireLoginMode == SelectedRooms
    /// เท่านั้น เก็บเป็น string ธรรมดาแทนตารางแยกเพราะจำนวนห้องน้อย (ห้องแล็บทั้งโรงเรียนไม่ถึง 10 ห้อง)
    /// และกันไม่ต้องมี join table + migration เพิ่มอีกตาราง</summary>
    public string? RequireLoginRoomIdsCsv { get; set; }

    /// <summary>true = เข้าใช้แบบไม่ระบุชื่อได้ (บันทึกเป็น guest แทนนักเรียนจริง)</summary>
    public bool GuestModeEnabled { get; set; }

    /// <summary>true = ใช้รหัสฉุกเฉิน (TOTP) ได้เมื่อ Server ล่ม - ตัว TOTP จริงเป็นงานของ WP3-4
    /// ฟิลด์นี้แค่เก็บ toggle เปิด/ปิดของหน้าตั้งค่าเดียวกัน</summary>
    public bool EmergencyCodeEnabled { get; set; } = true;

    /// <summary>ความยาวรหัสผ่านขั้นต่ำของนักเรียน</summary>
    public int PasswordMinLength { get; set; } = 6;

    /// <summary>บังคับเปลี่ยนรหัสผ่านตอนล็อกอินครั้งแรก (หลังนำเข้าหรือหลังถูกรีเซ็ต)</summary>
    public bool ForceChangePasswordOnFirstLogin { get; set; } = true;

    /// <summary>กรอกผิดกี่ครั้งติดกันถึงจะล็อกบัญชี (ครู/Admin ปลดได้ - ดู Student.IsLocked)</summary>
    public int LockoutFailedAttempts { get; set; } = 5;

    /// <summary>กติกา "บัญชีเดียวหลายเครื่อง"</summary>
    public MultiDeviceLoginMode MultiDeviceMode { get; set; } = MultiDeviceLoginMode.KickOldSession;

    /// <summary>ออกจากระบบเองเมื่อไม่ได้ใช้งานกี่นาที (ไม่ขยับเมาส์/คีย์บอร์ด)</summary>
    public int AutoLogoutIdleMinutes { get; set; } = 15;

    /// <summary>true = ออกจากระบบเองเมื่อปิด/รีสตาร์ทเครื่อง</summary>
    public bool LogoutOnShutdown { get; set; } = true;

    /// <summary>true = Teacher รีเซ็ตรหัสผ่าน/ปลดล็อกบัญชีนักเรียนได้ด้วย (false = Admin เท่านั้น)</summary>
    public bool TeacherCanResetPassword { get; set; } = true;

    /// <summary>แสดงชื่อนักเรียนใน Console ไหม (ตาราง Clients, ผังที่นั่ง, Live View)</summary>
    public bool ShowStudentNameInConsole { get; set; } = true;

    /// <summary>ข้อความบนจอล็อกอิน เช่น ชื่อโรงเรียน กฎการใช้ห้อง - null/ว่าง = ไม่แสดง</summary>
    public string? LoginScreenMessage { get; set; }

    /// <summary>ข้อความเริ่มต้นตอนครูล็อกจอ - ครูพิมพ์ทับเฉพาะครั้งตอนกดล็อกได้ (ไม่แก้ค่า default นี้)</summary>
    public string LockScreenDefaultMessage { get; set; } = "กรุณาละสายตาจากหน้าจอและฟังครูผู้สอน";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว (เก็บเป็น int ใน SQLite)</summary>
public enum RequireLoginMode
{
    /// <summary>ปิด - ใช้เครื่องได้เลยแบบเดิม ไม่ต้องล็อกอิน</summary>
    Off,

    /// <summary>เปิดบังคับล็อกอินทุกห้อง</summary>
    AllRooms,

    /// <summary>เปิดบังคับล็อกอินเฉพาะห้องที่เลือก (ดู RequireLoginRoomIdsCsv)</summary>
    SelectedRooms
}

/// <summary>ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว (เก็บเป็น int ใน SQLite)</summary>
public enum MultiDeviceLoginMode
{
    /// <summary>ห้าม - เตะเครื่องเก่าออกแล้วให้ใช้เครื่องใหม่ได้ (ค่าเริ่มต้นตาม roadmap)</summary>
    KickOldSession,

    /// <summary>ห้าม - ไม่ให้เข้าเครื่องใหม่จนกว่าจะออกจากเครื่องเดิมก่อน</summary>
    BlockNewSession,

    /// <summary>อนุญาตให้ login พร้อมกันได้หลายเครื่อง</summary>
    Allow
}
