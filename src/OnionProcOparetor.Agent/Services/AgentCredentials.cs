using System.Diagnostics;
using System.Security.Cryptography;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ความลับประจำเครื่องที่ใช้พิสูจน์ตัวตนกับ Server (2026-08-31, จาก security audit)
///
/// ปัญหาเดิม: endpoint ฝั่ง agent ทั้งหมด (/api/poll, /api/logs, /api/commands/{id}/ack,
/// /api/commands/{id}/result และ SignalR hub ทั้งตัว) ไม่มี auth เลย ใช้แค่ "รู้ ClientGuid" เป็น
/// การยืนยันตัวตน - แต่ ClientGuid เดินทางเป็น path segment ใน URL ของทุก poll บน HTTP ธรรมดา
/// ใครที่นั่งอยู่ใน LAN เดียวกัน (คือทุกเครื่องในห้อง) ดักดูก็เก็บ GUID ของทุกเครื่องได้ครบ แล้วเอาไป:
///   - ดึงกฎทั้งชุดมาดูว่าอะไรถูกบล็อกบ้าง เพื่อหาทางเลี่ยง
///   - ยิง ack ใส่ commandId ที่เป็นเลขรันนิ่ง เพื่อทำให้คำสั่งของครูที่ส่งมาหาเครื่องตัวเอง
///     ถูก mark ว่า "ส่งแล้ว" โดยที่ไม่เคยถูกรันจริง
///   - ปลอมผลลัพธ์ command / ปลอม log
///
/// วิธีแก้: ความลับ 32 ไบต์ที่สุ่มขึ้นบนเครื่องเอง ไม่เคยเดินทางในรูปแบบที่ใช้ซ้ำได้จาก URL และ
/// เก็บในโฟลเดอร์ที่ ACL ตัดกลุ่ม Users ออกแล้ว (โฟลเดอร์เดียวกับ wd.key ของ tamper watchdog)
///
/// <b>ข้อควรระวังเรื่อง disk freeze:</b> เครื่อง lab ทุกเครื่องมี Reboot Restore Rx ไฟล์ที่เขียน
/// ตอน runtime จะหายทุก reboot - agent.key จึงต้องถูกสร้างตอน "ติดตั้ง" (install-watchdog.ps1)
/// เพื่อให้ติดไปกับ baseline ไม่ใช่ปล่อยให้คลาสนี้สร้างเองตอนรันครั้งแรก การสร้างเองยังคงอยู่
/// เป็น fallback แต่ถ้าเกิดขึ้นจริงบนเครื่อง lab แปลว่ามีอะไรผิด (ดู WasGeneratedAtRuntime)
///
/// ข้อจำกัดที่ยอมรับตรงๆ: ระบบยังเป็น HTTP ล้วน ความลับนี้จึงเดินทางเป็น plaintext ใน header
/// เหมือนกัน คนที่ดักอ่าน traffic ได้ยังขโมยไปใช้ได้อยู่ - สิ่งที่แก้ได้จริงคือ "รู้ GUID ของเครื่อง
/// อื่นแล้วปลอมเป็นเครื่องนั้นได้ทันที" ซึ่งเป็นระดับที่นักเรียนทำได้ง่ายกว่าการดัก packet มาก
/// การอุดช่องที่เหลือต้องใช้ TLS หรือ HMAC ต่อ request (บันทึกไว้ใน HARDENING-REPORT ว่าเป็น
/// ขั้นถัดไป) - ไม่ได้อ้างว่าตรงนี้แก้ครบแล้ว
/// </summary>
public static class AgentCredentials
{
    private static readonly Lazy<string> LazySecret = new(LoadOrCreateSecret, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Header ที่แนบไปกับทุก request ฝั่ง agent - ต้องตรงกับที่ Server อ่าน</summary>
    public const string SecretHeaderName = "X-Agent-Secret";

    /// <summary>Header บอกว่าเป็นเครื่องไหน - จำเป็นสำหรับ endpoint ที่ไม่มี clientGuid ใน URL (ack/result)</summary>
    public const string GuidHeaderName = "X-Agent-Guid";

    public static string Secret => LazySecret.Value;

    /// <summary>
    /// true = ต้องสร้าง secret ขึ้นมาเองตอน runtime เพราะหาไฟล์ไม่เจอ
    ///
    /// บนเครื่อง lab ที่มี Reboot Restore Rx ค่านี้เป็น true คือ "สัญญาณเตือน" ไม่ใช่เรื่องปกติ:
    /// agent.key ควรถูกสร้างตอนติดตั้งโดย install-watchdog.ps1 แล้วติดไปกับ baseline
    /// ถ้าต้องมาสร้างเองตอน runtime แปลว่าไฟล์นั้นอยู่นอก baseline = จะหายทุก reboot = Server
    /// จะเห็น secret ไม่ตรงทุกครั้งที่บูต (ดู TamperProtectionService ที่ log เตือนเรื่องนี้)
    /// </summary>
    public static bool WasGeneratedAtRuntime { get; private set; }

    private static string GetKeyPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "Agent", "agent.key");

    private static string LoadOrCreateSecret()
    {
        var keyPath = GetKeyPath();

        try
        {
            if (File.Exists(keyPath))
            {
                var existing = File.ReadAllText(keyPath).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }
            }
        }
        catch
        {
            // อ่านไม่ได้ (ไฟล์เสีย/สิทธิ์เพี้ยน) - สร้างใหม่ทับไปเลย ผลที่ตามมาคือ Server จะเห็นว่า
            // secret ไม่ตรงกับที่ enroll ไว้ ซึ่งครูแก้ได้จาก Console (re-enroll เครื่องนั้น)
        }

        // มาถึงตรงนี้ = install-watchdog.ps1 ไม่ได้สร้าง agent.key ไว้ให้ (หรือมันหายไปแล้ว)
        // ยังสร้างเองได้เพื่อไม่ให้ Agent ใช้งานไม่ได้เลย แต่ต้องมีคนรู้ - ดู WasGeneratedAtRuntime
        WasGeneratedAtRuntime = true;
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            var dir = Path.GetDirectoryName(keyPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(keyPath, secret);
            RestrictToSystemAndAdmins(dir);
        }
        catch
        {
            // เขียนไม่ลง = ใช้ secret ใน memory ไปก่อนรอบนี้ (จะเปลี่ยนทุกครั้งที่ service restart
            // ทำให้ enroll ใหม่ทุกรอบ - ไม่พังการทำงาน แต่จะเห็น log เตือนฝั่ง Server ถี่ๆ)
        }

        return secret;
    }

    /// <summary>
    /// ตัด inheritance แล้วให้สิทธิ์เฉพาะ SYSTEM + Administrators - เหมือนที่ install-watchdog.ps1 ทำ
    /// ทำซ้ำที่นี่โดยตั้งใจ ไม่พึ่งว่าสคริปต์นั้นรันสำเร็จมาก่อน: ถ้า CustomAction ตอนติดตั้งล้มเหลว
    /// เงียบๆ (Return="ignore") โฟลเดอร์นี้จะยังเป็น ACL default ของ ProgramData ที่ Users สร้าง
    /// ไฟล์ได้ - ปล่อยไว้เท่ากับเก็บ agent.key ไว้ในที่ที่นักเรียนอาจอ่านได้ ซึ่งทำให้ทั้งกลไกนี้ไร้ผล
    /// </summary>
    private static void RestrictToSystemAndAdmins(string directory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = $"\"{directory}\" /inheritance:r /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /T /C /Q",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(milliseconds: 30_000);
        }
        catch
        {
            // best-effort - install-watchdog.ps1 จะทำซ้ำให้อีกทีภายใน 1 นาที (TamperProtectionService)
        }
    }
}
