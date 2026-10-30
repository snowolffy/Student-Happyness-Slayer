using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>ตัวช่วยร่วมของ endpoint ทุกกลุ่ม</summary>
public static class AuthHelpers
{
    // ---- Helper: เช็คว่า request มี token ที่ valid ไหม คืน AdminUser ถ้า valid, null ถ้าไม่ ----
    public static async Task<AdminUser?> GetAuthorizedAdmin(HttpRequest request, AppDbContext db)
    {
        if (!request.Headers.TryGetValue("X-Auth-Token", out var tokenValue))
        {
            return null;
        }

        var token = await db.AuthTokens
            .FirstOrDefaultAsync(t => t.Token == tokenValue.ToString() && t.ExpiresAt > DateTime.UtcNow);

        if (token is null)
        {
            return null;
        }

        return await db.AdminUsers.FindAsync(token.AdminUserId);
    }

    // ---- Helper: บังคับว่า endpoint นี้ Admin เท่านั้น (WP4, 2026-10-30) ----
    // Teacher ทำได้แค่งานประจำวัน (คุมห้อง/ดูจอ/โหมดสอบ/รีเซ็ตรหัสนักเรียน/Staff session) ตามตาราง
    // Admin/Teacher ใน docs/roadmap.md ส่วนการแก้กฎถาวร, ผู้ใช้, ห้อง และตั้งค่าระบบเป็นของ Admin เท่านั้น
    //
    // เรียกหลังผ่าน GetAuthorizedAdmin + เช็ค HasDefaultPassword แล้วเสมอ (pattern เดียวกับ 2 เช็คนั้น):
    //   var admin = await GetAuthorizedAdmin(request, db);
    //   if (admin is null) return Results.Unauthorized();
    //   if (admin.HasDefaultPassword) return Results.Json(..., statusCode: 403);
    //   if (RequireAdmin(admin) is { } forbidden) return forbidden;
    // คืน null ถ้าผ่าน (เป็น Admin), คืน IResult แบบ 403 ถ้าไม่ผ่าน (เป็น Teacher)
    public static IResult? RequireAdmin(AdminUser admin) =>
        admin.Role == AdminRole.Admin
            ? null
            : Results.Json(
                new { error = "Teacher accounts do not have permission to perform this action." },
                statusCode: StatusCodes.Status403Forbidden);
}
