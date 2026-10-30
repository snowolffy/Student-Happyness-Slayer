namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ห้องแล็บคอมพิวเตอร์หนึ่งห้อง (WP3, 2026-10-30 - migration ลำดับที่ 1 ของขั้น 1)
///
/// เป็นแค่ label สำหรับจัดกลุ่มเครื่อง + ผังที่นั่งในอนาคต (ดู ClientMachine.RoomId/SeatRow/SeatCol)
/// ไม่มี concept "ตาราง/คาบเรียน" ผูกกับห้อง - ตัดสินใจไว้แล้วว่าห้องอยู่โหมดใช้งานทั่วไปเสมอ (ดู
/// docs/roadmap.md หัวข้อ "ไม่มีตาราง/คาบเรียน")
/// </summary>
public class Room
{
    public int Id { get; set; }

    /// <summary>ชื่อห้อง เช่น "ห้องคอม 1" - ต้องไม่ซ้ำกัน (ดู AppDbContext.OnModelCreating)</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
