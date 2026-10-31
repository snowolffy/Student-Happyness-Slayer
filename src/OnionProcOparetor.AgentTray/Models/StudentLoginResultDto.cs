namespace OnionProcOparetor.AgentTray.Models;

/// <summary>
/// รูปร่าง JSON ตอบกลับจาก POST /api/students/login และ /api/students/change-password ฝั่ง Server
/// (ดู StudentAuthEndpoints.cs) รวมทุก field ที่เป็นไปได้ไว้ในคลาสเดียว (status ต่างกันใช้ field ต่างกัน
/// - ดู Status ว่าอันไหนมีค่าจริงบ้าง) เพราะ response shape ไม่คงที่ตาม status:
/// - "InvalidCredentials" / "Locked" - ไม่มี field อื่นเพิ่ม
/// - "LoggedInElsewhere" - OtherMachineName, CanForce
/// - "MustChangePassword" - PendingToken, FullName, PasswordMinLength
/// - "Success" - SessionId, Student
/// - Error ไม่ว่าง = HTTP ไม่ใช่ 2xx (เช่น /change-password ตอบ 400 BadRequest) ไม่มี Status
/// </summary>
public class StudentLoginResultDto
{
    public string? Status { get; set; }
    public string? OtherMachineName { get; set; }
    public bool CanForce { get; set; }
    public string? PendingToken { get; set; }
    public string? FullName { get; set; }
    public int PasswordMinLength { get; set; }
    public int? SessionId { get; set; }
    public StudentDto? Student { get; set; }
    public string? Error { get; set; }
}

public class StudentDto
{
    public int Id { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
}
