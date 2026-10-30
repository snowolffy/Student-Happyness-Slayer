using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// Token แทน POST /bypass/set ของ Agent ที่เดิมไม่มี auth เลย (WP2, 2026-10-30) - คนที่อยู่บนเครื่อง
/// นักเรียนเดียวกัน (ไม่ว่า user ธรรมดาหรือ process ไหนก็ตาม) ยิง curl ไปที่ 127.0.0.1:8788/bypass/set
/// ตรงๆ ได้มาตลอด เพราะ loopback bind ไม่ได้แปลว่า "มีแค่ AgentTray เรียกได้" (process อื่นบนเครื่อง
/// เดียวกันเรียกได้เหมือนกัน) endpoint คู่นี้ทำให้ Agent ต้องเห็น token ที่ Server ออกให้ก่อนเท่านั้น
/// ถึงจะยอม bypass
///
/// นี่คือฐานของ "Staff session" (ขั้น 3 ของ roadmap - ครู login ที่เครื่องนักเรียนแล้วปิดกฎทั้งเครื่อง
/// ชั่วคราว) แต่ยังไม่ทำ UI/behavior เต็มของ Staff session ในรอบนี้ (ปิดกฎทั้งเครื่อง ต่อเวลา ฯลฯ เป็นงาน
/// ของขั้น Sign-in) ทำแค่กลไกออก/ตรวจ token ผูก clientGuid + บัญชี ให้ BypassCoordinator ของ Agent ใช้
///
/// Token เก็บใน memory ของ Server process เท่านั้น (ดู StaffSessionTokenStore) ไม่ลง DB เพราะ WP2
/// ระบุห้ามสร้าง EF migration ในรอบนี้ - หายเองทุกครั้ง Server restart ซึ่งแค่แปลว่าต้อง login ใหม่ที่
/// AgentTray ไม่ใช่ปัญหาจริง (session อายุสั้นเป็นนาทีอยู่แล้วตามธรรมชาติ)
/// </summary>
public static class StaffSessionEndpoints
{
    private const int DefaultMinutes = 30;
    private const int MaxMinutes = 120;

    public static WebApplication MapStaffSessionEndpoints(this WebApplication app)
    {
        // ครู login ผ่าน AgentTray สำเร็จแล้ว (ใช้บัญชี Console เดิม - X-Auth-Token เดียวกับที่ Console
        // ใช้) ขอ token ผูกกับ clientGuid ของเครื่องนักเรียนเครื่องนี้โดยเฉพาะ - เรียกซ้ำบนเครื่องเดียวกัน
        // ถือเป็นการต่อเวลา (ออก token ใหม่ทับของเดิมในเครื่องเดิม) ไม่มี endpoint extend แยกในรอบนี้
        app.MapPost("/api/staff-session/start", async (StaffSessionStartRequest request, HttpRequest httpRequest, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(httpRequest, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            if (string.IsNullOrWhiteSpace(request.ClientGuid))
                return Results.BadRequest(new { error = "ClientGuid ว่างเปล่า" });

            var minutes = request.Minutes is > 0 and <= MaxMinutes ? request.Minutes.Value : DefaultMinutes;
            var duration = TimeSpan.FromMinutes(minutes);

            var token = StaffSessionTokenStore.Issue(request.ClientGuid, admin.Username, duration);
            var expiresAt = DateTime.UtcNow.Add(duration);

            // เครื่องอาจยังไม่เคย poll เข้ามาเลยก็ได้ (ขอ token ได้ก่อน Agent ติดต่อ Server ครั้งแรกเสียอีก) -
            // ใช้ชื่อเครื่องจาก DB ถ้ามี ไม่มีก็ log เป็น clientGuid ตรงๆ แทน ไม่ hard-fail เพราะ audit log
            // ยังมีประโยชน์แม้ไม่รู้ชื่อเครื่อง
            var machineName = (await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == request.ClientGuid))?.MachineName
                ?? request.ClientGuid;

            await activityLog.LogAdminActionAsync(
                admin,
                "StaffSessionStarted",
                $"{admin.Username} ขอ bypass token (Staff session) ที่เครื่อง '{machineName}' ({minutes} นาที)",
                request.ClientGuid,
                machineName);

            return Results.Ok(new { token, clientGuid = request.ClientGuid, expiresAt, minutes });
        });

        // Agent เรียกมาตรวจ token ก่อนยอม bypass จริง (ดู BypassCoordinator.ValidateStaffSessionTokenAsync
        // ฝั่ง Agent) - ไม่ต้องมี X-Auth-Token เพราะ Agent ไม่ใช่บัญชี admin ไม่มี token แบบนั้นเป็นของตัวเอง
        // ความปลอดภัยอยู่ที่ตัว token เอง (สุ่ม 256 บิต จับคู่กับ clientGuid เท่านั้น เดา/ปลอมไม่ได้จริง)
        // ถ้า Server ปิดอยู่หรือ Agent ต่อมาไม่ถึงเลย ฝั่ง Agent จะปฏิเสธ bypass เอง (fail-secure)
        app.MapPost("/api/staff-session/validate", (StaffSessionValidateRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.ClientGuid) || string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.Ok(new { valid = false, expiresAt = (DateTime?)null });
            }

            var (valid, expiresAtUtc) = StaffSessionTokenStore.Validate(request.ClientGuid, request.Token);
            return Results.Ok(new { valid, expiresAt = expiresAtUtc });
        });

        return app;
    }
}

/// <summary>
/// เก็บ bypass token ที่ออกให้แล้วทั้งหมด - key คือ clientGuid (เครื่องหนึ่งมี token "ตัวล่าสุด" ที่ valid
/// ได้ทีละตัวเท่านั้น ออกใหม่ทับของเดิมเสมอ) ConcurrentDictionary เพราะ /start และ /validate มาจาก
/// request คนละตัวพร้อมกันได้ (ครูหลายคนคนละเครื่อง, Agent 40 ตัว validate พร้อมกันได้ในทางทฤษฎี)
///
/// ไม่ลง DB โดยตั้งใจ (WP2 ห้ามสร้าง EF migration) - static field ธรรมดา อยู่ใน memory ของ Server process
/// เดียว หายเองทุก restart ยอมรับได้เพราะ session พวกนี้อายุสั้น (นาที/ชั่วโมง) อยู่แล้วโดยธรรมชาติ
/// </summary>
internal static class StaffSessionTokenStore
{
    private sealed record Entry(string Token, string AdminUsername, DateTime ExpiresAtUtc);

    private static readonly ConcurrentDictionary<string, Entry> TokensByClientGuid = new();

    public static string Issue(string clientGuid, string adminUsername, TimeSpan duration)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        TokensByClientGuid[clientGuid] = new Entry(token, adminUsername, DateTime.UtcNow.Add(duration));
        return token;
    }

    public static (bool Valid, DateTime? ExpiresAtUtc) Validate(string clientGuid, string token)
    {
        if (!TokensByClientGuid.TryGetValue(clientGuid, out var entry))
        {
            return (false, null);
        }

        if (entry.ExpiresAtUtc <= DateTime.UtcNow)
        {
            // หมดอายุแล้ว - ลบทิ้งกันเก็บค้างไม่จำกัดถ้าเครื่องนั้นไม่มาขอ token ใหม่อีกเลย
            TokensByClientGuid.TryRemove(clientGuid, out _);
            return (false, null);
        }

        if (!string.Equals(entry.Token, token, StringComparison.Ordinal))
        {
            return (false, null);
        }

        return (true, entry.ExpiresAtUtc);
    }
}

// ---- Request DTOs ของ WP2 เท่านั้น - เก็บแยกจาก Endpoints/EndpointRequests.cs ของกลุ่มอื่นโดยตั้งใจ
// เพราะ WP2 แก้ไฟล์นี้ไฟล์เดียวฝั่ง Server (ดู docs/roadmap.md หัวข้อ WP2) ----
internal sealed record StaffSessionStartRequest(string ClientGuid, int? Minutes);
internal sealed record StaffSessionValidateRequest(string ClientGuid, string Token);
