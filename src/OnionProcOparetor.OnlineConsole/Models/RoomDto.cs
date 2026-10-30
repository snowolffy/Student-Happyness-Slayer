namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from Server/Models/Room.cs (WP3, 2026-10-30) - GET/POST /api/rooms</summary>
public class RoomDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>จำนวนเครื่องที่อยู่ในห้องนี้ตอนนี้ - Server คำนวณมาให้แล้ว (ดู RoomEndpoints.cs)</summary>
    public int ClientCount { get; set; }
}
