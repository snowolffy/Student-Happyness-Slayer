namespace OnionProcOparetor.Console.Models;

/// <summary>ข้อมูลเครื่อง client ที่ได้จาก GET /api/clients</summary>
public class ClientMachineDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? LastKnownIp { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; }

    /// <summary>ถือว่า offline ถ้าไม่ poll เข้ามาเกิน 90 วิ (3 เท่าของ PollIntervalSeconds ปกติ 30 วิ)</summary>
    public bool IsOffline => (DateTime.UtcNow - LastSeenAt).TotalSeconds > 90;
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>
    /// คำนวณฝั่ง Server (ClientMachine.IsMissingUnexpectedly) ส่งมาเป็น field นี้ตรงๆ ทั้งจาก
    /// GET /api/clients และ SignalR ClientStatusChanged - true = เครื่องนี้ IsEnabled อยู่แต่ Agent
    /// หายไปนานผิดปกติ (เกิน 5 นาที) น่าสงสัยกว่า Offline ธรรมดา (ปิดเครื่องตอนเลิกเรียน)
    /// </summary>
    public bool IsMissingUnexpectedly { get; set; }
}