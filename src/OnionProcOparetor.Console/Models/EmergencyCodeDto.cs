namespace OnionProcOparetor.Console.Models;

/// <summary>ผลลัพธ์จาก GET /api/settings/emergency-code (WP3-4, 2026-10-31) - mirror ของ response ฝั่ง Server (EmergencyCodeEndpoints.cs)</summary>
public class EmergencyCodeSettingsDto
{
    public string SecretBase32 { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RotatedAt { get; set; }
    public string? RotatedByUsername { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsageAt { get; set; }
    public string? LastUsageMachineName { get; set; }
}

/// <summary>แถวหนึ่งของ GET /api/settings/emergency-code/usage</summary>
public class EmergencyCodeUsageDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTime UsedAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}
