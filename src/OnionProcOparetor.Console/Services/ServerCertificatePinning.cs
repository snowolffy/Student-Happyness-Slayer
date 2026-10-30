using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// ตรวจ certificate ของ Server ตอนต่อ HTTPS (WP1/WP1b, 2026-10-30 - ดู docs/roadmap.md หัวข้อ
/// "เครือข่ายและความปลอดภัย")
///
/// WP1 (เดิม): pin thumbprint เดียวจากไฟล์ %ProgramData%\OnionProcOparetor\cert\server-thumbprint.txt
/// ที่ Server's CertificateManager เขียนไว้ให้ตัวเอง - ใช้ได้ดีตอน Console รันเครื่องเดียวกับ Server
/// (เช่นเครื่อง dev) แต่ Console ตัวจริงของผู้ใช้รันอยู่อีกเครื่องเสมอ (ต่อ Server ที่ 192.168.17.249) จึง
/// ไม่มีไฟล์นั้นให้อ่านเลยในสถานการณ์จริง
///
/// WP1b (ใหม่): pin แยกตาม "host:port" เก็บไว้ใน ConsoleSettings (console-settings.json ของเครื่อง
/// Console เอง ไม่ใช่ของเครื่อง Server) พร้อม flow "ไว้ใจ certificate นี้" แบบ trust-on-first-use (TOFU)
/// คล้าย SSH known_hosts:
///   1. ต่อครั้งแรกกับ host:port หนึ่ง - ถ้า cert ไม่ได้อยู่ใน Windows trust store (self-signed) และยังไม่
///      เคย pin ไว้ -> Validate() ปฏิเสธเสมอ (fail-closed ไม่มี silent fallback) แต่ "จำ" cert ที่เจอไว้
///      ก่อน (ดู LastSeen) เพราะ callback นี้เป็นจุดเดียวที่เห็น certificate object จริง - handshake fail
///      แล้ว .NET ไม่คืน cert นั้นออกมาให้เห็นที่ไหนอีกเลย (HttpRequestException ที่ลอยขึ้นไปไม่มี cert ติด
///      มาด้วย)
///   2. MainWindow เจอ login fail ที่น่าจะมาจาก cert (เช็คจาก TryGetLastSeenCertificate - มีค่าแปลว่า TLS
///      handshake ไปถึงชั้นเห็น cert จริงแล้ว ไม่ใช่แค่ต่อ TCP ไม่ติด) ดึง cert ที่จำไว้มาโชว์ thumbprint ให้
///      ผู้ใช้เทียบกับที่ Server แสดง แล้วกดยืนยัน
///   3. ยืนยันแล้ว (TrustCertificate) pin ถูกเขียนลง console-settings.json ทันที - login ครั้งถัดไปกับ
///      host:port เดิมผ่านได้โดยไม่ต้องถามซ้ำ จนกว่า cert จะเปลี่ยน (ข้อ 4)
///   4. ถ้า host:port นี้ pin ไว้แล้วแต่ cert ที่เจอจริงไม่ตรงกับที่ pin ไว้ (Server สร้าง cert ใหม่/ย้าย
///      เครื่อง หรือกรณีเลวร้ายกว่าคือมีคนปลอมตัวเป็น Server) -> Validate() ปฏิเสธเสมอเหมือนข้อ 1 ไม่มี
///      silent fallback แต่ MainWindow ต้องรู้ว่านี่เป็นกรณี "cert เปลี่ยน" ไม่ใช่ "เจอครั้งแรก" (เทียบ
///      TryGetLastSeenCertificate กับ GetPinnedThumbprint เอง) แล้วเตือนให้ชัดเจนกว่าข้อ 2 ธรรมดา
/// </summary>
public static class ServerCertificatePinning
{
    // เดิม (WP1) - ไฟล์ที่ Server's CertificateManager เขียนไว้ให้ตัวเอง มีประโยชน์เฉพาะตอน Console รัน
    // เครื่องเดียวกับ Server เท่านั้น (loopback) เก็บไว้เป็น auto-trust เสริมสำหรับกรณีนั้นโดยเฉพาะ - ไม่ใช้
    // กับ host อื่นเพราะไฟล์นี้คือ thumbprint ของ Server เครื่องนี้เครื่องเดียว เอาไปเทียบกับ host อื่นที่ไม่ใช่
    // เครื่องนี้จะผิดความหมายทันที (คนละ cert กันโดยสิ้นเชิง)
    private static readonly string LegacySameMachineThumbprintFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "cert", "server-thumbprint.txt");

    /// <summary>
    /// cert ล่าสุดที่ Validate() เจอสำหรับ host:port หนึ่ง ไม่ว่าผลตรวจจะผ่านหรือไม่ - เก็บไว้ให้ MainWindow
    /// ดึงมาโชว์ให้ผู้ใช้ยืนยันหลัง login พัง (ดู class summary ข้อ 1-2) อายุสั้นมากโดยเจตนา: แค่ process
    /// lifetime ของ Console เอง ไม่ persist ข้าม process เพราะเป็นแค่ "สิ่งที่เพิ่งเห็นเมื่อกี้" ไม่ใช่ pin จริง
    /// </summary>
    private static readonly ConcurrentDictionary<string, SeenCertificate> LastSeen = new();

    public readonly record struct SeenCertificate(string Thumbprint, string Subject, string Issuer, DateTime NotAfter);

    /// <summary>ใช้เป็น HttpClientHandler.ServerCertificateCustomValidationCallback / SignalR's HttpMessageHandlerFactory ได้ตรงๆ</summary>
    public static bool Validate(HttpRequestMessage? request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslErrors)
    {
        if (certificate is null)
        {
            return false;
        }

        var host = request?.RequestUri?.Host;
        var port = request?.RequestUri?.Port ?? -1;
        if (string.IsNullOrEmpty(host) || port < 0)
        {
            // ไม่น่าเกิดจริง (ไม่รู้ request ปลายทางคือ host:port ไหน ก็ไม่รู้จะ pin/เทียบกับอะไร) -
            // fail-closed เท่าที่ทำได้: ยอมผ่านเฉพาะตอน Windows trust store ยืนยันแล้วเท่านั้น
            return sslErrors == SslPolicyErrors.None;
        }

        var key = HostKey(host, port);
        LastSeen[key] = new SeenCertificate(certificate.Thumbprint, certificate.Subject, certificate.Issuer, certificate.NotAfter);

        var pinned = GetPinnedThumbprint(host, port) ?? LegacySameMachineThumbprint(host);
        if (pinned is null)
        {
            // ยังไม่เคย pin host:port นี้ไว้เลย - ยอมผ่านเฉพาะตอน Windows trust store ยืนยันแล้วเท่านั้น
            // (เช่น CA จริง) self-signed ที่ยังไม่ pin ต้องให้ผู้ใช้กด "ไว้ใจ" ก่อนเสมอ (ดู TrustCertificate)
            return sslErrors == SslPolicyErrors.None;
        }

        // pin ไว้แล้ว (จาก settings จริงหรือไฟล์เดิมของเครื่องเดียวกัน) - เทียบ thumbprint ตรงๆ เท่านั้น ไม่
        // สนใจ sslErrors เลย (จุดประสงค์ของการ pin คือยอมรับ self-signed ที่ thumbprint ตรงกับที่ pin ไว้
        // จริง) ไม่ตรง = cert เปลี่ยนไปจากที่เคยไว้ใจ ปฏิเสธเสมอ ไม่ fallback เงียบๆ ไม่ว่ากรณีไหน
        return string.Equals(certificate.Thumbprint, pinned, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>สร้าง HttpClientHandler ที่ผูก Validate ไว้แล้ว - ใช้สร้าง HttpClient ของ ApiClient และ HttpMessageHandlerFactory ของ SignalR</summary>
    public static HttpClientHandler CreateHandler() => new()
    {
        ServerCertificateCustomValidationCallback = Validate
    };

    /// <summary>
    /// cert ล่าสุดที่เจอสำหรับ host:port นี้ในรอบการทำงานนี้ของ Console (ไม่ว่าผ่านตรวจหรือไม่) - null =
    /// ยังไม่เคย handshake TLS กับ host:port นี้สำเร็จถึงชั้นเห็น cert เลย (เช่น ต่อ TCP ไม่ติดตั้งแต่แรก
    /// หรือเป็น HTTP ธรรมดาที่ไม่มี TLS) MainWindow ใช้ค่านี้แยกแยะ "login พังเพราะ cert" ออกจาก "login
    /// พังเพราะเหตุอื่น" (เช่น server ปิดอยู่ หรือรหัสผ่านผิด)
    /// </summary>
    public static SeenCertificate? TryGetLastSeenCertificate(string host, int port) =>
        LastSeen.TryGetValue(HostKey(host, port), out var seen) ? seen : null;

    /// <summary>thumbprint ที่ pin ไว้สำหรับ host:port นี้จาก console-settings.json ของเครื่องนี้โดยเฉพาะ
    /// (ไม่รวมไฟล์เดิมของ WP1 - เจตนาใช้ค่านี้เทียบว่า "เคย pin เองไว้ก่อนไหม" ต่างจาก Validate() ที่ยอมรับ
    /// ไฟล์เดิมด้วยเพื่อ backward-compat)</summary>
    public static string? GetPinnedThumbprint(string host, int port) =>
        SettingsService.Current.Connection.TrustedCertificateThumbprints.TryGetValue(HostKey(host, port), out var thumb)
            ? thumb
            : null;

    /// <summary>
    /// ผู้ใช้กดยืนยัน "ไว้ใจ certificate นี้" แล้ว (หลังเทียบ thumbprint กับที่ Server แสดงด้วยตาเอง) -
    /// เขียน pin ลง console-settings.json ทันที (persist ข้าม process - ครั้งถัดไปที่เปิด Console ต่อ
    /// host:port เดิมผ่านได้โดยไม่ต้องถามซ้ำ) สร้าง ConsoleSettings ใหม่ทั้งก้อนแทนแก้ของเดิมสด ๆ เพราะ
    /// SettingsService.Save ตรวจ validate ทั้งก้อนเสมอ (pattern เดียวกับ MainWindow.SaveConnectionAsDefault)
    /// </summary>
    public static (bool Success, string? Error) TrustCertificate(string host, int port, string thumbprint)
    {
        var current = SettingsService.Current;
        var updatedPins = new Dictionary<string, string>(current.Connection.TrustedCertificateThumbprints)
        {
            [HostKey(host, port)] = thumbprint
        };

        var updated = new Models.ConsoleSettings
        {
            Vnc = current.Vnc,
            Ui = current.Ui,
            Connection = new Models.ConnectionSettings
            {
                ServerHost = current.Connection.ServerHost,
                ServerPort = current.Connection.ServerPort,
                DashboardPollIntervalSeconds = current.Connection.DashboardPollIntervalSeconds,
                UseHttps = current.Connection.UseHttps,
                TrustedCertificateThumbprints = updatedPins
            }
        };

        return SettingsService.Save(updated);
    }

    private static string? LegacySameMachineThumbprint(string host)
    {
        if (!IsLoopback(host))
        {
            return null;
        }

        try
        {
            return File.Exists(LegacySameMachineThumbprintFile) ? File.ReadAllText(LegacySameMachineThumbprintFile).Trim() : null;
        }
        catch
        {
            // อ่านไฟล์ไม่ได้ (สิทธิ์/ไฟล์เสีย) - ถือว่าไม่มี ไม่ throw ขึ้นไป (จุดนี้ถูกเรียกจาก TLS callback)
            return null;
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" || host == "::1";

    private static string HostKey(string host, int port) => $"{host.ToLowerInvariant()}:{port}";
}
