using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>Website Catalog Sources: CRUD + sync-now</summary>
public static class WebsiteCatalogEndpoints
{
    public static WebApplication MapWebsiteCatalogEndpoints(this WebApplication app)
    {
        // ---- Website Catalog Sources (2026-09-12) - sync WebsiteRule จาก list ภายนอกเป็นระยะ ----
        // ดู Services/WebsiteCatalogSyncer.cs สำหรับกลไก fetch/parse/diff เต็ม และ AbsoluteMaxEntries
        // สำหรับเหตุผลที่ต้องมีเพดาน (list สาธารณะบางอันมีโดเมนเป็นแสน ใช้กับ registry policy ไม่ได้จริง)

        app.MapGet("/api/website-catalog-sources", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var sources = await db.WebsiteCatalogSources.OrderBy(s => s.Name).ToListAsync();
            return Results.Ok(sources);
        });

        app.MapPost("/api/website-catalog-sources", async (WebsiteCatalogSource source, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // WP4: catalog source คือการตั้งค่าระบบ (sync config) - Admin เท่านั้น
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            source.Id = 0;
            source.CreatedAt = DateTime.UtcNow;
            source.LastSyncStatus = WebsiteCatalogSyncStatus.NeverRun;
            source.LastSyncedAt = null;
            source.LastSyncEntryCount = null;
            source.LastSyncError = null;
            if (source.MaxEntries <= 0 || source.MaxEntries > WebsiteCatalogSyncer.AbsoluteMaxEntries)
            {
                source.MaxEntries = Math.Min(source.MaxEntries <= 0 ? 1000 : source.MaxEntries, WebsiteCatalogSyncer.AbsoluteMaxEntries);
            }

            db.WebsiteCatalogSources.Add(source);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteCatalogSourceCreated",
                $"{admin.Username} เพิ่ม catalog source '{source.Name}' ({source.SourceUrl})");

            return Results.Created($"/api/website-catalog-sources/{source.Id}", source);
        });

        app.MapPut("/api/website-catalog-sources/{id:int}", async (int id, WebsiteCatalogSource updated, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var source = await db.WebsiteCatalogSources.FindAsync(id);
            if (source is null) return Results.NotFound();

            source.Name = updated.Name;
            source.SourceUrl = updated.SourceUrl;
            source.IsEnabled = updated.IsEnabled;
            source.MaxEntries = Math.Clamp(updated.MaxEntries <= 0 ? 1000 : updated.MaxEntries, 1, WebsiteCatalogSyncer.AbsoluteMaxEntries);

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteCatalogSourceUpdated",
                $"{admin.Username} แก้ไข catalog source '{source.Name}'");

            return Results.Ok(source);
        });

        // ลบ source พร้อม WebsiteRule ทุกแถวที่ผูกกับ source นี้ - กัน orphan (แถวที่ SourceCatalogId ชี้ไป
        // source ที่ไม่มีอยู่แล้ว จะไม่มีทาง sync ใหม่/ลบออกได้อีกเลยถ้าไม่ลบตรงนี้ให้)
        app.MapDelete("/api/website-catalog-sources/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var source = await db.WebsiteCatalogSources.FindAsync(id);
            if (source is null) return Results.NotFound();

            var removedRuleCount = await db.WebsiteRules
                .Where(r => r.SourceCatalogId == id)
                .ExecuteDeleteAsync();

            db.WebsiteCatalogSources.Remove(source);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "WebsiteCatalogSourceDeleted",
                $"{admin.Username} ลบ catalog source '{source.Name}' (พร้อมกฎบล็อกเว็บที่มาจาก source นี้ {removedRuleCount} รายการ)");

            return Results.Ok(new { message = "Catalog source deleted.", removedRuleCount });
        });

        // ให้ครูกด sync ทันทีแทนรอรอบ timer 24 ชม. - เรียก WebsiteCatalogSyncer โค้ดเดียวกับที่ timer ใช้
        app.MapPost("/api/website-catalog-sources/{id:int}/sync-now", async (int id, HttpRequest request, AppDbContext db, WebsiteCatalogSyncer syncer, CancellationToken ct) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var source = await db.WebsiteCatalogSources.FindAsync(id);
            if (source is null) return Results.NotFound();

            await syncer.SyncSourceAsync(db, source, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(source);
        });

        return app;
    }
}
