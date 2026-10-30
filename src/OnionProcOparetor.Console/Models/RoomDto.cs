namespace OnionProcOparetor.Console.Models;

/// <summary>ห้องแล็บคอมพิวเตอร์ (WP3, 2026-10-30) - ได้จาก GET /api/rooms
/// Port มาจาก OnionProcOparetor.OnlineConsole/Models/RoomDto.cs (เหมือนกันเป๊ะ - สองฝั่งอ่านจาก
/// endpoint เดียวกัน) ใช้เติมตัวเลือกห้องบนหัวแท็บ Clients ของ Console (ดู DashboardWindow)</summary>
public class RoomDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>จำนวนเครื่องที่อยู่ในห้องนี้ตอนนี้ - Server คำนวณมาให้แล้ว (ดู RoomEndpoints.cs)</summary>
    public int ClientCount { get; set; }
}
