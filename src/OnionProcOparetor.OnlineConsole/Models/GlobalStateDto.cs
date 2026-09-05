namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>
/// สถานะ enforcement ระดับทั้งห้อง (GET /api/global, POST /api/global/set)
///
/// ค่านี้เป็น AND gate คร่อม IsEnabled รายเครื่องอีกที: Agent จะบังคับใช้กฎก็ต่อเมื่อ
/// IsEnabled ของ "ทั้งห้อง" และ "ของเครื่องนั้น" เปิดพร้อมกัน (ดู /api/poll ฝั่ง Server)
/// จึงต้องแสดงให้ครูเห็นเสมอ ไม่งั้นเครื่องที่ขึ้น ENABLED ในตารางอาจไม่ได้บังคับใช้อะไรอยู่เลย
/// </summary>
public class GlobalStateDto
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; }
}
