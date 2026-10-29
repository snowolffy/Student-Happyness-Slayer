using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>ตั้งค่าที่บันทึกไว้ในเครื่องเพื่อไม่ต้องกรอก login ใหม่ทุกครั้งที่เปิดโปรแกรม</summary>
public class RemoteBridgeConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>รหัสประจำ remote นี้ (เผื่อมีหลายตัวในอนาคต) - แค่ label ให้คนอ่าน ไม่ใช่ตัวยืนยันตัวตน (ดู RemoteSecret)</summary>
    public string RemoteId { get; set; } = string.Empty;

    /// <summary>password เข้ารหัสด้วย DPAPI (ผูกกับ Windows user account ของเครื่องนี้เท่านั้น) - ไม่เก็บ plaintext</summary>
    public string? EncryptedPassword { get; set; }

    /// <summary>
    /// secret สุ่มขึ้นเองตอน login ครั้งแรก - ใช้ยืนยันตัวตนของ ESP32/ฮาร์ดแวร์ที่ยิงมาที่ local HTTP
    /// listener (ดู RemoteTriggerListener) คนละตัวกับ password login โดยตั้งใจ เพราะหลัง Tailscale
    /// Funnel แล้ว endpoint นี้เปิดสู่อินเทอร์เน็ตสาธารณะจริง - ถ้าเผลอเอา admin credential ไปฝังใน
    /// firmware แล้วหลุด จะเสียหายหนักกว่ามาก (สิทธิ์เต็ม Console) secret ตัวนี้ทำได้แค่ lock/unlock เท่านั้น
    /// เก็บ plaintext ได้ (ไม่เข้ารหัสเหมือน password) เพราะต้องเอาไปกรอกใน Arduino sketch อยู่ดี
    /// </summary>
    public string RemoteSecret { get; set; } = string.Empty;

    /// <summary>port ที่ local HTTP listener เปิดฟัง (ดู RemoteTriggerListener) - Tailscale Funnel ต้องชี้มาที่ port นี้</summary>
    public int LocalListenerPort { get; set; } = 51888;
}

/// <summary>
/// เก็บ config ไว้ที่ %AppData%\OnionProcOparetor.RemoteBridge\config.json
///
/// บัญชีที่ RemoteBridge ใช้มีสิทธิ์เท่า Console เต็มรูปแบบ (ระบบยังไม่มี role แยกระดับ - ดู
/// project memory เรื่อง remote lock bridge) จึงเข้ารหัส password ด้วย DPAPI แทน plaintext
/// เป็นการลดความเสียหายถ้าไฟล์ config หลุดออกจากเครื่องนี้ไป (DPAPI ผูกกับ user account ของเครื่อง
/// ที่เข้ารหัสไว้ ถอดที่เครื่องอื่นไม่ได้) - ไม่ได้อ้างว่าปลอดภัยเท่า secret manager จริงจัง
/// </summary>
public static class ConfigStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OnionProcOparetor.RemoteBridge", "config.json");

    /// <summary>
    /// cache ใน memory ของ config ล่าสุดที่ Load()/Save() มา - RemoteTriggerListener อ่านค่านี้ทุก
    /// request (ผ่าน property นี้ตรงๆ ไม่ใช่เปิดไฟล์อ่านใหม่ทุกครั้ง) ต้องอัปเดตคู่กับ Save() เสมอ
    /// ไม่งั้น listener จะเห็น secret เก่าค้างอยู่หลังจากมีการ save ค่าใหม่ระหว่างที่โปรแกรมยังรันอยู่
    /// </summary>
    public static RemoteBridgeConfig? Current { get; private set; }

    public static RemoteBridgeConfig? Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<RemoteBridgeConfig>(json);
            return Current;
        }
        catch
        {
            // config เสีย/อ่านไม่ได้ - ให้ผู้ใช้เห็นหน้า login ใหม่แทนที่จะ crash ทั้งโปรแกรม
            return null;
        }
    }

    public static void Save(RemoteBridgeConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
        Current = config;
    }

    /// <summary>
    /// สุ่ม secret ใหม่ 32 ไบต์ - เข้ารหัสเป็น hex (ไม่ใช่ base64 แบบ AuthToken ฝั่ง Server) โดยตั้งใจ
    /// เพราะ secret ตัวนี้ต้องเอาไปพิมพ์/copy ลง Arduino sketch แล้วส่งเป็น URL query param จาก ESP32 -
    /// base64 มี '+' '/' '=' ที่ต้อง URL-encode และพิมพ์สับสนง่าย hex ปลอดภัยกว่าในทุกบริบทที่จะใช้
    /// </summary>
    public static string GenerateRemoteSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    public static string EncryptPassword(string password)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string DecryptPassword(string encrypted)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
