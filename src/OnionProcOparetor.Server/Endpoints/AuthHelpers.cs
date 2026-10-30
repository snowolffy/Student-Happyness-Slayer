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
}
