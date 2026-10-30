using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>
/// Ported from OnionProcOparetor.Console/Services/ServerCertificatePinning.cs (WP1, 2026-10-30 - ดู
/// docs/roadmap.md หัวข้อ "เครือข่ายและความปลอดภัย") - RemoteBridge พิมพ์ IP:Port ของ Server เอง
/// เหมือน Console (ดู LoginWindow.xaml.cs/ApiClient.SetServer) ไม่ได้ผ่าน MSI install เสมอไป จึง pin
/// thumbprint จากไฟล์เดียวกับที่ Console/OnlineConsole ใช้: %ProgramData%\OnionProcOparetor\cert\
/// server-thumbprint.txt (ไฟล์ของ Server's CertificateManager เอง - เจอทันทีถ้า RemoteBridge รันอยู่
/// บนเครื่องเดียวกับ Server)
///
/// ถ้าไม่มีไฟล์นี้ ตกกลับไปใช้ผลตรวจสอบปกติของ .NET (เชื่อถือผ่าน Windows trust store เท่านั้น) -
/// fail-closed เสมอ ไม่มี fallback แบบ "เชื่อหมดทุกใบ"
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
