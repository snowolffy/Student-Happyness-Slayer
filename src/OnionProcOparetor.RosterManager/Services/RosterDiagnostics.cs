using System.IO;

namespace OnionProcOparetor.RosterManager.Services;

/// <summary>
/// เขียน diagnostic log ของ Roster Manager ลงไฟล์ตรงๆ - pattern เดียวกับ Console/Services/
/// ConsoleDiagnostics.cs (WPF ธรรมดา ไม่มี DI/ILogger) แยกไฟล์ log คนละตัวจาก Console/Server เพราะเป็น
/// โปรแกรมคนละตัว (ดู docs/roadmap.md WP3-2 "Roster Manager เป็นโปรแกรมแยก (WPF)")
/// </summary>
public static class RosterDiagnostics
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "RosterManager", "logs", "roster-manager.log");

    public static void WriteLine(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // เขียนไม่ได้ก็ปล่อยผ่าน - ไม่ควรทำให้โปรแกรมหลักพังเพราะ log เขียนไม่ได้
        }
    }
}
