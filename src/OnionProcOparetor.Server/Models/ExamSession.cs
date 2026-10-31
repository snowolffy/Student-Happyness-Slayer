namespace OnionProcOparetor.Server.Models;

/// <summary>
/// "โหมดสอบ" ครั้งหนึ่ง (WP2-2, 2026-10-31) - ตั้ง allow-list ชั่วคราวให้เครื่องกลุ่มหนึ่ง ต้องมี
/// <see cref="ExpiresAt"/> เสมอ (บังคับที่ ExamSessionEndpoints - ไม่มีโหมด "ไม่มีกำหนด") เพราะ Agent
/// ต้องใช้ค่านี้หมดเวลาเองได้แม้ Server จะล่มระหว่างนั้น (ดู Agent/Services/ExamModeState.cs)
///
/// ไม่มี nav property ไปตารางอื่น (ตรงกับสไตล์เดิมของ repo - ดู comment ใน AllowListPresetDomain)
/// "ใช้งานอยู่ไหม" คำนวณจาก EndedAt/ExpiresAt สดทุกครั้งที่อ่าน (ดู IsActiveAt) ไม่เก็บเป็น flag แยก
/// เพื่อไม่ต้องมี background sweep ใดๆ มาคอยปิดแถวตอนหมดเวลา - Server เองก็ "หมดเวลาเฉยๆ" แบบ Agent
/// </summary>
public class ExamSession
{
    public int Id { get; set; }

    /// <summary>ไม่ null = กำหนดเป้าเป็นทั้งห้องนี้ (ทุกเครื่องที่ ClientMachine.RoomId ตรงกัน) ต้องมีค่าใดค่า
    /// หนึ่งระหว่างนี้กับ ClientGuidsCsv เท่านั้น (บังคับที่ endpoint)</summary>
    public int? RoomId { get; set; }

    /// <summary>ไม่ null = กำหนดเป้าเป็นเครื่องที่เลือกไว้ (comma-separated ClientGuid) - รูปแบบเดียวกับ
    /// SignInSettings.RequireLoginRoomIdsCsv (CSV แทน join table เพราะเป็นรายการที่ไม่ต้อง CRUD ทีละแถว)</summary>
    public string? ClientGuidsCsv { get; set; }

    /// <summary>ชุด Allow-list ที่เลือกไว้ (comma-separated AllowListPreset.Id) - เลือกได้หลายชุดพร้อมกัน
    /// ตาม wireframe nf-exam ("เลือกชุดสำเร็จรูปได้หลายชุด")</summary>
    public string PresetIdsCsv { get; set; } = string.Empty;

    /// <summary>โดเมนเพิ่มเติมที่ครูพิมพ์เองตอนเปิดโหมดสอบ (comma-separated) - ไม่ผูกกับ preset ไหน</summary>
    public string? ExtraDomainsCsv { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>เวลาที่จะกลับเป็นกฎปกติเอง - บังคับต้องมีเสมอ (ดู doc comment บนคลาส)</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>ไม่ null = ถูกยกเลิกก่อนเวลาโดยครู (ดู EndedReason) - ยังไม่ถึง ExpiresAt ก็ถือว่าจบแล้ว</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>"CancelledEarly" เท่านั้นตอนนี้ (null = ยังไม่จบ หรือจบเพราะหมดเวลาตามปกติ)</summary>
    public string? EndedReason { get; set; }

    /// <summary>Username ของครู/Admin ที่เปิดโหมดสอบนี้ - ไว้แสดงผล/audit เท่านั้น ไม่ใช่ FK</summary>
    public string StartedByUsername { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"กำลังทำงานอยู่ไหม ณ เวลา at" - ยังไม่ถูกยกเลิก และยังไม่ถึง ExpiresAt</summary>
    public bool IsActiveAt(DateTime at) => EndedAt is null && at < ExpiresAt;
}
