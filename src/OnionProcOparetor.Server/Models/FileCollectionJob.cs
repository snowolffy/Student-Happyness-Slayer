namespace OnionProcOparetor.Server.Models;

/// <summary>
/// งาน "เก็บไฟล์" หนึ่งงาน (WP4-3, 2026-11-01 - wireframe tc-files หัวข้อ "เก็บงาน") ครูตั้งชื่องาน +
/// ชนิดไฟล์ที่รับ (ใบ้ filter ให้ OpenFileDialog ฝั่ง AgentTray เท่านั้น ไม่ enforce เข้มที่ Server -
/// ดู FileTransferEndpoints POST .../upload) + เวลาปิดรับ (ไม่บังคับ) แล้วส่ง command "CollectFile" ไป
/// ยังเครื่องเป้าหมาย (ผ่าน /api/commands/{clientGuid} เดิม) ให้ AgentTray โชว่ให้นักเรียนเลือกไฟล์ส่ง
///
/// เป้าหมายของงาน (RoomId) ถูกใช้ "สด" ทุกครั้งที่ GET .../status (ไม่ snapshot รายชื่อเครื่องไว้ตอน
/// สร้างงาน) - เครื่องที่ถูกย้ายเข้า/ออกห้องหลังสร้างงานจะมีผลกับตัวเลข "ได้ X/Y" ทันที ยอมรับได้เพราะ
/// เป็นเครื่องมือใช้งานสด ไม่ใช่ประวัติที่ต้อง snapshot แบบ StudentSession.RoomId
/// </summary>
public class FileCollectionJob
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>เช่น "*.sb3, *.docx" - ใบ้ filter ของ OpenFileDialog ฝั่ง AgentTray เท่านั้น (ไม่บังคับ
    /// ที่ Server) null/ว่าง = รับทุกชนิดไฟล์</summary>
    public string? FileNamePattern { get; set; }

    /// <summary>null = เป้าหมายทั้งหมด (ทุกเครื่อง), มีค่า = เฉพาะเครื่องในห้องนี้ (ดู doc comment บนคลาส)</summary>
    public int? RoomId { get; set; }

    /// <summary>null = ไม่กำหนดเวลาปิดรับ - ยังอัปโหลดได้ตลอดจนกว่าครูจะกด "ปิดงาน" เองหรือ Server/DB ถูกล้าง
    /// (คิวนี้ไม่ persist ข้าม disk freeze ของเครื่องนักเรียน แต่ FileCollectionJob เก็บฝั่ง Server เท่านั้น
    /// ไม่ได้รับผลกระทบจาก disk freeze เลย)</summary>
    public DateTime? ClosesAt { get; set; }

    /// <summary>true = ครูกด "ปิดงาน" เอง (ไม่รับไฟล์เพิ่มแม้ ClosesAt ยังไม่ถึง) - แยกจาก ClosesAt ผ่านไป
    /// แล้วตั้งใจ เพื่อให้ "ปิดก่อนเวลา" กับ "หมดเวลาแล้ว" log ไว้คนละเหตุผลได้ถ้าต้องตรวจย้อนหลัง</summary>
    public bool IsClosedManually { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedByUsername { get; set; } = string.Empty;

    /// <summary>true = ยังเปิดรับไฟล์อยู่จริง (ยังไม่ถูกปิดด้วยมือ และ ClosesAt ยังไม่ถึง/ไม่ได้ตั้งไว้)
    /// ใช้ซ้ำทั้งตอนแสดงผลและตอนตรวจสิทธิ์อัปโหลดจริง (ดู FileTransferEndpoints)</summary>
    public bool IsOpen => !IsClosedManually && (ClosesAt is null || ClosesAt > DateTime.UtcNow);
}
