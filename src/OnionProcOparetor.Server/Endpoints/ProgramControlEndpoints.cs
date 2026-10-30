using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>Program Control: classify + program verdicts</summary>
public static class ProgramControlEndpoints
{
    public static WebApplication MapProgramControlEndpoints(this WebApplication app)
    {
        // ======================= Program Control (2026-08-31) =======================
        // Agent ส่ง fingerprint ของโปรแกรมที่ยังไม่รู้จักมาให้จำแนก แล้วรับ action กลับไปบังคับใช้
        // ดู Services/ProgramClassifier.cs สำหรับหลักการของอัลกอริทึม และ Models/ProgramVerdict.cs
        // สำหรับเหตุผลที่ key ด้วย SHA-256 แทน path/ชื่อไฟล์
        app.MapPost("/api/classify", async (
            ClassifyRequest req,
            HttpRequest httpRequest,
            AppDbContext db,
            AgentAuthenticator agentAuth,
            IActivityLogService activityLog,
            ProgramVerdictService verdictService,
            CancellationToken ct) =>
        {
            var (allowed, client) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/classify");
            if (!allowed)
            {
                return Results.Unauthorized();
            }

            if (req.Programs is null || req.Programs.Count == 0)
            {
                return Results.Ok(new { verdicts = new Dictionary<string, int>() });
            }

            // จำกัดจำนวนต่อ request - Agent ส่งมาเป็นชุดอยู่แล้ว (ดู ProgramControlService ฝั่ง Agent)
            // แต่ endpoint นี้ต้องยืนหยัดเองได้โดยไม่พึ่งว่า client จะประพฤติดี
            const int MaxProgramsPerRequest = 50;
            var programs = req.Programs.Take(MaxProgramsPerRequest).ToList();

            foreach (var program in programs)
            {
                program.MachineName ??= client?.MachineName;
            }

            var verdicts = await verdictService.ResolveAsync(db, programs, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                // ส่งเป็นตัวเลขของ enum - Agent มี enum ชุดเดียวกันฝั่งตัวเอง
                verdicts = verdicts.ToDictionary(kv => kv.Key, kv => (int)kv.Value)
            });
        });

        // คิวตรวจสอบของครู - ค่า default คือ "เฉพาะที่ยังไม่มีคนตัดสิน" เรียงตามความถี่ที่เจอ
        // (filter ใช้ ApplyVerdictFilter ท้ายไฟล์ ร่วมกับ /summary)
        app.MapGet("/api/program-verdicts", async (
            HttpRequest request,
            AppDbContext db,
            string? filter = "flagged",
            int? category = null,
            int take = 200) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var query = ApplyVerdictFilter(db.ProgramVerdicts.AsQueryable(), filter);

            // กรองตามหมวดที่ classifier เดาไว้ (2026-09-01) - ห้องจริงสะสมได้หลายร้อยแถวภายในไม่กี่วัน
            // ครูจึงต้องเลือกดูทีละหมวดได้ (เช่น "อีมูเลเตอร์ทั้งหมด") แทนที่จะไล่อ่านทั้งตาราง
            //
            // ต้องกรองที่นี่ ไม่ใช่ฝั่ง Console: take ตัดจำนวนแถวตั้งแต่ที่ Server แล้ว ถ้าไปกรองทีหลัง
            // หมวดที่ครูอยากดูอาจถูกตัดทิ้งไปหมดแล้วตั้งแต่ก่อนถึง Console (เจอเมื่อมี >200 แถว = กรณีจริง)
            if (category is not null && Enum.IsDefined(typeof(ProgramCategory), category.Value))
            {
                var wanted = (ProgramCategory)category.Value;
                query = query.Where(v => v.Category == wanted);
            }

            var items = await query
                .OrderByDescending(v => v.SightingCount)
                .ThenByDescending(v => v.LastSeenAt)
                .Take(Math.Clamp(take, 1, 1000))
                .ToListAsync();

            return Results.Ok(items);
        });

        // จำนวนแถวแยกตามหมวด สำหรับ filter ที่เลือกอยู่ (2026-09-01)
        //
        // มีไว้เพื่อ 2 อย่างที่หน้าคิวตรวจสอบต้องการจริงๆ: เติมตัวเลขในกล่องเลือกหมวด (ครูจะได้รู้ว่าควร
        // เริ่มดูหมวดไหนก่อนโดยไม่ต้องกดไล่ทีละหมวด) และบอกยอดรวมจริงเพื่อเตือนเมื่อรายการถูก take ตัด
        app.MapGet("/api/program-verdicts/summary", async (
            HttpRequest request,
            AppDbContext db,
            string? filter = "flagged") =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var counts = await ApplyVerdictFilter(db.ProgramVerdicts.AsQueryable(), filter)
                .GroupBy(v => v.Category)
                .Select(g => new { category = (int)g.Key, count = g.Count() })
                .ToListAsync();

            return Results.Ok(new
            {
                total = counts.Sum(c => c.count),
                categories = counts.OrderByDescending(c => c.count).ToList()
            });
        });

        // ครูเพิ่มโปรแกรมเข้าระบบเองจากรายการ "โปรแกรมที่กำลังรันอยู่" (2026-08-31)
        //
        // ต่างจาก /decide ที่ต้องมีแถวอยู่ก่อนแล้ว - อันนี้ upsert: ถ้า Program Control ยังไม่เคยเห็น
        // โปรแกรมนี้ก็สร้างแถวใหม่ให้เลย ถ้าเคยเห็นแล้ว (ไม่ว่าอัลกอริทึมจะตัดสินว่าอะไรไว้) ก็เขียนทับ
        //
        // เหตุผลที่ต้องมี: ปุ่ม "CREATE RULE" เดิมสร้างกฎที่ผูกกับชื่อ publisher ซึ่งใช้ไม่ได้เลยกับ
        // โปรแกรมที่ไม่มี publisher - ซึ่งดันเป็นกลุ่มที่ครูอยากบล็อกมากที่สุดพอดี (เกม portable/โหลด)
        // ผูกกับ SHA-256 แทนจึงครอบคลุมทุกกรณี และเปลี่ยนชื่อไฟล์หนีไม่ได้ด้วย
        app.MapPost("/api/program-verdicts/manual", async (
            ManualVerdictRequest req,
            HttpRequest request,
            AppDbContext db,
            IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            if (string.IsNullOrWhiteSpace(req.Sha256) || req.Sha256.Length != 64)
            {
                // ไม่มี hash = ผูกกับอะไรไม่ได้เลย - ฝั่ง Console ปิดปุ่มไว้อยู่แล้วเมื่ออ่านไฟล์ไม่ได้
                // แต่ endpoint ต้องยืนหยัดเองได้โดยไม่พึ่งว่า client จะประพฤติดี
                return Results.BadRequest(new { error = "ต้องมี SHA-256 ของไฟล์ (64 ตัวอักษร) ถึงจะผูกคำตัดสินได้" });
            }

            if (!Enum.IsDefined(typeof(ProgramAction), req.Action))
            {
                return Results.BadRequest(new { error = "Unknown action." });
            }

            var sha = req.Sha256.ToLowerInvariant();
            var action = (ProgramAction)req.Action;
            var verdict = await db.ProgramVerdicts.FirstOrDefaultAsync(v => v.Sha256 == sha);
            var isNew = verdict is null;

            if (verdict is null)
            {
                verdict = new ProgramVerdict
                {
                    Sha256 = sha,
                    FirstSeenAt = DateTime.UtcNow,
                    SightingCount = 1
                };
                db.ProgramVerdicts.Add(verdict);
            }

            // อัปเดตข้อมูลอ่านง่ายให้ทุกครั้ง - แถวที่อัลกอริทึมสร้างไว้ก่อนอาจมีข้อมูลไม่ครบเท่าที่
            // Console เห็นอยู่ตรงหน้า (เช่นชื่อหน้าต่างที่ทำให้ครูจำได้ว่าเป็นโปรแกรมอะไร)
            verdict.FileName = string.IsNullOrWhiteSpace(req.FileName) ? verdict.FileName : req.FileName;
            verdict.ProductName = req.ProductName ?? verdict.ProductName;
            verdict.Publisher = req.Publisher ?? verdict.Publisher;
            verdict.IsSignedPublisher = req.IsSignedPublisher;
            verdict.LastSeenAt = DateTime.UtcNow;
            verdict.LastSeenOnMachine = req.MachineName ?? verdict.LastSeenOnMachine;

            var previous = isNew ? "(ยังไม่เคยเห็น)" : verdict.Action.ToString();
            verdict.Action = action;
            verdict.IsManual = true;
            verdict.DecidedBy = admin.Username;

            if (isNew)
            {
                verdict.Category = ProgramCategory.Unknown;
                verdict.Score = action == ProgramAction.Block ? 100 : 0;
                verdict.ReasonsJson = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { code = "MANUAL", weight = 0, description = $"{admin.Username} เพิ่มเองจากรายการโปรแกรมที่กำลังรันอยู่บนเครื่อง {req.MachineName}" }
                });
            }

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "ProgramVerdictManual",
                $"{admin.Username} ตั้งค่าโปรแกรม '{verdict.FileName}' เป็น {action} ด้วยตนเอง (เดิม: {previous}) - ผูกกับ SHA-256 มีผลทุกเครื่อง",
                null, req.MachineName);

            return Results.Ok(new { verdict.Id, verdict.Sha256, action = (int)verdict.Action, created = isNew });
        });

        // ครูตัดสินด้วยมือ - ทับผลของอัลกอริทึมถาวร (IsManual) มีผลกับทุกเครื่องในรอบ poll ถัดไป
        app.MapPost("/api/program-verdicts/{id:int}/decide", async (
            int id,
            ProgramDecisionRequest req,
            HttpRequest request,
            AppDbContext db,
            IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var verdict = await db.ProgramVerdicts.FindAsync(id);
            if (verdict is null) return Results.NotFound();

            if (!Enum.IsDefined(typeof(ProgramAction), req.Action))
            {
                return Results.BadRequest(new { error = "Unknown action." });
            }

            var previous = verdict.Action;
            verdict.Action = (ProgramAction)req.Action;
            verdict.IsManual = true;
            verdict.DecidedBy = admin.Username;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "ProgramVerdictDecided",
                $"{admin.Username} ตั้งค่าโปรแกรม '{verdict.FileName}' จาก {previous} เป็น {verdict.Action} (ทับผลของอัลกอริทึมถาวร)");

            return Results.Ok(verdict);
        });

        return app;
    }

    // filter ชุดเดียวกันถูกใช้ทั้งที่ /api/program-verdicts และ /summary - แยกออกมาเป็น method
    // เพื่อให้ตัวเลขนับในกล่องเลือกหมวดตรงกับรายการที่แสดงจริงเสมอ ถ้าเขียนซ้ำ 2 ที่แล้ววันหลังแก้ที่เดียว
    // ครูจะเห็น "เกม (12)" แต่พอเลือกแล้วขึ้นมา 8 แถว ซึ่งเป็นบั๊กที่หาสาเหตุยากมากเพราะดูเหมือน UI ค้าง
    private static IQueryable<ProgramVerdict> ApplyVerdictFilter(IQueryable<ProgramVerdict> query, string? filter) => filter switch
    {
        // คิวงานจริงของครู: สิ่งที่อัลกอริทึมไม่มั่นใจพอจะตัดสินเอง และยังไม่มีใครตัดสินให้
        "flagged" => query.Where(v => v.Action == ProgramAction.Flag && !v.IsManual),
        "blocked" => query.Where(v => v.Action == ProgramAction.Block),
        "allowed" => query.Where(v => v.Action == ProgramAction.Allow),
        "manual" => query.Where(v => v.IsManual),
        _ => query
    };
}
