using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// แจก public certificate ของ Server เอง (WP1, 2026-10-30) - ไม่มี auth เพราะเนื้อหาเป็น public key
/// เท่านั้น (ไม่มี private key) และต้องให้ client ดึงได้ตั้งแต่ก่อนจะเชื่อถือ HTTPS เสียอีก (เช่น
/// setup-client.ps1 ของ Agent MSI ดึงผ่าน HTTP ตอนย้ายระบบ แล้วเอาไปติดตั้งลง LocalMachine\Root ของ
/// เครื่องนั้น) ตั้งใจเปิดทั้งบน HTTP และ HTTPS listener (ไม่ได้ RequireHost แยก) เพื่อให้ bootstrap ได้
/// ไม่ว่า client จะต่อทางไหนมาก่อน
/// </summary>
public static class SecurityEndpoints
{
    public static WebApplication MapSecurityEndpoints(this WebApplication app)
    {
        app.MapGet("/security/certificate", (CertificateManager certificateManager) =>
            Results.Bytes(
                certificateManager.PublicCertificateBytes,
                contentType: "application/x-x509-ca-cert",
                fileDownloadName: "onionprocoparetor-server.cer"));

        app.MapGet("/security/certificate/thumbprint", (CertificateManager certificateManager) =>
            Results.Text(certificateManager.Thumbprint, "text/plain"));

        return app;
    }
}
