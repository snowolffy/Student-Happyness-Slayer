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
}