namespace OnionProcOparetor.Server.Models;

/// <summary>
/// โปรแกรมที่ Admin อนุญาตให้เปิดจาก Console/OnlineConsole ได้ (WP4-1, 2026-11-01)
///
/// ทำไมต้องมีรายการนี้แทนให้ Teacher พิมพ์ path เอง: ถ้าให้พิมพ์ path อิสระ Teacher (หรือใครก็ตามที่มี
/// auth token) จะสั่งรันไฟล์อะไรก็ได้บน 40 เครื่องพร้อมกัน (ดู docs/roadmap.md ขั้น 4 "ห้ามรับ path
/// อิสระจาก Teacher") Admin เป็นคนกำหนดรายการที่อนุญาตไว้ล่วงหน้าในตารางนี้เท่านั้น แล้ว Agent
/// re-validate ซ้ำกับรายการนี้อีกชั้นก่อนรันจริงเสมอ (เรียก GET /api/launchable-programs/{id}/for-agent -
/// ดู Agent/Services/CommandProcessor.cs HandleLaunchProgramAsync)
/// </summary>
public class LaunchableProgram
{
    public int Id { get; set; }

    /// <summary>ชื่อที่แสดงใน dropdown ของ Console/OnlineConsole เช่น "Scratch Desktop"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>path เต็มของ .exe บนเครื่อง client (ต้องเหมือนกันทุกเครื่องในแล็บ)</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>arguments ที่จะส่งให้โปรแกรมตอนเปิด (ไม่บังคับ)</summary>
    public string? Arguments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
