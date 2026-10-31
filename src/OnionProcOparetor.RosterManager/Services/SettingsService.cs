using System.IO;
using System.Text.Json;
using OnionProcOparetor.RosterManager.Models;

namespace OnionProcOparetor.RosterManager.Services;

/// <summary>
/// โหลด/บันทึก config ของ Roster Manager เอง (pattern เดียวกับ Console/Services/SettingsService.cs
/// แต่เล็กกว่ามาก - มีแค่เรื่อง connection + theme) เก็บที่
/// %ProgramData%\OnionProcOparetor\RosterManager\roster-settings.json
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "RosterManager");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "roster-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static RosterSettings _current = LoadFromDiskOrDefault();

    public static RosterSettings Current => _current;

    public static (bool Success, string? Error) Save(RosterSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            return (false, $"เขียนไฟล์ settings ไม่สำเร็จ: {ex.Message}");
        }

        _current = settings;
        return (true, null);
    }

    private static RosterSettings LoadFromDiskOrDefault()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<RosterSettings>(json, JsonOptions) ?? new RosterSettings();
            }
        }
        catch
        {
            // ไฟล์เสีย/ไม่มีสิทธิ์ - fallback ไปใช้ default เงียบๆ เสมอ (ไม่ควรทำให้เปิดโปรแกรมไม่ขึ้น)
        }

        return new RosterSettings();
    }
}
