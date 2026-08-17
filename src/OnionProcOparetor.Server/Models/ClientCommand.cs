namespace OnionProcOparetor.Server.Models;

/// <summary>
/// คำสั่งที่ Console ส่งไปยัง Agent เครื่องใดเครื่องหนึ่ง (เช่นสั่งจาก real-time push ผ่าน SignalR)
/// บันทึกลง DB เสมอแม้จะ push ผ่าน SignalR ได้ทันที เพื่อเป็น fallback ผ่าน HTTP poll
/// ถ้า Agent เครื่องนั้นไม่ได้ connect SignalR อยู่ตอนที่สั่ง (offline/disconnect)
///
/// มีสถานะ 2 ชุด "แยกกันชัดเจน" อย่าสับสน:
/// - Status/DeliveredAt = สถานะ "ส่งมอบ" (delivery) - Agent ได้รับ command นี้แล้วหรือยัง
///   ตัวนี้เท่านั้นที่คุมว่า /api/poll จะส่ง command นี้ซ้ำอีกไหม (Pending = ส่งซ้ำ)
/// - ExecutionStatus/ExecutionMessage/ExecutedAt = สถานะ "ผลลัพธ์จริง" (execution) - action
///   ที่สั่งไปทำสำเร็จจริงไหม ไม่เกี่ยวกับ poll เลย ใช้ให้ Console รายงานผลให้ครูดู
///
/// เหตุผลที่ต้องแยก: ack เดิมยิงกลับมาเสมอหลัง process ไม่ว่าจะสำเร็จหรือไม่ (แม้แต่ตอนที่
/// command ถูกข้ามเพราะซ้ำ) มันพิสูจน์แค่ว่า "ถึงเครื่องแล้ว" ไม่ได้พิสูจน์ว่า "ทำได้จริง"
/// ถ้าเอา Status ตัวเดิมมาทับด้วยผลลัพธ์ จะทำให้ความหมายของ poll filter เพี้ยนไปด้วย
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

    /// <summary>สถานะ "ส่งมอบ" เท่านั้น - /api/poll ส่ง command ซ้ำตราบใดที่ยังเป็น Pending</summary>
    public ClientCommandStatus Status { get; set; } = ClientCommandStatus.Pending;

    /// <summary>
    /// รหัสชุดคำสั่ง - ครูกด action ครั้งเดียวใน ActionPanelWindow แล้วยิงไปหลายเครื่องพร้อมกัน
    /// ทุกเครื่องในการกดครั้งนั้นได้ BatchId เดียวกัน เพื่อให้ Console ดึงสถานะทั้งชุดทีเดียวได้
    /// (ดู GET /api/commands/batch/{batchId}) null ได้ถ้าคำสั่งไม่ได้มาจาก batch (เช่นยิง API ตรง)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>สถานะ "ผลลัพธ์จริง" ของ action - แยกจาก Status (delivery) โดยสิ้นเชิง</summary>
    public CommandExecutionStatus ExecutionStatus { get; set; } = CommandExecutionStatus.Pending;

    /// <summary>รายละเอียดตอน Failed (ข้อความ error จาก Agent) หรือหมายเหตุเพิ่มเติมตอน Succeeded</summary>
    public string? ExecutionMessage { get; set; }

    /// <summary>เวลาที่ Agent รายงานผลจริงกลับมา (null = ยังไม่รายงาน)</summary>
    public DateTime? ExecutedAt { get; set; }
}

/// <summary>
/// สถานะ "ส่งมอบ" (delivery) ของเดิม - ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว
/// เพราะ EF Core เก็บเป็น int ใน SQLite ค่าที่มีอยู่แล้วใน DB จะเพี้ยนทันที
/// </summary>
public enum ClientCommandStatus
{
    Pending,
    Delivered,
    Failed
}

/// <summary>
/// สถานะ "ผลลัพธ์จริง" (execution) ของ action ที่สั่งไป - ใหม่ทั้งหมด แยกจาก ClientCommandStatus
///
/// Timeout แยกจาก Failed โดยตั้งใจ: ความหมายต่างกันมากสำหรับครูที่ยืนอยู่หน้าห้อง
/// - Failed  = เครื่องรับคำสั่งแล้ว ลองทำแล้ว แต่ทำไม่ได้ (มี error message บอกสาเหตุ)
/// - Timeout = ไม่รู้เลยว่าเกิดอะไรขึ้น (เครื่องปิดอยู่? เน็ตหลุด? Agent ตาย?) ต้องไปดูที่เครื่อง
/// </summary>
public enum CommandExecutionStatus
{
    /// <summary>ยังไม่มีผลกลับมา - ยังอยู่ในเวลารอ</summary>
    Pending,

    /// <summary>Agent ยืนยันว่าทำ action สำเร็จจริง</summary>
    Succeeded,

    /// <summary>Agent ยืนยันว่าทำ action ไม่สำเร็จ - ดู ExecutionMessage ประกอบ</summary>
    Failed,

    /// <summary>ไม่มีผลกลับมาภายในเวลาที่กำหนด - ไม่รู้ผลจริง (ต่างจาก Failed ที่รู้ว่าล้มเหลว)</summary>
    TimedOut
}
