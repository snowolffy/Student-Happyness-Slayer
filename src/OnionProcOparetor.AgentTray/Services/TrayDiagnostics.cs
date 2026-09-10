using System.IO;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// log ไฟล์กลางของ AgentTray - จำเป็นขึ้นมาเพราะอาการ "ตื่นจาก sleep แล้ว Tray ค้าง ไม่ตอบสนอง"
/// (รายงาน 2026-09-10) วินิจฉัยจากโค้ดอย่างเดียวไม่ได้ ต้องมีหลักฐานจากเครื่องจริงตอนเกิดอาการ
/// ก่อนหน้านี้ AgentTray แทบไม่ log อะไรเลย - BroadcastPipeListener ใช้ Debug.WriteLine ซึ่งมองไม่เห็น
/// ถ้าไม่มี debugger attach อยู่ ทำให้ทุกอย่างที่เกิดใน field หายไปหมดไม่เหลือร่องรอย
///
/// เขียนลง %LocalAppData% ไม่ใช่ %ProgramData% ด้วยเหตุผลเดียวกับ PhysicalInputBlocker.LogPath -
/// AgentTray รันเป็น user ธรรมดา (ไม่ elevated) ProgramData อาจเขียนไม่ได้ถ้า ACL ถูกจำกัดไว้
///
/// สำคัญกับเครื่องในห้องแล็บ: ทั้ง 40 เครื่องใช้ Reboot Restore Rx ไฟล์นี้จะหายทุกครั้งที่รีบูต
/// เพราะฉะนั้นต้องเก็บ log ตอนที่อาการยังค้างอยู่ "ก่อน" รีสตาร์ทเครื่องเสมอ ไม่งั้นไม่เหลืออะไรให้ดู
/// </summary>
public static class TrayDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OnionProcOparetor", "AgentTray", "logs", "tray.log");

    /// <summary>ตัดไฟล์ทิ้งเมื่อโตเกินนี้ - Tray รันค้างเป็นสัปดาห์ได้ ไม่ควรปล่อยให้ไฟล์โตไม่จำกัด</summary>
    private const long MaxLogBytes = 2 * 1024 * 1024;

    private static readonly object Lock = new();

    /// <summary>ปลอดภัยเรียกจาก thread ไหนก็ได้ - ล้มเหลวเงียบๆ เสมอ (log พังต้องไม่ทำให้ Tray พังตาม)</summary>
    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir is not null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    File.Delete(LogPath);
                }

                // ใส่วันที่เต็มเสมอ (ไม่ใช่แค่ HH:mm:ss แบบ input-blocker.log) - อาการที่ต้องไล่คือ
                // "หลังตื่นจาก sleep" ซึ่งข้ามวันได้ ถ้าไม่มีวันที่จะแยกไม่ออกว่าบรรทัดไหนอยู่รอบไหน
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n");
            }
        }
        catch
        {
            // ตั้งใจกลืน - ดู doc comment ของ class
        }
    }
}
