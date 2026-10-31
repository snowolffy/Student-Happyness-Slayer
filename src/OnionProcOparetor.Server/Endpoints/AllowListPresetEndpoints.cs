using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>
/// ชุด Allow-list สำเร็จรูป/กำหนดเอง ของ "โหมดสอบ" (WP2-2, 2026-10-31) - ดู Models/AllowListPreset.cs
/// GET เปิดให้ Teacher อ่านได้ด้วย (ต้องเห็นรายชื่อชุดตอนเปิดโหมดสอบ - ดู ExamSessionEndpoints)
/// ส่วนแก้ไข/ลบ/เพิ่มชุดและโดเมนเป็น Admin เท่านั้น เหมือน WebsiteRule/Rule ("Admin แก้ที่นี่ ครูแค่กดเลือกชุด")
/// </summary>
public static class AllowListPresetEndpoints
{
    public static WebApplication MapAllowListPresetEndpoints(this WebApplication app)
    {
        // คืนทุกชุดพร้อมโดเมนของตัวเอง (ไม่แบ่งหน้า - จำนวนชุดน้อยมาก ไม่เหมือน WebsiteRule ที่เป็น
        // พันๆ แถวจาก catalog sync) หน้า "ชุด Allow-list" และหน้า "เปิดโหมดสอบ" ใช้ endpoint เดียวกันนี้
        app.MapGet("/api/allowlist-presets", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var presets = await db.AllowListPresets.OrderBy(p => p.Name).ToListAsync();
            var domainsByPreset = await db.AllowListPresetDomains.ToListAsync();

            var result = presets.Select(p => new
            {
                p.Id,
                p.Name,
                p.IsBuiltIn,
                p.CreatedAt,
                p.UpdatedAt,
                Domains = domainsByPreset
                    .Where(d => d.PresetId == p.Id)
                    .OrderBy(d => d.Domain)
                    .Select(d => new { d.Id, d.Domain })
                    .ToList()
            });

            return Results.Ok(result);
        });

        app.MapPost("/api/allowlist-presets", async (AllowListPresetRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "กรุณาระบุชื่อชุด" });

            var now = DateTime.UtcNow;
            var preset = new AllowListPreset { Name = req.Name.Trim(), IsBuiltIn = false, CreatedAt = now, UpdatedAt = now };
            db.AllowListPresets.Add(preset);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetCreated", $"{admin.Username} สร้างชุด Allow-list '{preset.Name}'");

            return Results.Created($"/api/allowlist-presets/{preset.Id}", preset);
        });

        app.MapPut("/api/allowlist-presets/{id:int}", async (int id, AllowListPresetRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var preset = await db.AllowListPresets.FindAsync(id);
            if (preset is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "กรุณาระบุชื่อชุด" });

            preset.Name = req.Name.Trim();
            preset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetUpdated", $"{admin.Username} เปลี่ยนชื่อชุด Allow-list เป็น '{preset.Name}'");

            return Results.Ok(preset);
        });

        app.MapDelete("/api/allowlist-presets/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var preset = await db.AllowListPresets.FindAsync(id);
            if (preset is null) return Results.NotFound();

            var domains = await db.AllowListPresetDomains.Where(d => d.PresetId == id).ToListAsync();
            db.AllowListPresetDomains.RemoveRange(domains);
            db.AllowListPresets.Remove(preset);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetDeleted", $"{admin.Username} ลบชุด Allow-list '{preset.Name}'");

            return Results.Ok(new { message = "Allow-list preset deleted." });
        });

        // "ทำสำเนา" (ดู wireframe con-website-allow.dc.html) - ชื่อใหม่ต่อท้ายด้วย " (สำเนา)" โดเมนก๊อปมาทั้งชุด
        app.MapPost("/api/allowlist-presets/{id:int}/duplicate", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var source = await db.AllowListPresets.FindAsync(id);
            if (source is null) return Results.NotFound();

            var now = DateTime.UtcNow;
            var copy = new AllowListPreset { Name = $"{source.Name} (สำเนา)", IsBuiltIn = false, CreatedAt = now, UpdatedAt = now };
            db.AllowListPresets.Add(copy);
            await db.SaveChangesAsync();

            var sourceDomains = await db.AllowListPresetDomains.Where(d => d.PresetId == id).ToListAsync();
            foreach (var domain in sourceDomains)
            {
                db.AllowListPresetDomains.Add(new AllowListPresetDomain { PresetId = copy.Id, Domain = domain.Domain });
            }
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetDuplicated", $"{admin.Username} ทำสำเนาชุด Allow-list '{source.Name}' เป็น '{copy.Name}'");

            return Results.Created($"/api/allowlist-presets/{copy.Id}", copy);
        });

        app.MapPost("/api/allowlist-presets/{id:int}/domains", async (int id, AddDomainRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var preset = await db.AllowListPresets.FindAsync(id);
            if (preset is null) return Results.NotFound();

            var domain = req.Domain?.Trim();
            if (string.IsNullOrEmpty(domain))
                return Results.BadRequest(new { error = "กรุณาระบุโดเมน" });

            var exists = await db.AllowListPresetDomains
                .AnyAsync(d => d.PresetId == id && d.Domain.ToLower() == domain.ToLower());
            if (exists)
                return Results.BadRequest(new { error = "โดเมนนี้มีอยู่ในชุดนี้แล้ว" });

            var row = new AllowListPresetDomain { PresetId = id, Domain = domain };
            db.AllowListPresetDomains.Add(row);
            preset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetDomainAdded", $"{admin.Username} เพิ่มโดเมน '{domain}' เข้าชุด '{preset.Name}'");

            return Results.Created($"/api/allowlist-presets/{id}/domains/{row.Id}", row);
        });

        app.MapDelete("/api/allowlist-presets/{id:int}/domains/{domainId:int}", async (int id, int domainId, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var preset = await db.AllowListPresets.FindAsync(id);
            if (preset is null) return Results.NotFound();

            var domain = await db.AllowListPresetDomains.FirstOrDefaultAsync(d => d.Id == domainId && d.PresetId == id);
            if (domain is null) return Results.NotFound();

            db.AllowListPresetDomains.Remove(domain);
            preset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "AllowListPresetDomainRemoved", $"{admin.Username} ลบโดเมน '{domain.Domain}' จากชุด '{preset.Name}'");

            return Results.Ok(new { message = "Domain removed." });
        });

        return app;
    }

    /// <summary>คืนรายการโดเมนรวมของชุด preset Id เหล่านี้ (เอาเฉพาะที่มีอยู่จริง ไม่ throw ถ้า id ไม่มี) -
    /// ใช้ร่วมกันทั้ง ExamSessionEndpoints (ตอนเริ่มโหมดสอบ) และ PollEndpoints (ตอนประกอบ allow-list ส่ง Agent)</summary>
    public static async Task<List<string>> GetDomainsForPresetIdsAsync(AppDbContext db, IEnumerable<int> presetIds)
    {
        var ids = presetIds.ToList();
        if (ids.Count == 0) return new List<string>();

        return await db.AllowListPresetDomains
            .Where(d => ids.Contains(d.PresetId))
            .Select(d => d.Domain)
            .Distinct()
            .ToListAsync();
    }
}
