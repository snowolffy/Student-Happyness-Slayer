namespace OnionProcOparetor.Console.Models;

/// <summary>กฎที่ได้จาก GET /api/rules หรือส่งไปตอน POST /api/rules</summary>
public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>[OPTIONAL] narrow เพิ่มจาก publisher เช่น "notepad" - เว้นว่างได้ถ้าไม่รู้ชื่อไฟล์ล่วงหน้า</summary>
    public string? ProcessNameContains { get; set; }

    public bool RequireSignedMatch { get; set; } = true;
    public bool KillProcess { get; set; } = false;
    public string? ActionCommand { get; set; }
    public string? ActionArguments { get; set; }
    public bool IsEnabled { get; set; } = true;
}
