namespace OnionProcOparetor.Server.Models;

/// <summary>
/// คำสั่งที่ Console ส่งไปยัง Agent เครื่องใดเครื่องหนึ่ง (เช่นสั่งจาก real-time push ผ่าน SignalR)
/// บันทึกลง DB เสมอแม้จะ push ผ่าน SignalR ได้ทันที เพื่อเป็น fallback ผ่าน HTTP poll
/// ถ้า Agent เครื่องนั้นไม่ได้ connect SignalR อยู่ตอนที่สั่ง (offline/disconnect)
/// </summary>
public class ClientCommand
{
    public int Id { get; set; }

    /// <summary>เครื่อง client เป้าหมายที่จะรับคำสั่งนี้ (ผูกกับ ClientMachine.ClientGuid)</summary>
    public string ClientGuid { get; set; } = string.Empty;

    /// <summary>ประเภทคำสั่ง เช่น "RefreshRules", "Kill", "RunCommand" - Agent เป็นคนตีความเอง</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>ข้อมูลประกอบคำสั่ง เก็บเป็น JSON string ดิบ (schema แล้วแต่ CommandType)</summary>
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>เวลาที่ Agent ยืนยันว่าได้รับคำสั่งนี้แล้ว (null = ยังไม่ได้รับ)</summary>
    public DateTime? DeliveredAt { get; set; }

    public ClientCommandStatus Status { get; set; } = ClientCommandStatus.Pending;
}

public enum ClientCommandStatus
{
    Pending,
    Delivered,
    Failed
}
