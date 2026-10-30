using System.Security.Cryptography;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// Shared secret ของ loopback API (127.0.0.1:8788) - ปิดช่องที่ process อื่นบนเครื่องเดียวกัน (เช่น
/// นักเรียนเขียน script เอง) ยิง POST ตรงมาที่ /vnc/session-result หรือ /processes/list-result ได้เอง
/// โดยไม่ต้องผ่าน AgentTray จริง ซึ่งเดิมทำให้ยิง PID/ผลลัพธ์ปลอมเข้ามาได้ แล้ว Agent (รันเป็น SYSTEM)
/// จะเปิด firewall rule ให้ตามนั้น (ดู WP2b ใน docs/roadmap.md)
///
/// สุ่มใหม่ทุกครั้งที่ Agent process เริ่ม (constructor) เก็บไว้ใน memory เท่านั้น - ไม่ persist ข้าม
/// restart ไม่เขียนลงดิสก์/registry เลย (ตาม disk freeze constraint ใน CLAUDE.md - ไม่มี state ฝั่ง
/// client ที่ต้อง "รอด" reboot อยู่แล้วสำหรับค่านี้ เพราะ AgentTray ได้ค่าใหม่ทุกครั้งผ่าน pipe)
///
/// AgentTrayNotifier เป็นคนแนบ Value ไปกับทุก message ที่ส่งผ่าน named pipe (ดู SendAsync) - ฝั่ง
/// AgentTray เก็บค่าล่าสุดไว้เสมอ (LoopbackSecretStore) ทำให้ sync กันเองโดยไม่ต้องมี round-trip แยก
/// แม้ Agent restart แล้วสุ่มค่าใหม่ เพราะค่าใหม่มาถึงพร้อมกับคำสั่ง StartVncSession/GetRunningProcesses
/// ที่ทำให้ AgentTray ต้อง callback กลับมาที่ 2 endpoint นี้อยู่แล้ว
///
/// แยกออกมาเป็นคลาสเดี่ยวๆ ไม่มี dependency อื่นเลย (ไม่ใช่ ILogger, ไม่ใช่ DI service อื่น) โดยตั้งใจ
/// เพื่อให้ unit test ตรรกะเปรียบเทียบได้โดยไม่ต้องรัน Agent จริง (ต้องรันเป็น LocalSystem service จึง
/// รันจริงบนเครื่อง dev ไม่ได้ - ดูรายงาน WP2b ว่าทดสอบคลาสนี้แยกยังไง)
/// </summary>
public sealed class LoopbackSecret
{
    private readonly byte[] _secretBytes;

    public LoopbackSecret()
    {
        _secretBytes = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>ค่า secret ปัจจุบันของ Agent process นี้ (base64) - แนบไปกับทุก message ที่ส่งผ่าน pipe ให้ AgentTray</summary>
    public string Value => Convert.ToBase64String(_secretBytes);

    /// <summary>
    /// true เมื่อค่าที่ AgentTray แนบมาตรงกับ secret จริงของ Agent process นี้ - เปรียบเทียบแบบ
    /// constant-time (CryptographicOperations.FixedTimeEquals) กัน timing attack ที่ process อื่นบน
    /// เครื่องเดียวกันอาจใช้เดา secret จากเวลาตอบสนองที่ต่างกัน
    ///
    /// ปฏิเสธทันที (fail-secure) ถ้าไม่มีค่าแนบมาเลย (null/empty), แนบมาเป็น base64 ที่ parse ไม่ได้,
    /// หรือความยาวหลัง decode ไม่ตรงกับความยาวจริง (32 bytes เสมอ - ความยาวที่ถูกต้องตายตัวอยู่แล้ว
    /// ไม่ใช่ข้อมูลลับที่รั่วเพิ่มจากการเช็คนี้) - เฉพาะกรณีความยาวตรงกันเท่านั้นที่ไปถึงขั้น FixedTimeEquals จริง
    /// </summary>
    public bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromBase64String(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        if (providedBytes.Length != _secretBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, _secretBytes);
    }
}
