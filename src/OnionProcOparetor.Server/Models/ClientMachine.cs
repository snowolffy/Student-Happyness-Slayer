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
    /// เกิน threshold นี้ (วินาที) โดยที่ IsEnabled=true ถือว่า "หายไปผิดปกติ" - มากกว่า threshold
    /// "offline ปกติ" ของ Console (90 วิ = 3 เท่าของ PollIntervalSeconds ปกติ) มาก เพื่อกัน false
    /// alarm จากเครื่อง sleep สั้นๆ หรือ network กระตุกชั่วคราว ก่อนจะถือว่าน่าสงสัยจริงๆ
    /// </summary>
    public const int MissingUnexpectedlyThresholdSeconds = 300; // 5 นาที

    /// <summary>
    /// true = เครื่องนี้ยังถูก enforce อยู่ (IsEnabled) แต่ Agent หายไปนานเกิน threshold ข้างบน -
    /// ต่างจาก "Offline" ธรรมดา (ปิดเครื่องปกติตอนเลิกเรียน) เพราะเครื่องกลุ่มนี้ควรจะเปิด Agent
    /// อยู่ตลอด การหายไปกลางคันจึงน่าสงสัยกว่า (เช่น นักเรียนหลบ uninstall password gate ด้วยการ
    /// kill service ตรงผ่าน Task Manager/services.msc) ไม่ map ลง DB - คำนวณสดทุกครั้งที่ serialize
    /// </summary>
    [NotMapped]
    public bool IsMissingUnexpectedly =>
        IsEnabled && (DateTime.UtcNow - LastSeenAt).TotalSeconds > MissingUnexpectedlyThresholdSeconds;
}