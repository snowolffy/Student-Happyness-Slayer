namespace OnionProcOparetor.RosterManager.Models;

/// <summary>
/// Settings ฝั่ง Roster Manager เท่านั้น - persist ผ่าน Services/SettingsService ที่
/// %ProgramData%\OnionProcOparetor\RosterManager\roster-settings.json (pattern เดียวกับ
/// Console/Models/ConsoleSettings.cs แต่เล็กกว่ามาก เพราะโปรแกรมนี้ทำแค่เรื่อง roster เรื่องเดียว)
/// </summary>
public class RosterSettings
{
    public RosterConnectionSettings Connection { get; set; } = new();
    public string? Theme { get; set; }
}

public class RosterConnectionSettings
{
    public string ServerHost { get; set; } = "localhost";
    public string ServerPort { get; set; } = "8787";
    public bool UseHttps { get; set; } = false;
    public string Username { get; set; } = string.Empty;
}
