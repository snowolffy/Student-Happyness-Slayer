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
}
