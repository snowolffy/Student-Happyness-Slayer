namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/RuleDto.cs (identical - GET/POST /api/rules)</summary>
public class RuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>[OPTIONAL] narrow เพิ่มจาก publisher เช่น "notepad" - เว้นว่างได้ถ้าไม่รู้ชื่อไฟล์ล่วงหน้า</summary>
    public string? ProcessNameContains { get; set; }

    public bool RequireSignedMatch { get; set; } = true;
    public bool KillProcess { get; set; }
    public string? ActionCommand { get; set; }
    public string? ActionArguments { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>ห้องที่กฎนี้ใช้ด้วย (WP2-3, 2026-10-31) - null = ใช้กับทุกห้อง</summary>
    public int? RoomId { get; set; }

    /// <summary>โชว์ในคอลัมน์ "ใช้กับ" - Rules.razor เป็นคนเซ็ตชื่อจริงให้หลังโหลด room list เสร็จ</summary>
    public string ScopeText { get; set; } = "ทุกห้อง";
}
