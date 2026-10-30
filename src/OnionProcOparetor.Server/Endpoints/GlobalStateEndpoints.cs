using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>สถานะ enforcement ระดับ global (ทั้งห้อง)</summary>
public static class GlobalStateEndpoints
{
    public static WebApplication MapGlobalStateEndpoints(this WebApplication app)
    {
        // ---- สถานะ enforcement ระดับ global (ทั้งห้อง) ----
        //
        // บั๊กจริงที่เจอ (2026-09-04, field test): GlobalState.IsEnabled เป็น AND gate คร่อม enforcement
        // ของ "ทุกเครื่อง" อยู่ (ดู /api/poll: enabled = globalEnabled && client.IsEnabled) แต่ไม่มี
        // endpoint ไหนให้ "อ่าน" ค่านี้ได้เลยตั้งแต่แรก - มีแต่ POST /toggle ที่พลิกค่าแบบมองไม่เห็น
        // ผลคือพอค่านี้กลายเป็น false (กดปุ่มสลับทั้งระบบไปโดยไม่รู้ตัว) อาการที่เห็นคือ Console โชว์
        // เครื่องเป็น ENABLED ตามปกติทุกเครื่อง แต่ Agent ทุกตัวรายงาน "Disabled" ค้างอยู่อย่างนั้น
        // ตลอดไป และการ toggle รายเครื่องกี่รอบก็ไม่มีอะไรเปลี่ยน (คำสั่ง lock/shutdown ยังทำงานปกติ
        // เพราะไม่ได้ผ่าน gate ตัวนี้) = อาการที่หาสาเหตุไม่เจอเลยเพราะไม่มี UI ไหนมองเห็นค่านี้ได้
        //
        // GET นี้คือ "ตาที่หายไป" - Console ทั้งสองตัวอ่านค่าไปแสดงเป็น banner ได้แล้ว
        app.MapGet("/api/global", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            // ไม่ SaveChangesAsync() โดยตั้งใจ - GET ต้องไม่เขียนอะไรลง DB ถ้าแถวหายจริง (ไม่ควรเกิด)
            // ค่าที่ helper สร้างให้จะถูกทิ้งไปพร้อม scoped DbContext ของ request นี้ และคำตอบที่ได้
            // ("เปิดอยู่") ก็ตรงกับที่ /api/poll ตีความในกรณีเดียวกันพอดี
            return Results.Ok(await GetOrCreateGlobalStateAsync(db));
        });

        // ตั้งค่าตรงๆ ว่าจะเปิดหรือปิด (idempotent) - ต่างจาก /toggle ที่พลิกค่าจากของเดิม
        //
        // ทำไมต้องมีทั้งสองแบบ: /toggle ต้องรู้ค่าปัจจุบันก่อนถึงจะรู้ว่ากดแล้วจะได้อะไร ซึ่งเป็นต้นตอ
        // ของบั๊กด้านบน และยังแพ้ lost-update ถ้าครูสองคนกดพร้อมกัน (กดคนละครั้ง = กลับมาที่เดิม)
        // Console ทั้งสองตัวใช้ /set นี้แทนแล้ว ส่วน /toggle เก็บไว้เพื่อ Console เวอร์ชันเก่าที่ deploy
        // ไปแล้วยังใช้ได้ต่อ (ดู endpoint ถัดไป)
        app.MapPost("/api/global/set", async (GlobalStateRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            // WP4: ปิด/เปิด enforcement ทั้งห้องคือ "ตั้งค่าระบบ" - Admin เท่านั้น (Teacher คุมได้แค่รายเครื่อง
            // ผ่าน POST /api/clients/{id}/toggle)
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var state = await GetOrCreateGlobalStateAsync(db);
            var changed = state.IsEnabled != req.Enabled;

            state.IsEnabled = req.Enabled;
            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // เขียน Activity Log เฉพาะตอนที่ค่าเปลี่ยนจริง - การกด "เปิด" ซ้ำบนสิ่งที่เปิดอยู่แล้ว
            // ไม่ใช่เหตุการณ์ที่ต้องบันทึก และจะกลบเหตุการณ์จริงในหน้า log เปล่าๆ
            if (changed)
            {
                await activityLog.LogAdminActionAsync(admin, "GlobalEnforcementToggled",
                    $"{admin.Username} {(state.IsEnabled ? "เปิด" : "ปิด")} enforcement ทั้งห้อง");
            }

            return Results.Ok(state);
        });

        // toggle ทั้งห้อง (global) - คงไว้เพื่อ Console เวอร์ชันเก่า ตัวใหม่ใช้ POST /api/global/set แทน
        app.MapPost("/api/global/toggle", async (HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var state = await GetOrCreateGlobalStateAsync(db);

            state.IsEnabled = !state.IsEnabled;
            state.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "GlobalEnforcementToggled",
                $"{admin.Username} {(state.IsEnabled ? "เปิด" : "ปิด")} enforcement ทั้งห้อง");

            return Results.Ok(state);
        });

        return app;
    }

    /// <summary>
    /// อ่านแถว GlobalState (Id = 1) - สร้างให้ถ้ายังไม่มี ผู้เรียกต้อง SaveChangesAsync() เอง
    /// แถวนี้ถูก seed ไว้ใน migration อยู่แล้ว (ดู AppDbContext.OnModelCreating) การสร้างตรงนี้จึงเป็น
    /// safety net สำหรับ DB ที่ถูกสร้างมาก่อน seed เท่านั้น ไม่ใช่ทางปกติ
    /// </summary>
    private static async Task<GlobalState> GetOrCreateGlobalStateAsync(AppDbContext db)
    {
        var state = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
        if (state is null)
        {
            state = new GlobalState { Id = 1, IsEnabled = true };
            db.GlobalStates.Add(state);
        }

        return state;
    }
}
