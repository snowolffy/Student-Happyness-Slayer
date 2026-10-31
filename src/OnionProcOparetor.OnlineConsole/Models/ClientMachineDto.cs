namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>Ported from OnionProcOparetor.Console/Models/ClientMachineDto.cs (identical - GET /api/clients)</summary>
public class ClientMachineDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? LastKnownIp { get; set; }

    /// <summary>true = เครื่องนี้กำลังต่อผ่าน Tailscale fallback อยู่ตอนนี้ ไม่ใช่ LAN ปกติ</summary>
    public bool UsingFallback { get; set; }

    public bool IsEnabled { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>คำนวณฝั่ง Server (ClientMachine.IsOnline) ส่งมาตรงๆ ทั้งจาก GET /api/clients และ SignalR ClientStatusChanged</summary>
    public bool IsOnline { get; set; }

    /// <summary>ห้องที่เครื่องนี้อยู่ (WP3, 2026-10-30) - null = "ยังไม่จัดห้อง"</summary>
    public int? RoomId { get; set; }

    public int? SeatRow { get; set; }
    public int? SeatCol { get; set; }

    /// <summary>นักเรียนที่ login อยู่บนเครื่องนี้ตอนนี้ (WP3-5, 2026-10-31) - มาจาก GET /api/clients ตรงๆ
    /// (Server เติมมาเฉพาะตอนมี StudentSession ที่ยังเปิดอยู่ของเครื่องนี้ - ดู ClientEndpoints.cs)</summary>
    public int? CurrentStudentId { get; set; }
    public string? CurrentStudentCode { get; set; }
    public string? CurrentStudentName { get; set; }

    public bool HasActiveStudent => CurrentStudentId is not null;
    public string StudentDisplayText => HasActiveStudent ? $"{CurrentStudentName} · {CurrentStudentCode}" : "—";
}
