using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>Website Control: CRUD + bulk ของ WebsiteRule</summary>
public static class WebsiteRuleEndpoints
{
    public static WebApplication MapWebsiteRuleEndpoints(this WebApplication app)
    {
        // ---- Website Control endpoints (2026-09-11) ----
        // mirror ของชุด /api/rules ข้างบนทุกอย่าง (auth, error shape, activity log) ต่างกันแค่ field ของ
        // model - ดู Models/WebsiteRule.cs เรื่องที่ไม่มี override ต่อเครื่องในรอบแรก

        // ค้นหา + กรองตาม catalog source + แบ่งหน้า (2026-09-12) - ตั้งใจให้ Website Control ใช้ catalog
        // sync ได้เต็มที่ (ไม่ตัด cap ที่ UI ให้ต่ำเกินไป) แล้วแก้ปัญหา "รายการเยอะ" ด้วยการแบ่งหน้า/ค้นหา/
        // กรองแทน เดินตาม pattern เดียวกับ GET /api/activitylog (page/pageSize + totalCount)
        app.MapGet("/api/website-rules", async (
            HttpRequest request, AppDbContext db,
            string? search, int? sourceCatalogId, bool manualOnly = false,
            int page = 1, int pageSize = 100) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var query = db.WebsiteRules.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(r => r.Name.Contains(search) || r.UrlPattern.Contains(search));
            }

            if (manualOnly)
            {
                query = query.Where(r => r.SourceCatalogId == null);
            }
            else if (sourceCatalogId is not null)
            {
                query = query.Where(r => r.SourceCatalogId == sourceCatalogId);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(r => r.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new { items, totalCount, page, pageSize });
        });

        app.MapPost("/api/website-rules", async (WebsiteRule rule, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // WP4: กฎบล็อกเว็บถาวรเป็นสิทธิ์ Admin เท่านั้นเหมือน /api/rules
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            // WP2-3: RoomId ต้องเป็น null (ทุกห้อง) หรืออ้างถึงห้องที่มีอยู่จริงเท่านั้น
            if (rule.RoomId is not null && !await db.Rooms.AnyAsync(r => r.Id == rule.RoomId))
                return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });

            rule.CreatedAt = DateTime.UtcNow;
            rule.UpdatedAt = DateTime.UtcNow;
            db.WebsiteRules.Add(rule);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteRuleCreated", $"{admin.Username} สร้างกฎบล็อกเว็บ '{rule.Name}'");

            return Results.Created($"/api/website-rules/{rule.Id}", rule);
        });

        app.MapDelete("/api/website-rules/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.WebsiteRules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            db.WebsiteRules.Remove(rule);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteRuleDeleted", $"{admin.Username} ลบกฎบล็อกเว็บ '{rule.Name}'");

            return Results.Ok(new { message = "Website rule deleted." });
        });

        app.MapPut("/api/website-rules/{id:int}", async (int id, WebsiteRule updated, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.WebsiteRules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            // WP2-3: RoomId ต้องเป็น null (ทุกห้อง) หรืออ้างถึงห้องที่มีอยู่จริงเท่านั้น
            if (updated.RoomId is not null && !await db.Rooms.AnyAsync(r => r.Id == updated.RoomId))
                return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });

            rule.Name = updated.Name;
            rule.UrlPattern = updated.UrlPattern;
            rule.IsEnabled = updated.IsEnabled;
            rule.RoomId = updated.RoomId;
            rule.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteRuleUpdated", $"{admin.Username} แก้ไขกฎบล็อกเว็บ '{rule.Name}'");

            return Results.Ok(rule);
        });

        app.MapPost("/api/website-rules/{id:int}/toggle", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var rule = await db.WebsiteRules.FindAsync(id);
            if (rule is null) return Results.NotFound();

            rule.IsEnabled = !rule.IsEnabled;
            rule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteRuleToggled",
                $"{admin.Username} {(rule.IsEnabled ? "เปิด" : "ปิด")} กฎบล็อกเว็บ '{rule.Name}'");

            return Results.Ok(rule);
        });

        // เพิ่มกฎบล็อกเว็บทีเดียวหลายรายการ (2026-09-12) - ครูวางรายชื่อโดเมนทีละบรรทัดแทนกด "เพิ่มกฎ" ทีละอัน
        // Name = UrlPattern เสมอสำหรับทางเข้านี้ (ไม่มีช่องชื่อแยกในฟอร์ม paste) กันซ้ำทั้งกับของเดิมในระบบและ
        // ภายในชุดที่วางมาเอง เดินตาม pattern การจำกัดจำนวนต่อ request แบบเดียวกับ /api/classify
        app.MapPost("/api/website-rules/bulk", async (AddWebsiteRulesBulkRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (req.UrlPatterns is null || req.UrlPatterns.Count == 0)
            {
                return Results.Ok(new { added = 0, skipped = 0 });
            }

            const int MaxBulkRulesPerRequest = 200;

            var existing = await db.WebsiteRules
                .Select(r => r.UrlPattern)
                .ToListAsync();
            var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var added = 0;
            var skipped = 0;

            foreach (var raw in req.UrlPatterns.Take(MaxBulkRulesPerRequest))
            {
                var pattern = raw?.Trim();
                if (string.IsNullOrEmpty(pattern) || !seen.Add(pattern))
                {
                    skipped++;
                    continue;
                }

                db.WebsiteRules.Add(new WebsiteRule
                {
                    Name = pattern,
                    UrlPattern = pattern,
                    IsEnabled = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                added++;
            }

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteRuleBulkAdded",
                $"{admin.Username} เพิ่มกฎบล็อกเว็บ {added} รายการ (ข้าม {skipped} รายการซ้ำ)");

            return Results.Ok(new { added, skipped });
        });

        return app;
    }
}
