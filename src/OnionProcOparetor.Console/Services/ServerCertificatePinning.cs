using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace OnionProcOparetor.Console.Services;

/// <summary>
/// ตรวจ certificate ของ Server ตอนต่อ HTTPS (WP1, 2026-10-30 - ดู docs/roadmap.md หัวข้อ
/// "เครือข่ายและความปลอดภัย") Console เป็นแอปที่ครูพิมพ์ IP:Port เองได้ทุกครั้งที่ login (ไม่ได้ผ่าน
/// MSI install เสมอไปแบบ Agent) จึงเลือก "pin thumbprint" แทน "เชื่อถือผ่าน store" (อีกแบบที่ roadmap
/// อนุญาตไว้ - Agent/AgentTray ใช้แบบนั้นเพราะ MSI ติดตั้ง cert ลง LocalMachine\Root ให้อยู่แล้ว)
///
/// อ่าน thumbprint ที่ pin ไว้จาก %ProgramData%\OnionProcOparetor\cert\server-thumbprint.txt - ไฟล์
/// เดียวกับที่ Server's CertificateManager เขียนไว้ให้ตัวเอง (ถ้า Console รันอยู่บนเครื่องเดียวกับ
/// Server เช่นเครื่อง dev หรือเครื่อง Server+Console MSI จริง จะเจอไฟล์นี้ทันทีไม่ต้องตั้งอะไรเพิ่ม)
/// เครื่องอื่น (เช่น Console บนโน้ตบุ๊กครูที่ต่อ Server ข้ามเครือข่าย) ต้อง provision ไฟล์นี้เอง (คัดลอก
/// มาจาก Server หรือ pin จาก GET /security/certificate/thumbprint) - ยังไม่มี UI ให้ทำเรื่องนี้ใน
/// Console เอง (ต้องรอ WP6/WP7 ถ้าจะทำ flow "ยืนยัน/ไว้ใจ certificate นี้" ในแอป)
///
/// ถ้าไม่มีไฟล์นี้เลย (ยังไม่ pin อะไรไว้) ตกกลับไปใช้ผลตรวจสอบปกติของ .NET (เชื่อถือผ่าน Windows trust
/// store เท่านั้น) - self-signed cert ที่ไม่ได้ติดตั้งไว้ในนั้นจะต่อไม่ได้โดย default อยู่แล้ว (fail-closed
/// เสมอ ไม่มีจุดไหนที่ fallback กลับไปเป็น "เชื่อหมดทุกใบ")
/// </summary>
public static class ServerCertificatePinning
{
    private static readonly string ThumbprintFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "cert", "server-thumbprint.txt");

    /// <summary>ใช้เป็น HttpClientHandler.ServerCertificateCustomValidationCallback / SignalR's HttpMessageHandlerFactory ได้ตรงๆ</summary>
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

        // pin ไว้แล้ว - เทียบ thumbprint ตรงๆ เท่านั้น ไม่สนใจ sslErrors เลย (จุดประสงค์ของการ pin คือ
        // ยอมรับ self-signed cert ที่ไม่อยู่ใน trust store ได้ ถ้า thumbprint ตรงกับที่ pin ไว้จริง)
        return string.Equals(certificate.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>สร้าง HttpClientHandler ที่ผูก Validate ไว้แล้ว - ใช้สร้าง HttpClient ของ ApiClient และ HttpMessageHandlerFactory ของ SignalR</summary>
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
            // อ่านไฟล์ไม่ได้ (สิทธิ์/ไฟล์เสีย) - ถือว่าไม่ได้ pin ไว้ ตกไปใช้ default validation
            return null;
        }
    }
}
