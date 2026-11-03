namespace OnionProcOparetor.AgentTray.Models;

/// <summary>คำขอ "เก็บไฟล์" หนึ่งงานจาก Agent (WP4-3, 2026-11-01) - ดู BroadcastPipeListener.CollectFileRequested
/// และ CollectFileRequestWindow ที่โชว่ให้นักเรียนเลือกไฟล์ส่งจากข้อมูลนี้</summary>
public class CollectFileRequestDto
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;

    /// <summary>เช่น "*.sb3, *.docx" - ใบ้ filter ของ OpenFileDialog เท่านั้น null/ว่าง = รับทุกชนิดไฟล์</summary>
    public string? FileNamePattern { get; set; }

    public DateTime? ClosesAtUtc { get; set; }

    public int MaxFileSizeMb { get; set; } = 100;
}
