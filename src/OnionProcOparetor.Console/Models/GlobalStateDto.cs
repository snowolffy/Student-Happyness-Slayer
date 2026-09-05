namespace OnionProcOparetor.Console.Models;

/// <summary>
/// สถานะ enforcement ระดับทั้งห้อง (GET /api/global, POST /api/global/set)
///
/// เป็น AND gate คร่อม ClientMachineDto.IsEnabled ของ "ทุกเครื่อง" อีกที: Agent จะบังคับใช้กฎก็
/// ต่อเมื่อค่านี้และ IsEnabled ของเครื่องนั้นเปิดพร้อมกัน (ดู /api/poll ฝั่ง Server) - จึงต้องแสดง
/// ให้ครูเห็นตลอดเวลาที่มันปิดอยู่ ไม่งั้นเครื่องที่ขึ้น ENABLED ในตารางอาจไม่ได้บังคับใช้อะไรเลย
/// </summary>
public class GlobalStateDto
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; }
}
