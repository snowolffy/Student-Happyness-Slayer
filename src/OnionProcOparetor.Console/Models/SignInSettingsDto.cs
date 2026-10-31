namespace OnionProcOparetor.Console.Models;

/// <summary>
/// ตั้งค่า Sign-in นักเรียนทั้งระบบ (WP3-5, 2026-10-31) - mirror ของ SignInSettings.cs ฝั่ง Server
/// (WP3-1) ได้จาก GET /api/settings/signin / ส่งกลับด้วย PUT /api/settings/signin
///
/// enum ฝั่ง Server ไม่มี JsonStringEnumConverter ติดตั้งไว้ (ดู Server/Program.cs) จึง serialize เป็น
/// int ธรรมดา - เก็บเป็น int ตรงๆ ที่นี่เหมือนที่ UpdateSignInSettingsRequest ฝั่ง Server ใช้แทนการ mirror
/// enum มาอีกชุด (กันพลาดค่าไม่ตรงกันถ้า Server เปลี่ยนชื่อ enum แต่ไม่เปลี่ยนเลข)
///
/// RequireLoginMode: 0 = Off, 1 = AllRooms, 2 = SelectedRooms
/// MultiDeviceMode:  0 = KickOldSession, 1 = BlockNewSession, 2 = Allow
/// </summary>
public class SignInSettingsDto
{
    public int Id { get; set; }

    public int RequireLoginMode { get; set; }
    public string? RequireLoginRoomIdsCsv { get; set; }
    public bool GuestModeEnabled { get; set; }
    public bool EmergencyCodeEnabled { get; set; } = true;

    public int PasswordMinLength { get; set; } = 6;
    public bool ForceChangePasswordOnFirstLogin { get; set; } = true;
    public int LockoutFailedAttempts { get; set; } = 5;

    public int MultiDeviceMode { get; set; }
    public int AutoLogoutIdleMinutes { get; set; } = 15;
    public bool LogoutOnShutdown { get; set; } = true;

    public bool TeacherCanResetPassword { get; set; } = true;
    public bool ShowStudentNameInConsole { get; set; } = true;
    public string? LoginScreenMessage { get; set; }
    public string LockScreenDefaultMessage { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
