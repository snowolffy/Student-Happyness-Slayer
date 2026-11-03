namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "CollectFile" (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน "เก็บงาน") - ส่งมาแค่
/// JobId เท่านั้น (เหมือน LaunchProgramPayload.ProgramId) Agent ไปขอรายละเอียดจริง (ชื่องาน/ชนิดไฟล์/
/// เวลาปิดรับ/ขนาดสูงสุด) จาก Server เอง (GET /api/files/collection-jobs/{id}/for-agent) ทุกครั้งก่อน
/// ส่งต่อให้ AgentTray เพื่อตรวจซ้ำว่างานนี้ยังเปิดรับอยู่จริงไหม ณ เวลาที่ได้รับคำสั่ง - ดู
/// CommandProcessor.HandleCollectFileAsync
/// </summary>
public class CollectFilePayload
{
    public int JobId { get; set; }
}

/// <summary>ผลลัพธ์ของ GET /api/files/collection-jobs/{id}/for-agent ฝั่ง Agent</summary>
public class CollectionJobForAgentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FileNamePattern { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsOpen { get; set; }
    public int MaxFileSizeMb { get; set; }
}
