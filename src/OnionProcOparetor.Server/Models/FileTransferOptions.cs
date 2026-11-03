namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ตั้งค่าแจก/เก็บไฟล์ (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน) - ผูกกับ appsettings.json section
/// "FileTransfer" (ดู Program.cs builder.Services.Configure) ไม่เก็บเป็นตาราง DB แบบ BackupSettings
/// เพราะไม่มีความจำเป็นต้องแก้ผ่าน UI ขณะ Server รันอยู่ (ค่าที่ควรเปลี่ยนไม่บ่อย ตั้งครั้งเดียวตอน deploy)
/// - ตรงกับ docs/roadmap.md "จำกัดขนาดต่อไฟล์ ตั้งค่าได้" (แก้ไฟล์ config แล้ว restart Server เพื่อมีผล
/// เหมือน Security:AllowHttp/CommandTracking:EventCommandLifetimeSeconds ที่มีอยู่แล้ว)
///
/// GET /api/files/settings ให้ Console อ่านค่าปัจจุบันไปโชว์ "สูงสุด N MB" บนหน้า tc-files เท่านั้น -
/// การบังคับจริงอยู่ที่ฝั่ง Server เสมอ (ดู FileTransferEndpoints ตรวจ ContentLength/จำนวน bytes จริง
/// ก่อนเขียนลงดิสก์ - ไม่เชื่อค่าที่ฝั่ง client ส่งมาเด็ดขาด)
/// </summary>
public class FileTransferOptions
{
    /// <summary>ขนาดสูงสุดต่อไฟล์ (MB) ทั้งแจกและเก็บ - ตรงกับ wireframe tc-files ("สูงสุด 100 MB ต่อครั้ง")</summary>
    public int MaxFileSizeMb { get; set; } = 100;

    /// <summary>ที่เก็บไฟล์บนดิสก์ของ Server - null/ว่าง = ใช้ default (%ProgramData%\OnionProcOparetor\files)
    /// ตั้งไป temp ได้ตอนทดสอบ (ดู CLAUDE.md "ตั้ง storage path ไป temp") pattern เดียวกับ BackupSettings.StoragePath</summary>
    public string? StoragePath { get; set; }
}
