namespace OnionProcOparetor.RosterManager.Models;

/// <summary>ตรงกับ RosterEndpoints.ToPublicDto ฝั่ง Server (ไม่มี PasswordHash/PasswordSalt เด็ดขาด)</summary>
public class StudentDto
{
    public int Id { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? SchoolEmail { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public bool IsLocked { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>ป้ายสถานะตามลำดับความสำคัญเดียวกับตัวกรอง "สถานะบัญชี" ของ nf-roster</summary>
    public string StatusLabel =>
        IsDisabled ? "ปิดใช้งาน" :
        IsLocked ? "ถูกล็อก" :
        MustChangePassword ? "ต้องเปลี่ยนรหัส" :
        "ปกติ";
}

/// <summary>ชั้น/ห้อง + จำนวนคน - จาก GET /api/students/promote/preview (ใช้ซ้ำสำหรับ sidebar nf-roster ด้วย)</summary>
public class ClassCountDto
{
    public string ClassName { get; set; } = string.Empty;
    public int Count { get; set; }
}
