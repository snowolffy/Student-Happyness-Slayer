namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ไฟล์หนึ่งไฟล์ที่ครูแจกให้นักเรียน (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน, wireframe tc-files
/// หัวข้อ "แจกไฟล์") ครูอัปโหลดผ่าน POST /api/files/distribute (multipart) แล้ว Console สั่ง command
/// "DownloadFile" (ผ่าน /api/commands/{clientGuid} เดิม) ให้เครื่องเป้าหมายแต่ละเครื่องดึงไฟล์นี้ลงไป
/// เอง (ดู Endpoints/FileTransferEndpoints.cs GET .../content ที่ Agent เรียก)
///
/// เก็บไว้ที่ดิสก์ของ Server ใต้ {StoragePath}\distributed\{StoredFileName} เท่านั้น - StoredFileName
/// เป็น Guid ที่ server สร้างเอง ไม่ใช่ชื่อที่ครู/ไฟล์ส่งมา (กัน path traversal ตั้งแต่ชั้นเก็บ ไม่ต้อง
/// พึ่งการ sanitize ชื่อเดิมเลยตอนเขียนดิสก์จริง - ดู FileTransferPaths.SanitizeFileName ที่ใช้แค่ตอน
/// โชว์/ตั้งชื่อไฟล์ปลายทางบนเครื่องนักเรียนเท่านั้น)
///
/// ไม่มี retention/cleanup อัตโนมัติในรอบนี้ตั้งใจ - ไฟล์แจกมักเป็นของชั่วคราว (ใบงาน 1 คาบ) ครูลบเองได้
/// จากดิสก์ Server ถ้าต้องการ (ยังไม่มี endpoint ลบในรอบนี้ ดูหัวข้อ "ประเด็นที่ต้องตัดสิน" ในรายงาน WP)
/// </summary>
public class DistributedFile
{
    public int Id { get; set; }

    /// <summary>ชื่อไฟล์เดิมตามที่ครูอัปโหลด (แสดงผลเท่านั้น/ใช้เป็นชื่อไฟล์ปลายทางบนเครื่องนักเรียนหลัง
    /// sanitize อีกชั้นที่ Agent - ไม่ใช่ชื่อที่ใช้เก็บจริงบนดิสก์ Server)</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>ชื่อไฟล์จริงบนดิสก์ Server (Guid.ToString("N") + นามสกุลเดิม) - ดู doc comment บนคลาสนี้</summary>
    public string StoredFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? ContentType { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string UploadedByUsername { get; set; } = string.Empty;
}
