namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เขียน diagnostic log ตรงๆ ลงไฟล์แบบไม่พึ่ง DI/ILogger เลย ใช้เฉพาะช่วง startup
/// ก่อนที่ host จะ build เสร็จ (ตอนนั้น ILogger ยังไม่พร้อมใช้งาน) เพื่อไล่ปัญหา
/// working directory / content root ผิดตอนรันเป็น Windows Service (ไม่มี console ให้ดู)
/// </summary>
public static class StartupDiagnostics
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "Agent", "logs", "startup-debug.log");

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
            // เขียนไม่ได้ก็ปล่อยผ่าน - ไม่ควรทำให้ agent ทำงานหลักพังเพราะ log เขียนไม่ได้
        }
    }
}
