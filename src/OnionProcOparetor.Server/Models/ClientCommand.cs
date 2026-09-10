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

    /// <summary>
    /// หมดอายุเมื่อไหร่ - เลยเวลานี้แล้ว Server จะ **ไม่ส่ง command นี้ให้ Agent อีก** ไม่ว่ากรณีใด
    /// null = ไม่มีวันหมดอายุ (ใช้กับคำสั่งที่เป็น "สถานะ" ไม่ใช่ "เหตุการณ์" - ดู CommandExpiry)
    ///
    /// บั๊กจริงที่แก้ (2026-09-10): คำสั่งที่สร้างตอนเครื่องปิดอยู่ค้างเป็น Pending ตลอดกาล พอเครื่องนั้น
    /// ถูกเปิดวันหลัง มัน poll แล้วได้คำสั่งเดิมไปรันทันที เคสที่เจอจริงคือครูสั่ง Shutdown ทั้งห้อง
    /// 30 เครื่องที่เปิดอยู่ดับปกติ ส่วนอีก 10 เครื่องที่ปิดอยู่ พอเปิดมาก็ดับเองทันทีที่บูตเสร็จ
    ///
    /// รากของปัญหาคือความหมายของคำสั่ง: "ปิดเครื่องทั้งห้อง" หมายถึงเครื่องที่เปิดอยู่ *ตอนนี้*
    /// ไม่ใช่ "ปิดทันทีที่บูตครั้งหน้า" - คำสั่งประเภทเหตุการณ์จึงต้องมีอายุ
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// กติกาอายุของคำสั่ง - แยก "เหตุการณ์" (ทำเดี๋ยวนี้) ออกจาก "สถานะ" (ทำให้เครื่องเป็นแบบนี้)
///
/// **แก้ที่ Server ฝั่งเดียวโดยเจตนา** ไม่ได้แตะ Agent เลย: Agent 40 ตัวในห้องเป็นเวอร์ชันที่ deploy
/// ไปแล้ว การกรองตอน /api/poll ทำให้ทุกเครื่องได้รับการป้องกันทันทีที่ Server รีสตาร์ท โดยไม่ต้อง
/// ไล่อัปเดต MSI ทีละเครื่อง (ตรงกับข้อตกลงเดิมว่าแก้ฝั่ง Server/Console เท่านั้น)
/// </summary>
public static class CommandExpiry
{
    /// <summary>
    /// อายุของคำสั่งประเภท "เหตุการณ์" - 3 นาที
    ///
    /// เลือกจากรอบ poll จริงคือ 30 วิ: เครื่องที่เปิดอยู่จริงจะได้รับภายในรอบเดียว (หรือทันทีถ้า
    /// SignalR ต่ออยู่) 3 นาที = 6 รอบ ซึ่งเผื่อเน็ตกระตุกหรือ Tailscale หลุดชั่วครู่ได้สบาย แต่สั้น
    /// พอที่เครื่องซึ่งปิดอยู่จริงแล้วเปิดมาทีหลังจะไม่มีวันเจอคำสั่งเก่า
    /// </summary>
    public static readonly TimeSpan EventCommandLifetime = TimeSpan.FromMinutes(3);

    /// <summary>
    /// คำสั่งที่เป็น "สถานะ" ไม่ใช่ "เหตุการณ์" - ส่งถึงช้าก็ยังถูกต้อง (และควรถึงด้วย) จึงไม่หมดอายุ
    /// UpdateSettings/SetTailscaleLockdown คือการตั้งค่าให้เครื่องเป็นแบบนั้น เครื่องที่เพิ่งเปิดมา
    /// ต้องได้ค่านั้นด้วย ไม่ใช่ค้างอยู่กับค่าเก่า
    ///
    /// **ตั้งใจให้เป็น allow-list ไม่ใช่ deny-list**: CommandType ใหม่ที่ยังไม่ได้ขึ้นทะเบียนที่นี่จะ
    /// ถูกถือว่าเป็น "เหตุการณ์" และหมดอายุ - เดาผิดทางนี้แค่ทำให้คำสั่งไม่ถึงเครื่องที่ปิดอยู่
    /// เดาผิดอีกทางคือเครื่องดับเองตอนบูต
    /// </summary>
    private static readonly HashSet<string> DurableCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "UpdateSettings",
        "SetTailscaleLockdown",
        "RefreshRules"
    };

    /// <summary>
    /// คำนวณ ExpiresAt จากชนิดคำสั่ง - null = ไม่หมดอายุ
    ///
    /// <paramref name="eventLifetime"/> ตั้งได้จาก appsettings (CommandTracking:EventCommandLifetimeSeconds)
    /// เผื่อห้องที่ตั้งรอบ poll ไว้ต่างจาก 30 วิ และเพื่อให้เขียนเทสต์ที่ไม่ต้องนั่งรอ 3 นาทีได้
    /// </summary>
    public static DateTime? ComputeExpiry(string commandType, DateTime createdAtUtc, TimeSpan? eventLifetime = null) =>
        DurableCommandTypes.Contains(commandType)
            ? null
            : createdAtUtc + (eventLifetime ?? EventCommandLifetime);
}

/// <summary>
/// สถานะ "ส่งมอบ" (delivery) ของเดิม - ห้ามเปลี่ยนลำดับ/ค่าตัวเลขของสมาชิกที่มีอยู่แล้ว
/// เพราะ EF Core เก็บเป็น int ใน SQLite ค่าที่มีอยู่แล้วใน DB จะเพี้ยนทันที
/// </summary>
public enum ClientCommandStatus
{
    Pending,
    Delivered,
    Failed,

    /// <summary>
    /// เลยอายุแล้วโดยที่ Agent ยังไม่เคยรับไป - Server จะไม่ส่งให้อีก (ต่อท้ายเท่านั้น ห้ามแทรกกลาง
    /// เพราะ EF เก็บเป็น int ค่าที่มีอยู่ใน DB จะเพี้ยนทันที)
    /// </summary>
    Expired
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
    TimedOut,

    /// <summary>
    /// หมดอายุก่อนที่จะไปถึงเครื่อง - **ไม่เคยรัน** ต่างจาก TimedOut ที่ถึงเครื่องแล้วแต่ไม่รู้ผล
    /// เกือบทั้งหมดคือเครื่องปิดอยู่ตอนที่ครูสั่ง (ต่อท้ายเท่านั้น ด้วยเหตุผลเดียวกับข้างบน)
    /// </summary>
    Expired
}
