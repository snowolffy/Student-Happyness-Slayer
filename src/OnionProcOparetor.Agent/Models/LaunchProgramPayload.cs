namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "LaunchProgram" (WP4-1, 2026-11-01) - ส่งมาแค่ ProgramId เท่านั้น ไม่ส่ง
/// Path/Arguments มาด้วยตั้งใจ: Agent ต้องไปขอ Path/Arguments จริงจาก Server เอง
/// (GET /api/launchable-programs/{id}/for-agent) ทุกครั้งก่อนรัน เพื่อตรวจซ้ำว่าโปรแกรมนี้ยังอยู่ใน
/// รายการที่ Admin อนุญาตจริงไหม ณ เวลาที่รัน (ไม่ใช่ตอนที่ Console ส่งคำสั่ง - รายการอาจถูกแก้/ลบไปแล้ว
/// ระหว่างที่ command ค้างอยู่ใน queue) ดู CommandProcessor.HandleLaunchProgramAsync
/// </summary>
public class LaunchProgramPayload
{
    public int ProgramId { get; set; }
}

/// <summary>ผลลัพธ์ของ GET /api/launchable-programs/{id}/for-agent ฝั่ง Agent</summary>
public class LaunchableProgramDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Arguments { get; set; }
}
