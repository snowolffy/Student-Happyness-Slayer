namespace MyLabGuard.Server.Models;

/// <summary>
/// สถานะ toggle ระดับ global (ทั้งห้อง) — เก็บเป็น single row เดียวใน DB
/// ใช้คู่กับ ClientMachine.IsEnabled ที่เป็น per-machine toggle
/// Client ต้องเช็คทั้งสองอย่าง: ถ้า GlobalState ปิด หรือ ClientMachine ของตัวเองปิด
/// ก็ถือว่า enforcement ปิดอยู่
/// </summary>
public class GlobalState
{
    public int Id { get; set; }

    /// <summary>true = enforcement เปิดทั้งห้อง (ค่าเริ่มต้น), false = ปิดทั้งห้อง เช่นช่วงพักเที่ยง</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>หมายเหตุ เช่น "ปิดชั่วคราวสำหรับคาบครู"</summary>
    public string? Note { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}