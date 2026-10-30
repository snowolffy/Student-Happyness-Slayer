using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ Room (WP3, 2026-10-30) ----
// เก็บไว้ในไฟล์นี้แทน EndpointRequests.cs เพราะ WP3 ระบุขอบเขตไฟล์ไว้แค่ไฟล์นี้ไฟล์เดียวสำหรับ endpoint ใหม่
record CreateRoomRequest(string Name);
record UpdateRoomRequest(string Name);

/// <summary>
/// ย้ายเครื่องเข้า/ออกห้อง + ตั้งตำแหน่งที่นั่ง - RoomId = null คือเอาเครื่องออกจากห้อง (กลับไปเป็น
/// "ยังไม่จัดห้อง") SeatRow/SeatCol เป็น null ได้เสมอแม้ RoomId ไม่เป็น null (ยังไม่ได้ตั้งตำแหน่งในผัง)
/// </summary>
record AssignClientRoomRequest(int? RoomId, int? SeatRow, int? SeatCol);

/// <summary>ห้องแล็บคอมพิวเตอร์ + ผังที่นั่ง (WP3, 2026-10-30 - migration ลำดับที่ 1 ของขั้น 1)</summary>
public static class RoomEndpoints
{
    public static WebApplication MapRoomEndpoints(this WebApplication app)
    {
        // รายการห้องทั้งหมด พร้อมจำนวนเครื่องในห้องนั้น (ClientCount) - ให้ Console/OnlineConsole ใช้โชว์
        // ที่ตัวเลือกห้องได้โดยไม่ต้องดึง /api/clients มานับเอง
        app.MapGet("/api/rooms", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var rooms = await db.Rooms.OrderBy(r => r.Name).ToListAsync();

            var counts = await db.ClientMachines
                .Where(c => c.RoomId != null)
                .GroupBy(c => c.RoomId)
                .Select(g => new { RoomId = g.Key!.Value, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoomId, x => x.Count);

            var result = rooms.Select(r => new
            {
                r.Id,
                r.Name,
                r.CreatedAt,
                r.UpdatedAt,
                ClientCount = counts.GetValueOrDefault(r.Id)
            });

            return Results.Ok(result);
        });

        app.MapPost("/api/rooms", async (CreateRoomRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // WP4: จัดการห้องเป็นสิทธิ์ Admin เท่านั้น (ตาราง Admin/Teacher ใน docs/roadmap.md)
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return Results.BadRequest(new { error = "ต้องระบุชื่อห้อง" });

            if (await db.Rooms.AnyAsync(r => r.Name == name))
                return Results.Json(new { error = $"มีห้องชื่อ '{name}' อยู่แล้ว" }, statusCode: 409);

            var room = new Room { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RoomCreated", $"{admin.Username} สร้างห้อง '{room.Name}'");

            return Results.Created($"/api/rooms/{room.Id}", room);
        });

        app.MapPut("/api/rooms/{id:int}", async (int id, UpdateRoomRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var room = await db.Rooms.FindAsync(id);
            if (room is null) return Results.NotFound();

            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return Results.BadRequest(new { error = "ต้องระบุชื่อห้อง" });

            if (await db.Rooms.AnyAsync(r => r.Id != id && r.Name == name))
                return Results.Json(new { error = $"มีห้องชื่อ '{name}' อยู่แล้ว" }, statusCode: 409);

            var oldName = room.Name;
            room.Name = name;
            room.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RoomRenamed", $"{admin.Username} เปลี่ยนชื่อห้อง '{oldName}' เป็น '{room.Name}'");

            return Results.Ok(room);
        });

        // ลบห้อง - เครื่องทุกเครื่องที่อยู่ในห้องนี้ย้อนกลับไปเป็น "ยังไม่จัดห้อง" (RoomId/SeatRow/SeatCol = null)
        // ไม่ลบเครื่องทิ้ง - ห้องเป็นแค่ label จัดกลุ่ม ไม่ใช่เจ้าของเครื่อง (เดินตาม pattern เดียวกับที่ลบ
        // WebsiteCatalogSource ไม่ลบ WebsiteRule ที่ผูกอยู่ทั้งหมด แค่ unlink เท่านั้น - ดู WebsiteCatalogEndpoints.cs)
        app.MapDelete("/api/rooms/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var room = await db.Rooms.FindAsync(id);
            if (room is null) return Results.NotFound();

            var affected = await db.ClientMachines.Where(c => c.RoomId == id).ToListAsync();
            foreach (var client in affected)
            {
                client.RoomId = null;
                client.SeatRow = null;
                client.SeatCol = null;
            }

            db.Rooms.Remove(room);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "RoomDeleted",
                $"{admin.Username} ลบห้อง '{room.Name}' ({affected.Count} เครื่องย้อนกลับเป็น 'ยังไม่จัดห้อง')");

            return Results.Ok(new { message = "Room deleted.", unassignedCount = affected.Count });
        });

        // ย้ายเครื่องเข้า/ออกห้อง + ตั้งที่นั่ง - รอบนี้ยังไม่มีหน้าผังที่นั่งเต็มใน Console/OnlineConsole
        // (ตามที่ roadmap ระบุ) endpoint นี้เตรียมไว้ก่อนให้ WP ถัดไปเรียกใช้ได้ทันที
        app.MapPut("/api/clients/{id:int}/room", async (int id, AssignClientRoomRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // ย้ายเครื่องเข้า/ออกห้อง = แก้ "ห้อง" ตามตาราง Admin/Teacher เช่นกัน
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var client = await db.ClientMachines.FindAsync(id);
            if (client is null) return Results.NotFound();

            Room? room = null;
            if (req.RoomId is not null)
            {
                room = await db.Rooms.FindAsync(req.RoomId.Value);
                if (room is null)
                    return Results.BadRequest(new { error = "ไม่พบห้องที่ระบุ" });
            }

            client.RoomId = req.RoomId;
            client.SeatRow = req.SeatRow;
            client.SeatCol = req.SeatCol;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "ClientRoomChanged",
                $"{admin.Username} ย้ายเครื่อง '{client.MachineName}' ไปที่ {(room is not null ? $"ห้อง '{room.Name}'" : "'ยังไม่จัดห้อง'")}",
                client.ClientGuid, client.MachineName);

            return Results.Ok(client);
        });

        return app;
    }
}
