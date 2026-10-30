using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// Ported from OnionProcOparetor.Console/Services/ServerCertificatePinning.cs (WP1, 2026-10-30 - ดู
/// docs/roadmap.md หัวข้อ "เครือข่ายและความปลอดภัย") - OnlineConsole รันอยู่บนเครื่องเดียวกับ
/// OnionProcOparetor.Server เสมอ (ดู comment ใน Program.cs เรื่อง Tailscale gating/ResolveOwnTailnetIpAsync
/// ที่ต้องรันคู่กัน) จึงเจอไฟล์ %ProgramData%\OnionProcOparetor\cert\server-thumbprint.txt ที่ CertificateManager
/// ฝั่ง Server เขียนไว้ให้ตัวเองได้ทันทีโดยไม่ต้อง provision อะไรเพิ่ม - ต่างจาก WPF Console ที่อาจรันบน
/// เครื่องอื่น (ครูพิมพ์ IP:Port เอง) ซึ่งต้อง provision ไฟล์นี้เองถ้าจะใช้ pin
///
/// ถ้าไม่มีไฟล์นี้ (ยังไม่เคย provision/Server ยังไม่เคย start) ตกกลับไปใช้ผลตรวจสอบปกติของ .NET
/// (เชื่อถือผ่าน Windows trust store เท่านั้น) - fail-closed เสมอ ไม่มี fallback แบบ "เชื่อหมดทุกใบ"
/// </summary>
public static class ServerCertificatePinning
{
    private static readonly string ThumbprintFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "cert", "server-thumbprint.txt");

    public static bool Validate(HttpRequestMessage? request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslErrors)
    {
        if (certificate is null)
        {
            return false;
        }

        var expectedThumbprint = ReadExpectedThumbprint();
        if (string.IsNullOrWhiteSpace(expectedThumbprint))
        {
            return sslErrors == SslPolicyErrors.None;
        }

        return string.Equals(certificate.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    public static HttpClientHandler CreateHandler() => new()
    {
        ServerCertificateCustomValidationCallback = Validate
    };

    private static string? ReadExpectedThumbprint()
    {
        try
        {
            return File.Exists(ThumbprintFilePath) ? File.ReadAllText(ThumbprintFilePath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
