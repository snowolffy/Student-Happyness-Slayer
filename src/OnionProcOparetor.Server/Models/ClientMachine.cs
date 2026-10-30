using System.ComponentModel.DataAnnotations.Schema;

namespace OnionProcOparetor.Server.Models;

/// <summary>
/// ข้อมูลเครื่อง client แต่ละเครื่อง (ห้องแล็บ x40)
/// ระบุตัวตนด้วย unique GUID ที่ generate ตอนติดตั้ง เก็บใน registry ฝั่ง client
/// </summary>
public class ClientMachine
{
    public int Id { get; set; }

    /// <summary>GUID ที่ client generate เองตอน install (ไม่ซ้ำกันต่อเครื่อง)</summary>
    public string ClientGuid { get; set; } = string.Empty;

    /// <summary>ชื่อเครื่อง (hostname) ไว้แสดงผลใน Console GUI ให้อ่านง่าย</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>IP ล่าสุดที่เครื่องนี้ poll เข้ามา (เผื่อ debug)</summary>
    public string? LastKnownIp { get; set; }

    /// <summary>
    /// true = เครื่องนี้กำลัง poll ผ่าน FallbackBaseUrl (Tailscale) อยู่ตอนนี้ ไม่ใช่ primary LAN address
    /// ปกติ - Agent ส่งค่านี้มาเองทุกครั้งที่ poll (ดู ServerClient.PollAsync, /api/poll/{clientGuid}
    /// ด้านล่าง) เป็น real column ที่ persist ลง DB ตรงๆ (ไม่ใช้ pattern [NotMapped] แบบ
    /// IsConnectedViaSignalR ด้านล่าง) เพราะ semantic เป็นแบบ "ค่าล่าสุดที่รู้" (last-known) เหมือน
    /// LastKnownIp/LastSeenAt ด้านบน ไม่ใช่ live state ที่เปลี่ยนได้โดยไม่มี poll มาบอกแบบ SignalR -
    /// ความคลาดเคลื่อนระหว่าง poll แต่ละรอบยอมรับได้เท่ากับ field อื่นๆ ในกลุ่มเดียวกัน
    /// </summary>
    public bool UsingFallback { get; set; }

    /// <summary>true = เปิดใช้งาน enforcement บนเครื่องนี้, false = ปิด (per-machine toggle)</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// ห้องที่เครื่องนี้อยู่ (WP3, 2026-10-30) - null = "ยังไม่จัดห้อง" (ค่าเริ่มต้นของเครื่องเก่าทุกเครื่อง
    /// หลัง migration นี้ และของเครื่องใหม่ที่ยังไม่ถูกย้ายเข้าห้อง) ไม่ใส่ FK/navigation property ไปที่
    /// Models/Room.cs ตั้งใจ - เดินตาม pattern เดียวกับ WebsiteRule.SourceCatalogId (เป็น int? เปล่าๆ,
    /// endpoint เป็นคนดูแล referential integrity เอง เช่นตอนลบห้องต้องเซ็ตเครื่องในห้องนั้นกลับเป็น null ก่อน
    /// ดู Endpoints/RoomEndpoints.cs) เพื่อให้ migration เป็น additive ล้วนๆ
    /// </summary>
    public int? RoomId { get; set; }

    /// <summary>แถวที่นั่งในผังห้อง (WP3) - null = ยังไม่ระบุตำแหน่ง มีผลเฉพาะตอน RoomId ไม่เป็น null เท่านั้น</summary>
    public int? SeatRow { get; set; }

    /// <summary>คอลัมน์ที่นั่งในผังห้อง (WP3) - null = ยังไม่ระบุตำแหน่ง มีผลเฉพาะตอน RoomId ไม่เป็น null เท่านั้น</summary>
    public int? SeatCol { get; set; }

    /// <summary>
    /// PBKDF2 hash ของ agent secret ประจำเครื่องนี้ (2026-08-31, จาก security audit)
    ///
    /// null = ยังไม่เคย enroll (เครื่องที่ติดตั้งด้วย build ก่อนหน้า หรือเครื่องใหม่ที่ยังไม่ poll)
    /// ค่าแรกที่ส่งมาถือว่าเป็นของจริง (trust-on-first-use) แล้วล็อกไว้ตลอด - เลือกแบบนี้เพราะ
    /// ทางเลือกอื่น (pre-shared key ตอนติดตั้ง) แปลว่าต้องเดินไปทีละเครื่องทั้ง 40 เครื่อง และ key
    /// จะไปนั่งอยู่ใน MSI/สคริปต์ที่นักเรียนอ่านได้อยู่ดี
    ///
    /// ช่องที่เหลืออยู่ของ TOFU (บอกตรงๆ ไม่ได้ปิดหมด): ระหว่างที่เครื่องหนึ่งยังไม่เคย enroll
    /// ใครก็ตามที่รู้ GUID ของเครื่องนั้นชิง enroll ก่อนได้ หน้าต่างนี้กว้างแค่ช่วงหลัง rollout build
    /// นี้จนถึง poll แรกของแต่ละเครื่อง (ไม่กี่สิบวินาที) และครู re-enroll ทับได้จาก Console
    /// เก็บเป็น hash ไม่ใช่ค่าดิบ ด้วยเหตุผลเดียวกับรหัสผ่าน admin: DB ที่หลุดไม่ควรแปลว่า
    /// ปลอมเป็นทุกเครื่องในห้องได้ทันที
    /// </summary>
    public string? AgentSecretHash { get; set; }

    /// <summary>salt ของ AgentSecretHash - null คู่กับ AgentSecretHash เสมอ</summary>
    public string? AgentSecretSalt { get; set; }

    /// <summary>เวลาที่ enroll agent secret ครั้งล่าสุด (ไว้ให้ครูดูว่าเครื่องไหนเพิ่งถูก enroll ใหม่ผิดปกติ)</summary>
    public DateTime? AgentSecretEnrolledAt { get; set; }

    /// <summary>เวลาที่ client เครื่องนี้ poll เข้ามาล่าสุด ใช้เช็คว่ายัง online อยู่ไหม</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// [OPTIONAL] Override ค่า PollIntervalSeconds ของเครื่องนี้จากศูนย์กลาง
    /// null = ใช้ค่า default ใน appsettings.json ของ Agent เอง (ปกติ 30 วิ)
    /// ตั้งค่านี้ไว้เพื่อใช้กับฟีเจอร์ "Remote client setting" และ "Immediately pull" ในอนาคต
    /// </summary>
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>
    /// เกิน threshold นี้ (วินาที) โดยไม่มี SignalR connection ค้างอยู่เลย ถือว่า offline - เดิมเป็นค่าที่
    /// Console คำนวณเองฝั่ง client (90 วิ = 3 เท่าของ PollIntervalSeconds ปกติ) ย้ายมารวมไว้ที่นี่แหล่งเดียว
    /// (2026-08-20: เดิมแยกกัน 2 concept คือ "Missing" ฝั่ง Server เกณฑ์ 5 นาที + IsEnabled กับ "Offline"
    /// ฝั่ง Console เกณฑ์ 90 วิ คำนวณคนละที่ - ยุบเหลือ IsOnline ตัวเดียว คำนวณที่ Server แหล่งเดียว
    /// Console แค่อ่านค่านี้ตรงๆ ไม่ต้องคำนวณเองอีก กันปัญหา clock skew ระหว่าง 2 เครื่องด้วย)
    /// </summary>
    public const int OnlineThresholdSeconds = 90;

    /// <summary>
    /// ตั้งจากภายนอกก่อน serialize ทุกครั้ง (ไม่ persist ลง DB) - มาจาก AgentConnectionRegistry.IsConnected
    /// (ดูจุดที่ set: GET /api/clients, POST /api/clients/{id}/toggle, PUT /api/clients/{id}/settings,
    /// GET /api/poll/{clientGuid} ก่อน broadcast, ClientOnlineStatusMonitorService) ต้อง set ทุกจุดที่คืน/
    /// broadcast ClientMachine ออกไป ไม่งั้นจะ default เป็น false เสมอ (ตกไปพึ่ง LastSeenAt อย่างเดียว -
    /// ไม่ผิดร้ายแรง แค่ไม่ได้สัญญาณเร็วจาก SignalR)
    /// </summary>
    [NotMapped]
    public bool IsConnectedViaSignalR { get; set; }

    /// <summary>
    /// ตั้งจากภายนอกคู่กับ IsConnectedViaSignalR เสมอ - มาจาก AgentConnectionRegistry.HasEverConnected
    /// ดู doc comment ของ IsOnline ข้างล่างว่าทำไมต้องแยกจาก IsConnectedViaSignalR
    /// </summary>
    [NotMapped]
    public bool HasEverConnectedViaSignalR { get; set; }

    /// <summary>
    /// true = เครื่องนี้ยัง online อยู่
    ///
    /// บั๊กจริงที่เจอ (2026-08-20): เดิมใช้ `IsConnectedViaSignalR || LastSeenAt ยังไม่เกิน 90 วิ` เสมอ -
    /// ตอน SignalR หลุดจริง (เช่น net stop service แบบ graceful) LastSeenAt ที่เพิ่ง poll ไปก่อนหน้ายังคง
    /// "สด" อยู่ภายใน 90 วิ ทำให้ fallback ฝั่งขวายัง TRUE กลบสัญญาณ disconnect ที่เพิ่งเกิดทันที - ผลคือ
    /// broadcast ตอน disconnect ยังบอกว่า "online" เหมือนเดิม กว่าจะขึ้น OFFLINE จริงต้องรอ LastSeenAt
    /// หมดอายุพอดี = ช้าเท่าเดิมกับระบบเก่าทุกประการ ทั้งที่ตั้งใจให้เร็วกว่านั้นมาก
    ///
    /// แก้โดยแยก 2 กรณี: เครื่องที่ "เคย" connect SignalR สำเร็จมาก่อน (HasEverConnectedViaSignalR) ให้เชื่อ
    /// IsConnectedViaSignalR เป็นเกณฑ์เดียวเด็ดขาด ไม่ fallback ไป LastSeenAt อีก (ยอมรับได้ที่เครือข่าย
    /// กระตุกสั้นๆ อาจเห็น OFFLINE วูบแป๊บนึงก่อน SignalR reconnect เอง - ยังดีกว่าค้าง ONLINE ผิดๆ นานถึง
    /// 90 วิ) ส่วนเครื่องที่ไม่เคย connect SignalR เลย (เช่น firewall บล็อก WebSocket ถาวร) ยัง fallback ไป
    /// LastSeenAt ตามปกติเหมือนเดิม (ตรงกับหลักการเดิมของทั้งระบบ: poll ต้องทำงานได้ครบแม้ hub จะล่ม/ต่อไม่ติด)
    /// ไม่ผูกกับ IsEnabled อีกต่อไป - enforcement กับ online เป็นคนละเรื่องกัน (ดู ClientMachineDto ฝั่ง
    /// Console สำหรับที่ไปที่มาเต็มๆ)
    /// </summary>
    [NotMapped]
    public bool IsOnline =>
        HasEverConnectedViaSignalR
            ? IsConnectedViaSignalR
            : (DateTime.UtcNow - LastSeenAt).TotalSeconds <= OnlineThresholdSeconds;
}