namespace OnionProcOparetor.Console.Models;

/// <summary>
/// สถานะ command ที่ได้จาก Server - ทั้งทาง SignalR "CommandResultChanged" และทาง
/// GET /api/commands/batch/{batchId} ใช้รูปแบบเดียวกันนี้ (ตรงกับ CommandResultDto ฝั่ง Server)
///
/// ExecutionStatus มาเป็น string ไม่ใช่ตัวเลข enum โดยตั้งใจ - solution นี้ไม่มี shared library
/// ถ้าก๊อป enum มาไว้ 2 ฝั่งแล้ววันหนึ่งมีคนแทรกสมาชิกใหม่ตรงกลางฝั่งเดียว ค่าจะเพี้ยนแบบเงียบๆ
/// หาสาเหตุยากมาก ใช้ string แล้วเทียบด้วยชื่อปลอดภัยกว่าเยอะ
/// </summary>
public class CommandResultDto
{
    public int CommandId { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string? BatchId { get; set; }

    /// <summary>"Pending" | "Succeeded" | "Failed" | "TimedOut"</summary>
    public string ExecutionStatus { get; set; } = "Pending";

    public string? ExecutionMessage { get; set; }
    public DateTime? ExecutedAt { get; set; }

    /// <summary>"Pending" | "Delivered" | "Failed" - สถานะ "ส่งมอบ" เดิม แยกจาก ExecutionStatus</summary>
    public string DeliveryStatus { get; set; } = "Pending";
}
