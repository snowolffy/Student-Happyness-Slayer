namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// กัน AgentTray รันซ้อนกันหลาย instance ใน user session เดียวกัน
///
/// จำเป็นขึ้นมาเพราะ safety net ฝั่ง Agent (AgentTrayRecoveryService) จะสตาร์ท AgentTray ใหม่
/// ทุกครั้งที่ส่ง pipe ไม่สำเร็จ - แต่ "ส่งไม่สำเร็จ" ไม่ได้แปลว่า Tray ตายเสมอไป มีจังหวะที่
/// Tray ยังรันอยู่ดีๆ แต่ connect ไม่ติดพอดี (BroadcastPipeListener เปิด server ทีละ 1 instance
/// แล้วสร้างใหม่หลังรับ message เสร็จ - ระหว่างช่วงสร้างใหม่นั้น client ที่ต่อเข้ามาจะ fail)
/// ถ้าไม่มีตัวกันนี้ เคสนั้นจะได้ tray icon ซ้อนกัน 2 อันบน taskbar ซึ่งสับสนและทำให้มี
/// pipe listener 2 ตัวแย่งกันรับ message
///
/// ใช้ mutex แบบ "Local\" (ไม่ใช่ "Global\") โดยตั้งใจ - ขอบเขตคือต่อ session ไม่ใช่ต่อเครื่อง
/// เพราะถ้ามี user หลายคน login พร้อมกัน (fast user switching) แต่ละ session ต้องมี Tray ของ
/// ตัวเองจริงๆ (แต่ละ session มี desktop คนละอัน) การใช้ Global จะทำให้ session ที่ 2 ไม่มี Tray เลย
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\OnionProcOparetor.AgentTray.SingleInstance";

    private Mutex? _mutex;

    /// <summary>
    /// true = instance นี้เป็นตัวแรก (ทำงานต่อได้)
    /// false = มี AgentTray รันอยู่แล้วใน session นี้ (ผู้เรียกควรปิดตัวเองทันที)
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return createdNew;
        }
        catch (Exception)
        {
            // ถ้าจอง mutex ไม่ได้ด้วยเหตุผลใดก็ตาม ให้ "ปล่อยผ่าน" (ถือว่าเป็นตัวแรก) ดีกว่า
            // ปิดตัวเองทิ้ง - Tray ที่รันซ้อนกันยังดีกว่าไม่มี Tray เลย (lock/shutdown จะพังหมด)
            return true;
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // ไม่ได้เป็นเจ้าของ mutex อยู่ - ไม่ใช่เรื่องผิดปกติตอนปิดโปรแกรม ข้ามไป
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
