using System.IO;
using System.Text.Json;
using OnionProcOparetor.Console.Models;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// โหลด/บันทึก config ฝั่ง Console เอง (แยกจาก Agent's appsettings.json - รูปแบบไฟล์ต่างกันเพราะ Agent
/// bind เข้ากับ Generic Host's IOptions ส่วน Console เป็น plain WPF ไม่มี DI/Host) เก็บที่
/// %ProgramData%\OnionProcOparetor\Console\console-settings.json - ใช้ CommonApplicationData
/// (ProgramData) ให้ตรงกับ pattern เดิมที่ ConsoleDiagnostics ใช้อยู่แล้วสำหรับ console.log
/// (%ProgramData%\OnionProcOparetor\Console\logs\) แทนที่จะใช้ per-user AppData
///
/// โหลด/เขียนผิดพลาด (ไฟล์เสีย/ไม่มีสิทธิ์) fallback ไปใช้ default เงียบๆ เสมอ - ไม่ควรทำให้ Console
/// เปิดไม่ขึ้นเพราะ settings file มีปัญหา
/// </summary>
public static class SettingsService
{
    private const int MaxVncSessions = 100;
    private const int MinPollIntervalSeconds = 2;
    private const int MaxPollIntervalSeconds = 300;

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "Console");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "console-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static ConsoleSettings _current = LoadFromDiskOrDefault();

    public static ConsoleSettings Current => _current;

    /// <summary>เรียกทุกครั้งที่ Save สำเร็จ - DashboardWindow subscribe ไว้เพื่ออัพเดต auto-refresh interval แบบ live โดยไม่ต้อง restart Console</summary>
    public static event Action<ConsoleSettings>? Changed;

    /// <summary>
    /// เรียกครั้งเดียวตอน App เริ่มทำงาน (App.xaml.cs OnStartup) เพื่อบังคับให้ static field initializer
    /// ข้างบนทำงานแน่นอนก่อนหน้าต่างไหนเปิดขึ้นมา (ไม่งั้น .NET จะ lazy-init type ตอนถูกแตะครั้งแรกเท่านั้น
    /// ซึ่งอาจช้ากว่าจุดที่ VncSessionPool ต้องการค่า MaxConcurrentSessions ที่ถูกต้องแล้ว)
    /// </summary>
    public static void EnsureLoaded() => ApplyToRuntime(_current);

    public static (bool Success, string? Error) Save(ConsoleSettings settings)
    {
        var error = Validate(settings);
        if (error is not null)
        {
            return (false, error);
        }

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
        ApplyToRuntime(settings);
        Changed?.Invoke(settings);
        return (true, null);
    }

    /// <summary>
    /// VncSessionPool.MaxConcurrentSessions เป็น static settable property (เปลี่ยนจาก const เดิมโดยเจตนา
    /// เพื่อให้ config ตรงนี้มีผลได้) - VncSessionPool.OpenAsync อ่านค่านี้สดทุกครั้งตอนจอง slot เปลี่ยนค่า
    /// ตรงนี้จึงมีผลทันทีกับการเปิด session ครั้งถัดไป ไม่กระทบ session ที่เปิดค้างอยู่แล้ว
    /// </summary>
    private static void ApplyToRuntime(ConsoleSettings settings)
    {
        VncSessionPool.MaxConcurrentSessions = settings.Vnc.MaxConcurrentSessions;
    }

    private static string? Validate(ConsoleSettings settings)
    {
        if (settings.Vnc.MaxConcurrentSessions < 1 || settings.Vnc.MaxConcurrentSessions > MaxVncSessions)
        {
            return $"VNC concurrent session limit ต้องอยู่ระหว่าง 1-{MaxVncSessions}";
        }

        if (settings.Connection.DashboardPollIntervalSeconds < MinPollIntervalSeconds ||
            settings.Connection.DashboardPollIntervalSeconds > MaxPollIntervalSeconds)
        {
            return $"Dashboard auto-refresh interval ต้องอยู่ระหว่าง {MinPollIntervalSeconds}-{MaxPollIntervalSeconds} วินาที";
        }

        if (string.IsNullOrWhiteSpace(settings.Connection.ServerHost))
        {
            return "Server host ห้ามว่าง";
        }

        if (!int.TryParse(settings.Connection.ServerPort, out var port) || port < 1 || port > 65535)
        {
            return "Server port ต้องเป็นตัวเลข 1-65535";
        }

        return null;
    }

    private static ConsoleSettings LoadFromDiskOrDefault()
    {
        ConsoleSettings settings;
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                settings = JsonSerializer.Deserialize<ConsoleSettings>(json, JsonOptions) ?? new ConsoleSettings();
            }
            else
            {
                settings = new ConsoleSettings();
            }
        }
        catch
        {
            settings = new ConsoleSettings();
        }

        ApplyToRuntime(settings);
        return settings;
    }
}
