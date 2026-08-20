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

    /// <summary>true = เปิดใช้งาน enforcement บนเครื่องนี้, false = ปิด (per-machine toggle)</summary>
    public bool IsEnabled { get; set; } = true;

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
    /// true = เครื่องนี้ยัง online อยู่ - เชื่อ SignalR connection จริงก่อน (เร็วและแม่นยำที่สุด) ถ้าไม่ได้
    /// connect อยู่ (หรือ IsConnectedViaSignalR ไม่ได้ถูก set มา) ค่อย fallback ไปดู LastSeenAt แทน (รองรับ
    /// เครื่องที่ poll สำเร็จปกติแต่ SignalR ต่อไม่ติด - ต้องยังทำงานได้ตามหลักการเดิมของทั้งระบบ: poll ต้อง
    /// ทำงานได้ครบแม้ hub จะล่ม) ไม่ผูกกับ IsEnabled อีกต่อไป - enforcement กับ online เป็นคนละเรื่องกัน
    /// (ดู ClientMachineDto ฝั่ง Console สำหรับที่ไปที่มาเต็มๆ)
    /// </summary>
    [NotMapped]
    public bool IsOnline =>
        IsConnectedViaSignalR || (DateTime.UtcNow - LastSeenAt).TotalSeconds <= OnlineThresholdSeconds;
}