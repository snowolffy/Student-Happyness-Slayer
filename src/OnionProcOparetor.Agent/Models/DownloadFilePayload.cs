namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "DownloadFile" (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน "แจกไฟล์") - ส่งมาแค่
/// DistributedFileId เท่านั้น (เหมือน LaunchProgramPayload.ProgramId) ชื่อไฟล์จริงได้จาก Content-Disposition
/// ของ GET /api/files/distributed/{id}/content ตอนดาวน์โหลด ไม่เชื่อชื่อไฟล์ที่อาจติดมากับ payload เก่า
/// </summary>
public class DownloadFilePayload
{
    public int DistributedFileId { get; set; }

    /// <summary>true = เปิดไฟล์ให้อัตโนมัติหลังดาวน์โหลดเสร็จ (ผ่าน explorer.exe เหมือน OpenUrl - ใช้
    /// default app ของ user คนนั้น) ค่านี้มาจาก Console ตรงๆ (ไม่ใช่ข้อมูลที่ต้อง re-validate กับ Server
    /// เพราะไม่กระทบความปลอดภัย - อย่างมากก็เปิดไฟล์ที่ตัวเองเพิ่งดาวน์โหลดมาเอง)</summary>
    public bool OpenAfterDownload { get; set; }
}
