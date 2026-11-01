using System.Diagnostics.CodeAnalysis;

namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// payload ของ command "OpenUrl" (WP4-1, 2026-11-01) - Console ส่งมาตอนครูเปิดเว็บทั้งห้อง/เครื่องที่
/// เลือก (ดู wireframe tc-openurl.dc.html) Agent ต้องตรวจ Url ซ้ำว่าเป็น http/https เท่านั้นก่อนเปิดเสมอ
/// (ดู CommandProcessor.HandleOpenUrlAsync) - ห้ามเชื่อ validation ฝั่ง Console เพียงอย่างเดียว เพราะ
/// ใครก็ตามที่มี admin token ยิง POST /api/commands ตรงๆ ได้โดยไม่ผ่าน Console เลย
/// </summary>
public class OpenUrlPayload
{
    public string Url { get; set; } = string.Empty;

    /// <summary>"NewTab" (ค่าเริ่มต้น) หรือ "Fullscreen" - ดู OpenUrlMode</summary>
    public string? Mode { get; set; }
}

/// <summary>ค่าที่ยอมรับของ OpenUrlPayload.Mode - เก็บเป็น string ใน payload (ไม่ใช่ enum) เพราะ Console เก่ากว่า/ใหม่กว่า Agent ต้องยังส่ง/รับกันได้โดยไม่ throw ตอน deserialize</summary>
public static class OpenUrlMode
{
    public const string NewTab = "NewTab";
    public const string Fullscreen = "Fullscreen";
}

/// <summary>
/// ด่านตรวจ Url ของ command "OpenUrl" (WP4-1, 2026-11-01) - แยกออกมาเป็น static method ล้วนๆ
/// (ไม่พึ่ง DI/IO ใดๆ) โดยตั้งใจ เพื่อให้ unit test เรียกตรงได้โดยไม่ต้องสร้าง CommandProcessor ทั้งก้อน
/// (ซึ่งต้องพึ่ง ServerClient/AgentTrayNotifier/VncSessionManager ฯลฯ) ดู
/// CommandProcessor.HandleOpenUrlAsync ที่เรียก method นี้จริงตอนรัน และดู
/// docs/wireframes หรือ comment บน OpenUrlPayload เรื่องทำไมต้องตรวจซ้ำฝั่ง Agent
/// </summary>
public static class OpenUrlValidation
{
    /// <summary>
    /// true ถ้า <paramref name="url"/> เป็น absolute URI ที่มี scheme http หรือ https เท่านั้น (case ของ
    /// Uri.Scheme ถูก normalize เป็นตัวพิมพ์เล็กเสมอโดย System.Uri อยู่แล้ว) ปฏิเสธทุกอย่างอื่น: null/
    /// ว่าง/whitespace, scheme อื่น (file:, javascript:, ftp:, custom protocol handler ฯลฯ), relative
    /// path, และ UNC path (\\server\share ไม่ใช่ absolute URI ที่ Uri.TryCreate รับเป็น http/https)
    /// </summary>
    public static bool TryGetAllowedUri(string? url, out Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            uri = null;
            return false;
        }

        uri = parsed;
        return true;
    }
}

/// <summary>
/// ด่านตรวจของ command "LaunchProgram" (WP4-1, 2026-11-01) - แยกออกมาเป็น static method เหตุผลเดียวกับ
/// OpenUrlValidation ข้างบน: รับผลที่ ServerClient.GetLaunchableProgramAsync คืนมาแล้ว (null ทั้งกรณี
/// 404/ติดต่อ Server ไม่ได้ - ดู doc comment บน method นั้น) แล้วตัดสินว่า "อนุญาตให้รันไหม" โดยไม่ต้อง
/// ยิง HTTP จริงเพื่อทดสอบ logic นี้
/// </summary>
public static class LaunchProgramValidation
{
    /// <summary>false ถ้า program เป็น null (ไม่พบ/ติดต่อ Server ไม่ได้) หรือ Path ว่าง
    /// [NotNullWhen(true)] บอก nullable analyzer ว่าถ้าคืน true แปลว่า program ไม่ null แน่นอน -
    /// CommandProcessor.HandleLaunchProgramAsync เข้าถึง program.Path/.Name ต่อได้โดยไม่ต้องเช็คซ้ำ</summary>
    public static bool IsAllowedToLaunch([NotNullWhen(true)] LaunchableProgramDto? program) =>
        program is not null && !string.IsNullOrWhiteSpace(program.Path);
}
