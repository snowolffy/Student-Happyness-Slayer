namespace OnionProcOparetor.OnlineConsole.Models;

/// <summary>
/// Ported from OnionProcOparetor.Console/Models/RunningProcessDto.cs (identical shape - matches the
/// AgentTray-side copy too; there's no shared contracts library across any of these three projects).
/// </summary>
public class RunningProcessDto
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? PublisherName { get; set; }
    public bool IsSignedMatch { get; set; }
    public bool HasPublisher { get; set; }

    /// <summary>SHA-256 ของไฟล์ (hex ตัวเล็ก) - ว่างได้ถ้าอ่านไฟล์ไม่ได้/ไฟล์ใหญ่เกิน</summary>
    public string? Sha256 { get; set; }

    public bool CanBlockByHash => !string.IsNullOrWhiteSpace(Sha256);
}
