using System.IO;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// เขียน diagnostic log ลงไฟล์ตรงๆ แบบเดียวกับ StartupDiagnostics ฝั่ง Agent - Console ไม่มี DI/ILogger
/// อยู่แล้ว (plain WPF, ไม่ใช้ generic host) และของเดิมที่ LabHubClient ใช้ (Debug.WriteLine) ถูกตัดออก
/// ทั้งหมดตอน publish -c Release เพราะมี [Conditional("DEBUG")] ติดมากับ Debug.WriteLine โดย .NET เอง -
/// แปลว่า deployed Console ไม่เคย log อะไรเกี่ยวกับ SignalR connect/reconnect/register เลยจริงๆ ทำให้
/// ไล่ปัญหา "VNC session ready ไม่ถึง Console" ต่อไม่ได้ (ดู agent.log ที่ยืนยันว่า Agent ส่ง relay
/// สำเร็จแล้ว - ปัญหาน่าจะอยู่ช่วงหลังจากนั้นฝั่ง Server/Console แต่ไม่มีหลักฐานอะไรเลย)
/// </summary>
public static class ConsoleDiagnostics
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "Console", "logs", "console.log");

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
            // เขียนไม่ได้ก็ปล่อยผ่าน - ไม่ควรทำให้ Console ทำงานหลักพังเพราะ log เขียนไม่ได้
        }
    }
}
