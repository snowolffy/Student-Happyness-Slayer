using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สร้าง/โหลด self-signed certificate สำหรับ Kestrel ฟัง HTTPS (WP1, 2026-10-30 - ดู
/// docs/roadmap.md หัวข้อ "เครือข่ายและความปลอดภัย": "HTTPS ด้วย self-signed certificate")
///
/// เก็บไว้ที่ %ProgramData%\OnionProcOparetor\cert\ (คนละโฟลเดอร์กับ DB ที่ Program.cs ใช้อยู่แล้ว
/// แต่ root เดียวกัน) - สร้างครั้งแรกแล้วใช้ซ้ำทุกครั้งที่ Server start ต่อไป ไม่ gen ใหม่ทุกรอบ
/// (ถ้า gen ใหม่ทุกครั้ง thumbprint จะเปลี่ยนทุก restart แล้ว client ที่ pin thumbprint ไว้/ติดตั้ง
/// cert เดิมผ่าน MSI ไปแล้วจะต่อไม่ได้ทันที)
///
/// เป็น self-signed ล้วนๆ ไม่มี CA กลาง - โดยตั้งใจ (ไม่มีงบ/ไม่มี public domain ให้ขอ cert จริงจาก CA
/// สาธารณะ) client ฝั่งที่ไม่เชื่อถือ cert ตัวนี้ (ไม่ได้ลงผ่าน MSI ที่ import เข้า LocalMachine\Root
/// หรือไม่ได้ pin thumbprint ไว้) จะต่อ HTTPS ไม่ได้โดย design - นี่คือพฤติกรรมที่ต้องการ (fail-closed)
/// ไม่ใช่บั๊ก
/// </summary>
public sealed class CertificateManager
{
    private const string CertFileName = "server.pfx";
    private const string PasswordFileName = "server.pfx.pwd";
    private const string PublicCerFileName = "server.cer";
    private const string ThumbprintFileName = "server-thumbprint.txt";

    public X509Certificate2 Certificate { get; }

    /// <summary>SHA-1 thumbprint (รูปแบบ hex ตัวพิมพ์ใหญ่ ไม่มีช่องว่าง) - ใช้ pin บนฝั่ง client ที่เลือกวิธี pin thumbprint แทนการเชื่อถือผ่าน store</summary>
    public string Thumbprint => Certificate.Thumbprint!;

    /// <summary>เนื้อไฟล์ .cer (public key เท่านั้น ไม่มี private key) - แจกให้ client ผ่าน SecurityEndpoints ได้อย่างปลอดภัย</summary>
    public byte[] PublicCertificateBytes { get; }

    private CertificateManager(X509Certificate2 certificate)
    {
        Certificate = certificate;
        PublicCertificateBytes = certificate.Export(X509ContentType.Cert);
    }

    /// <summary>
    /// โหลด cert เดิมจาก certDirectory ถ้ามีและยังไม่หมดอายุ ไม่งั้นสร้างใหม่ให้ - เรียกครั้งเดียวตอน
    /// Server เริ่มทำงาน ก่อน builder.Build() (Kestrel ต้องมี X509Certificate2 พร้อมใช้ตอนตั้งค่า
    /// ListenOptions.UseHttps แล้ว)
    /// </summary>
    public static CertificateManager LoadOrCreate(string certDirectory)
    {
        Directory.CreateDirectory(certDirectory);
        var pfxPath = Path.Combine(certDirectory, CertFileName);
        var pwdPath = Path.Combine(certDirectory, PasswordFileName);
        var cerPath = Path.Combine(certDirectory, PublicCerFileName);
        var thumbprintPath = Path.Combine(certDirectory, ThumbprintFileName);

        if (File.Exists(pfxPath) && File.Exists(pwdPath))
        {
            try
            {
                var password = File.ReadAllText(pwdPath);
                var existing = X509CertificateLoader.LoadPkcs12FromFile(
                    pfxPath, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                // หมดอายุแล้ว (เกิน 10 ปีตามที่ CreateSelfSigned ตั้งไว้) - สร้างใหม่แทนปล่อยให้ client
                // ต่อ HTTPS ไม่ได้แบบเงียบๆ (Kestrel จะปฏิเสธ handshake เองถ้า cert หมดอายุ)
                if (existing.NotAfter > DateTime.Now)
                {
                    StartupDiagnostics.WriteLine(
                        $"[CertificateManager] โหลด cert เดิมสำเร็จ (Thumbprint: {existing.Thumbprint}, หมดอายุ: {existing.NotAfter:O})");
                    return Finish(existing, cerPath, thumbprintPath);
                }

                StartupDiagnostics.WriteLine(
                    $"[CertificateManager] cert เดิมหมดอายุแล้ว ({existing.NotAfter:O}) - จะสร้างใหม่");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteLine($"[CertificateManager] โหลด cert เดิมไม่สำเร็จ ({ex.Message}) - จะสร้างใหม่");
            }
        }

        var created = CreateSelfSigned();
        var newPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        File.WriteAllBytes(pfxPath, created.Export(X509ContentType.Pfx, newPassword));
        File.WriteAllText(pwdPath, newPassword);
        StartupDiagnostics.WriteLine($"[CertificateManager] สร้าง self-signed certificate ใหม่ (Thumbprint: {created.Thumbprint})");

        return Finish(created, cerPath, thumbprintPath);
    }

    /// <summary>เขียน .cer + thumbprint ไฟล์คู่กันไว้เสมอ (idempotent) - เผื่อไฟล์เหล่านี้หายไป (ลบมือ/ backup ไม่ครบ) ทั้งที่ .pfx ยังอยู่</summary>
    private static CertificateManager Finish(X509Certificate2 cert, string cerPath, string thumbprintPath)
    {
        var manager = new CertificateManager(cert);
        try
        {
            File.WriteAllBytes(cerPath, manager.PublicCertificateBytes);
            File.WriteAllText(thumbprintPath, manager.Thumbprint);
        }
        catch (Exception ex)
        {
            // ไม่ hard-fail: Kestrel ยังฟัง HTTPS ได้ปกติแม้เขียนไฟล์สำรองพวกนี้ไม่สำเร็จ (เช่นสิทธิ์ไฟล์
            // ผิดปกติ) แค่ client ที่พึ่งไฟล์ .cer/thumbprint นี้ (pin thumbprint, OnlineConsole บนเครื่องเดียวกัน)
            // จะไม่เจอจนกว่าจะแก้
            StartupDiagnostics.WriteLine($"[CertificateManager] เขียนไฟล์ .cer/thumbprint สำรองไม่สำเร็จ (ไม่กระทบการฟัง HTTPS): {ex.Message}");
        }
        return manager;
    }

    private static X509Certificate2 CreateSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false)); // Server Authentication

        // SAN ครอบทั้ง hostname, localhost และ IP ทุกใบที่เครื่องนี้มี - Console/Agent ต่อด้วย IP ตรงๆ
        // กันเป็นหลัก (ไม่ใช่ DNS name) ถ้าไม่มี SAN ที่ตรงกับ IP ที่ client ใช้จริง TLS handshake จะ
        // ปฏิเสธด้วยเหตุผลคนละอย่าง (ชื่อไม่ตรง) ทั้งที่ thumbprint/trust ถูกต้องแล้ว - สับสนตอน debug
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Environment.MachineName))
            {
                sanBuilder.AddIpAddress(ip);
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteLine($"[CertificateManager] หา IP ของเครื่องนี้ไม่สำเร็จตอนสร้าง cert (ไม่ hard-fail): {ex.Message}");
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.Now.AddDays(-1);
        var notAfter = DateTimeOffset.Now.AddYears(10);
        using var selfSigned = request.CreateSelfSigned(notBefore, notAfter);

        // ต้อง export แล้วโหลดกลับเป็น PFX ก่อนใช้งานจริง - cert ที่ได้ตรงจาก CreateSelfSigned() ถือ
        // private key แบบ ephemeral ที่ Kestrel/การ persist ข้าม process ไม่รองรับตรงๆ ในหลายเวอร์ชันของ
        // .NET (pattern ที่เอกสาร .NET แนะนำสำหรับ self-signed cert ที่ต้องเก็บไว้ใช้ข้ามรอบ start)
        return X509CertificateLoader.LoadPkcs12(
            selfSigned.Export(X509ContentType.Pfx), password: null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }
}
