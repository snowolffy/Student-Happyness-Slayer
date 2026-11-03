namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ไฟล์หนึ่งไฟล์ที่เก็บได้จากเครื่องนักเรียนหนึ่งเครื่อง สำหรับงานเก็บไฟล์หนึ่งงาน (WP4-3, 2026-11-01)
/// มีได้แค่ 1 แถวต่อ (JobId, ClientGuid) เท่านั้น - ส่งซ้ำทับของเดิม (นักเรียนส่งผิดไฟล์แล้วส่งใหม่ได้
/// ไม่ต้องให้ครูลบแถวเก่าเอง) ดู FileTransferEndpoints POST .../upload ที่ upsert ด้วยเงื่อนไขนี้
///
/// เก็บไว้ที่ดิสก์ของ Server ใต้ {StoragePath}\collected\{JobId}_{ชื่องานที่ sanitize แล้ว}\
/// {ชื่อนักเรียน หรือ ชื่อเครื่องถ้าไม่ได้ login}\{ชื่อไฟล์ที่ sanitize แล้ว} - ตรงกับ wireframe tc-files
/// "เก็บลง...แยกโฟลเดอร์ตาม...ชื่อนักเรียน (ชื่อมาจากการล็อกอิน)" (ไม่มีชั้น "ห้องเรียน"/"วันที่" แยกเพิ่ม
/// ในรอบนี้ตั้งใจลดสโคป - ดู "ประเด็นที่ต้องตัดสิน" ในรายงาน WP)
/// </summary>
public class CollectedFile
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public string ClientGuid { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    /// <summary>ชื่อนักเรียนที่ login อยู่ตอนอัปโหลด (จาก StudentSession ที่ยังเปิดอยู่ของ ClientGuid นี้ -
    /// resolve แบบเดียวกับ ClientEndpoints GET /api/clients) null = ไม่มีใคร login อยู่ตอนนั้น (ใช้ชื่อ
    /// เครื่องแทนตามที่ roadmap ระบุ - ดู FileTransferEndpoints.ResolveCollectorFolderName)</summary>
    public string? StudentName { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>path สัมพัทธ์ใต้ {StoragePath}\collected\ (ใช้เปิด/หาไฟล์จริงบนดิสก์ - ไม่ใช่ path สัมบูรณ์
    /// กันกรณี StoragePath ถูกย้ายทีหลัง)</summary>
    public string StoredRelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
