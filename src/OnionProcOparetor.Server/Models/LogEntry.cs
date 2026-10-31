namespace OnionProcOparetor.Server.Models;

/// <summary>
/// Log ที่ client ส่งกลับมาเมื่อตรวจพบ process ที่ match กับ rule (หรือเหตุการณ์อื่นๆ)
/// เก็บรวมศูนย์ที่ server เพื่อดูภาพรวมทั้งห้องได้โดยไม่ต้องไล่เช็คทีละเครื่อง
/// </summary>
public class LogEntry
{
    public int Id { get; set; }

    /// <summary>เครื่อง client ที่ส่ง log นี้มา (ผูกกับ ClientMachine.ClientGuid)</summary>
    public string ClientGuid { get; set; } = string.Empty;

    /// <summary>ชื่อเครื่อง ณ เวลาที่บันทึก (เก็บซ้ำไว้กันกรณี ClientMachine ถูกลบทีหลัง)</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>path เต็มของไฟล์ .exe ที่ตรวจพบ</summary>
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>ชื่อ publisher ที่ตรวจพบจริง (จาก signature หรือ metadata)</summary>
    public string? DetectedPublisher { get; set; }

    /// <summary>true = มาจาก digital signature, false = มาจาก metadata (CompanyName) เท่านั้น</summary>
    public bool WasSignedMatch { get; set; }

    /// <summary>Id ของ Rule ที่ match (null ถ้าไม่มีกฎไหน match แต่ยัง log ไว้)</summary>
    public int? MatchedRuleId { get; set; }

    /// <summary>ชื่อกฎที่ match เก็บซ้ำไว้กันกรณี Rule ถูกลบ/แก้ทีหลัง</summary>
    public string? MatchedRuleName { get; set; }

    /// <summary>action ที่ทำไปจริง (เช่น "Blocked", "Logged only", "Ran action command")</summary>
    public string ActionTaken { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// รหัสนักเรียนที่ login อยู่บนเครื่องนี้ตอนเกิด log นี้ (WP3-1, 2026-10-31) - null = ไม่มีใคร login
    /// อยู่ หรือ Agent รุ่นเก่าที่ยังไม่ส่งค่านี้มา (ดู docs/roadmap.md "log ทุกอย่างที่เกิดระหว่าง session
    /// ให้ผูกกับนักเรียน") ฝั่ง Agent แนบมาเองใน POST /api/logs ถ้ามี session เปิดอยู่ ณ ตอนนั้น (งานฝั่ง
    /// Agent เป็นของ WP3-3 - ไฟล์นี้แค่เตรียมที่เก็บไว้ก่อนแบบ additive)
    /// </summary>
    public string? StudentCode { get; set; }
}
