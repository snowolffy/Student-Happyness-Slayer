using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

public enum AgentAuthResult
{
    /// <summary>secret ตรงกับที่ enroll ไว้</summary>
    Valid,

    /// <summary>เครื่องนี้ยังไม่เคย enroll - เพิ่งบันทึก secret ที่ส่งมาเป็นของจริง (trust-on-first-use)</summary>
    Enrolled,

    /// <summary>ไม่ได้ส่ง secret มาเลย - agent เวอร์ชันเก่าที่ยังไม่ได้อัปเกรด</summary>
    NoSecretProvided,

    /// <summary>ส่ง secret มาแต่ไม่ตรงกับที่ enroll ไว้ - เป็นได้ทั้งการปลอมตัว และเครื่องที่ถูกลง Windows ใหม่</summary>
    Mismatch
}

/// <summary>
/// ตรวจ agent secret ที่แนบมากับ request/hub connection ฝั่ง agent (2026-08-31, จาก security audit)
///
/// โหมดการทำงานคุมด้วย config "Security:RequireAgentSecret":
///
///   false (default) = โหมดสังเกตการณ์ - ยังปล่อยผ่านทุกกรณี แต่เขียน Activity Log ทุกครั้งที่เจอ
///                     Mismatch หรือ NoSecretProvided ใช้ช่วง rollout: ทยอยอัปเดต Agent ทีละเครื่อง
///                     จนกว่า "เครื่องที่ยังไม่ enroll" จะเหลือศูนย์ โดยที่ห้องยังใช้งานได้ปกติ
///
///   true            = บังคับใช้ - Mismatch/NoSecretProvided ถูกปฏิเสธ 401
///
/// จงใจไม่ default เป็น true: ถ้าเปิดบังคับพร้อมกับที่ deploy build นี้ เครื่องทั้ง 40 ที่ยังรัน
/// Agent เวอร์ชันเก่าจะหลุดจากระบบพร้อมกันทันทีในวินาทีที่ Server restart และ enforcement จะค้างอยู่
/// ที่กฎชุดสุดท้ายไปเรื่อยๆ โดยที่ครูมองไม่เห็นว่าเกิดอะไรขึ้น - ความเสียหายจากการเปิดผิดจังหวะ
/// สูงกว่าประโยชน์ของการบังคับเร็วขึ้นไม่กี่วัน วิธีใช้ที่ถูกคือ deploy -> ดู Activity Log จนนิ่ง ->
/// ค่อยตั้ง true แล้ว restart service
/// </summary>
public sealed class AgentAuthenticator
{
    /// <summary>ต้องตรงกับ AgentCredentials.SecretHeaderName ฝั่ง Agent เป๊ะๆ</summary>
    public const string SecretHeaderName = "X-Agent-Secret";

    /// <summary>ต้องตรงกับ AgentCredentials.GuidHeaderName ฝั่ง Agent เป๊ะๆ</summary>
    public const string GuidHeaderName = "X-Agent-Guid";

    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentAuthenticator> _logger;

    public AgentAuthenticator(IConfiguration configuration, ILogger<AgentAuthenticator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>true = ปฏิเสธ request ที่ตรวจไม่ผ่าน (ไม่ใช่แค่ log)</summary>
    public bool IsEnforcing => _configuration.GetValue("Security:RequireAgentSecret", false);

    /// <summary>
    /// ตรวจ secret ของเครื่องหนึ่ง และ enroll ให้อัตโนมัติถ้ายังไม่เคยมี
    /// ผู้เรียกต้อง SaveChangesAsync() เอง (ปกติจะบันทึกพร้อมกับการอัปเดต LastSeenAt อยู่แล้ว)
    /// </summary>
    public AgentAuthResult Authenticate(ClientMachine client, string? providedSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            return AgentAuthResult.NoSecretProvided;
        }

        if (client.AgentSecretHash is null || client.AgentSecretSalt is null)
        {
            var salt = PasswordHasher.GenerateSalt();
            client.AgentSecretSalt = salt;
            client.AgentSecretHash = PasswordHasher.HashPassword(providedSecret, salt);
            client.AgentSecretEnrolledAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Enroll agent secret ให้เครื่อง '{MachineName}' ({ClientGuid}) เป็นครั้งแรก",
                client.MachineName, client.ClientGuid);

            return AgentAuthResult.Enrolled;
        }

        // PasswordHasher ใช้ PBKDF2 + FixedTimeEquals อยู่แล้ว (ดู Services/PasswordHasher.cs)
        // ยอมจ่าย cost ของ PBKDF2 ต่อ poll ทุกครั้ง: 40 เครื่อง x ทุก 30 วิ = ~1.3 ครั้ง/วินาที
        // ซึ่งไม่มีนัยสำคัญเทียบกับเครื่อง server ทั่วไป และได้ความสม่ำเสมอกับ auth ฝั่ง admin
        return PasswordHasher.VerifyPassword(providedSecret, client.AgentSecretSalt, client.AgentSecretHash)
            ? AgentAuthResult.Valid
            : AgentAuthResult.Mismatch;
    }

    /// <summary>
    /// ตรวจแล้วตัดสินว่าควรปฏิเสธ request นี้ไหม พร้อมเขียน Activity Log เมื่อเจอสิ่งผิดปกติ
    /// คืน true = ปล่อยผ่าน, false = ต้องตอบ 401
    /// </summary>
    public async Task<bool> AuthorizeAsync(
        ClientMachine client,
        string? providedSecret,
        IActivityLogService activityLog,
        string endpointName)
    {
        var result = Authenticate(client, providedSecret);

        switch (result)
        {
            case AgentAuthResult.Valid:
            case AgentAuthResult.Enrolled:
                return true;

            case AgentAuthResult.NoSecretProvided:
                _logger.LogWarning(
                    "{Endpoint}: เครื่อง '{MachineName}' ({ClientGuid}) ไม่ได้ส่ง agent secret มา (agent เวอร์ชันเก่า?) - โหมดบังคับใช้: {Enforcing}",
                    endpointName, client.MachineName, client.ClientGuid, IsEnforcing);
                return !IsEnforcing;

            case AgentAuthResult.Mismatch:
            default:
                // เขียน Activity Log ทุกครั้งแม้ยังไม่บังคับใช้ - นี่คือสัญญาณเดียวที่ครูจะเห็นว่ามีคน
                // พยายามปลอมตัวเป็นเครื่องอื่น ถ้าไม่บันทึกไว้ก็เท่ากับตรวจเจอแล้วทิ้งข้อมูลทิ้งเปล่าๆ
                await activityLog.LogAgentConnectivityAsync(
                    "AgentSecretMismatch",
                    $"มี request มาที่ {endpointName} โดยอ้างเป็นเครื่อง '{client.MachineName}' แต่ agent secret ไม่ตรงกับที่ลงทะเบียนไว้"
                        + (IsEnforcing ? " - ถูกปฏิเสธ" : " - ยังไม่ปฏิเสธเพราะ Security:RequireAgentSecret ยังเป็น false"),
                    client.ClientGuid,
                    client.MachineName);

                _logger.LogWarning(
                    "{Endpoint}: agent secret ของเครื่อง '{MachineName}' ({ClientGuid}) ไม่ตรง - โหมดบังคับใช้: {Enforcing}",
                    endpointName, client.MachineName, client.ClientGuid, IsEnforcing);

                return !IsEnforcing;
        }
    }

    /// <summary>
    /// หา ClientMachine จาก header X-Agent-Guid แล้ว authorize - ใช้กับ endpoint ที่ไม่มี clientGuid
    /// ใน URL (ack/result)
    ///
    /// Allowed=false แปลว่าต้องตอบ 401 ทันที
    /// Allowed=true แต่ Client=null แปลว่า "agent เวอร์ชันเก่าที่ไม่ส่ง header มาเลย และตอนนี้ยังไม่
    /// บังคับใช้" - ผู้เรียกต้องข้ามการตรวจ ownership ของ command ในกรณีนั้น (ไม่มีข้อมูลให้ตรวจ)
    /// แยกเป็น 2 ค่าแทนที่จะยัดใส่ค่าเดียว เพราะ "ผ่านแบบไม่รู้ว่าใคร" กับ "ผ่านและรู้ว่าเป็นเครื่องไหน"
    /// นำไปสู่การตัดสินใจที่ต่างกันจริงๆ ที่ call site
    /// </summary>
    public async Task<(bool Allowed, ClientMachine? Client)> AuthorizeByHeadersAsync(
        HttpRequest request,
        AppDbContext db,
        IActivityLogService activityLog,
        string endpointName)
    {
        var guid = request.Headers[GuidHeaderName].ToString();
        var secret = request.Headers[SecretHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(guid))
        {
            if (IsEnforcing)
            {
                _logger.LogWarning("{Endpoint}: ไม่มี header X-Agent-Guid และอยู่ในโหมดบังคับใช้ - ปฏิเสธ", endpointName);
                return (false, null);
            }

            return (true, null);
        }

        var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == guid);
        if (client is null)
        {
            // อ้าง GUID ที่ไม่มีในระบบ - ไม่ใช่ agent เก่า แต่เป็นคนที่ตั้งใจยิงเข้ามา ปฏิเสธเสมอ
            // ไม่ว่าจะบังคับใช้อยู่หรือไม่ (ไม่มี legacy behavior ไหนที่ต้องรักษาไว้ตรงนี้)
            _logger.LogWarning("{Endpoint}: อ้าง ClientGuid '{Guid}' ที่ไม่มีอยู่จริงใน DB", endpointName, guid);
            return (false, null);
        }

        var allowed = await AuthorizeAsync(client, secret, activityLog, endpointName);
        return (allowed, allowed ? client : null);
    }
}
