using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>poll ของ Agent (heartbeat + กฎ + คำสั่งที่ค้าง)</summary>
public static class PollEndpoints
{
    public static WebApplication MapPollEndpoints(this WebApplication app)
    {
        // client เอาไว้ poll เพื่อดึง rules + สถานะ enabled/disabled ของตัวเอง
        // (endpoint สำคัญที่สุดสำหรับฝั่ง Client Service)
        //
        // เดิมไม่มี auth เลย ("ใช้ clientGuid ยืนยันตัวเองแทน") - แต่ clientGuid เดินทางเป็น path segment
        // บน HTTP ธรรมดาทุกๆ 30 วินาที ใครที่อยู่ใน LAN เดียวกันเก็บ GUID ของทุกเครื่องได้ครบแล้วดึงกฎ
        // ทั้งชุดมาดูเพื่อหาทางเลี่ยง ตั้งแต่ 2026-08-31 ตรวจ agent secret เพิ่ม (ดู AgentAuthenticator -
        // โหมดบังคับใช้เปิด/ปิดด้วย config Security:RequireAgentSecret)
        app.MapGet("/api/poll/{clientGuid}", async (string clientGuid, string machineName, HttpContext httpContext, AppDbContext db, IHubContext<LabHub> hub, AgentConnectionRegistry connectionRegistry, AgentAuthenticator agentAuth, IActivityLogService activityLog, bool usingFallback = false, string? blocklistVersion = null) =>
        {
            var client = await db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
            var providedSecret = httpContext.Request.Headers[AgentAuthenticator.SecretHeaderName].ToString();

            // เครื่องที่มีอยู่แล้วต้องตรวจก่อนทำอะไรทั้งสิ้น - รวมถึงก่อนอัปเดต MachineName/LastKnownIp ด้วย
            // (ไม่งั้นคนที่ปลอม GUID จะเขียนทับ LastKnownIp ของเครื่องจริงได้ ซึ่งเป็น IP ที่ Console ใช้ต่อ VNC)
            // เครื่องใหม่ที่ยังไม่มีใน DB ไม่ต้องตรวจ - จะถูก enroll ตอนสร้าง record ด้านล่าง
            if (client is not null &&
                !await agentAuth.AuthorizeAsync(client, providedSecret, activityLog, "GET /api/poll"))
            {
                return Results.Unauthorized();
            }

            // เก็บ IP ล่าสุดที่ client นี้ poll เข้ามา - แต่เดิมเป็น field ที่มีอยู่ใน model/DTO แล้วแต่ไม่เคยถูก
            // set จริงที่ไหนเลย (dead field) จำเป็นต้อง populate ให้ใช้งานได้จริงตอนนี้เพราะ Console ต้องใช้ IP
            // นี้เชื่อม TCP ตรงไปยัง Agent เพื่อทำ RFB handshake (VNC data plane ไม่ผ่าน Server เป็น relay)
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

            if (client is null)
            {
                client = new ClientMachine
                {
                    ClientGuid = clientGuid,
                    MachineName = machineName,
                    IsEnabled = true,
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    LastKnownIp = remoteIp,
                    UsingFallback = usingFallback
                };

                // เครื่องใหม่: จด secret ที่ส่งมาเป็นของจริงทันที (trust-on-first-use) ถ้าไม่ส่งมาก็ปล่อยว่างไว้
                // แล้วจะถูก enroll ในรอบ poll ถัดไปที่ส่งมา - ดู AgentAuthenticator เรื่องช่องที่เหลือของ TOFU
                agentAuth.Authenticate(client, providedSecret);

                db.ClientMachines.Add(client);
            }
            else
            {
                client.LastSeenAt = DateTime.UtcNow;
                client.MachineName = machineName;
                client.LastKnownIp = remoteIp;
                client.UsingFallback = usingFallback;
            }

            var globalState = await db.GlobalStates.FirstOrDefaultAsync(g => g.Id == 1);
            var globalEnabled = globalState?.IsEnabled ?? true;
            // หมายเหตุ: ตรงนี้จงใจไม่เรียก GetOrCreateGlobalStateAsync - poll เป็นเส้นทางที่ agent 40 เครื่อง
            // วิ่งเข้ามาพร้อมกัน ไม่ควรมีการ INSERT แข่งกัน ถ้าแถวหาย (ไม่ควรเกิด - มี seed ใน migration)
            // ให้ถือว่า "เปิด" ไว้ก่อน = fail-open เฉพาะ gate ตัวนี้ ซึ่งถูกกว่าการปิดทั้งห้องเพราะแถวหาย

            // Effective rule list เฉพาะเครื่องนี้: เริ่มจาก global IsEnabled ของแต่ละกฎ แล้วให้ RuleOverride
            // ของ clientGuid นี้ (ถ้ามี) ชนะเสมอ 100% ไม่มี merge/priority ซับซ้อน - เครื่องอื่นไม่เห็นผลอะไรเลย
            // เพราะ query filter ด้วย ClientGuid นี้เท่านั้น ผลลัพธ์ยังเป็น List<Rule> รูปแบบเดิมทุกอย่าง
            // (Agent ฝั่งรับไม่ต้องรู้เรื่อง override เลย - แค่ apply list ที่ได้เหมือนเดิม)
            // กฎรายห้อง (WP2-3, 2026-10-31) - RoomId == null คือ "ใช้กับทุกห้อง" ผ่านด่านนี้เสมอไม่ว่าเครื่อง
            // จะอยู่ห้องไหน ส่วน RoomId ที่เจาะจงต้องตรงกับ client.RoomId เป๊ะ ๆ เท่านั้น ถ้าเครื่องนี้ยังไม่
            // จัดห้อง (client.RoomId == null) เงื่อนไขหลังจะกลาย เป็น r.RoomId == null ซ้ำกับเงื่อนไขแรกพอดี
            // ผลคือเครื่องที่ยังไม่จัดห้องเห็นแค่กฎที่ใช้กับทุกห้องเท่านั้น ไม่เห็นกฎของห้องใดห้องหนึ่งเลย -
            // ตรงตามเกณฑ์เสร็จของ WP2-3 ("เครื่องในห้อง A ไม่ได้รับกฎของห้อง B")
            var allRules = await db.Rules
                .Where(r => r.RoomId == null || r.RoomId == client.RoomId)
                .ToListAsync();
            var ruleOverrides = await db.RuleOverrides
                .Where(o => o.ClientGuid == clientGuid)
                .ToDictionaryAsync(o => o.RuleId, o => o.IsEnabled);
            var rules = allRules
                .Where(r => ruleOverrides.TryGetValue(r.Id, out var overrideEnabled) ? overrideEnabled : r.IsEnabled)
                .ToList();

            // Website Control (2026-09-11) - เหมือน rules ข้างบนแต่ไม่มี override ต่อเครื่อง (ยังไม่ทำในรอบแรก)
            // ส่งเฉพาะกฎที่ IsEnabled เท่านั้น - Agent ไม่ต้องกรองซ้ำ ได้ list เดียวไปเขียน registry ตรงๆ
            // กรองตามห้องเหมือน Rules ข้างบน (WP2-3)
            var websiteRules = await db.WebsiteRules
                .Where(r => r.IsEnabled && (r.RoomId == null || r.RoomId == client.RoomId))
                .ToListAsync();

            // ป้องกันการเลี่ยง (WP2-1, 2026-10-31) - ส่งค่าดิบไปเสมอ ไม่กรองด้วย globalEnabled/client.IsEnabled
            // ที่นี่ เพราะ Agent เป็นคนตัดสินเองว่าจะ apply หรือเคลียร์ policy (ดู Worker.ScanRunningProcessesAsync
            // ที่ใช้ pattern เดียวกับ websiteRules - ส่ง empty/clear เองตอน bypass หรือ enforcement ปิด)
            var browserHardening = await db.BrowserHardeningSettings.FirstOrDefaultAsync(s => s.Id == 1);

            // fallback path สำหรับ command ที่ยังไม่ถึง Agent ผ่าน SignalR (เช่น เครื่อง disconnect อยู่ตอนที่ถูกสั่ง)
            // Agent จะเห็น command เหล่านี้ผ่าน poll รอบถัดไปเสมอ ไม่ว่า SignalR จะต่อติดอยู่หรือไม่ก็ตาม
            //
            // ==================== ตัวกันคำสั่งเก่าเด้งตอนบูต (2026-09-10) ====================
            // นี่คือด่านสำคัญที่สุดของเรื่องอายุคำสั่ง เพราะเป็นทางเดียวที่เครื่องซึ่งเพิ่งเปิดจะได้รับคำสั่งค้าง
            //
            // บั๊กจริง: ครูสั่ง Shutdown ทั้งห้อง เครื่องที่เปิดอยู่ดับปกติ ส่วนเครื่องที่ปิดอยู่ตอนนั้นได้
            // command ค้างเป็น Pending ไว้ พอถูกเปิดวันหลัง poll ครั้งแรกได้คำสั่งเดิมไปรัน = ดับเองทันที
            // ที่บูตเสร็จ โดยไม่มีใครสั่งอะไรในตอนนั้นเลย
            //
            // ปิดสถานะเป็น Expired ก่อน "แล้วค่อย" query - ไม่ใช่แค่กรองออกจากผลลัพธ์: ถ้าแค่กรอง แถวจะยัง
            // เป็น Pending ค้างใน DB ตลอดไป ทำให้หน้า PENDING COMMANDS ของครูโชว์คำสั่งที่ไม่มีวันทำงาน
            // และไม่มีใครรู้ว่าเกิดอะไรขึ้นกับคำสั่งนั้น
            var nowUtc = DateTime.UtcNow;
            var expiredCommands = await db.ClientCommands
                .Where(c => c.ClientGuid == clientGuid
                            && c.Status == ClientCommandStatus.Pending
                            && c.ExpiresAt != null
                            && c.ExpiresAt < nowUtc)
                .ToListAsync();

            foreach (var expired in expiredCommands)
            {
                expired.Status = ClientCommandStatus.Expired;
                if (expired.ExecutionStatus == CommandExecutionStatus.Pending)
                {
                    expired.ExecutionStatus = CommandExecutionStatus.Expired;
                    expired.ExecutionMessage =
                        "หมดอายุก่อนถึงเครื่อง (เครื่องน่าจะปิดอยู่ตอนที่สั่ง) - ไม่ได้รันและจะไม่รันย้อนหลัง";
                }
            }

            var pendingCommands = await db.ClientCommands
                .Where(c => c.ClientGuid == clientGuid
                            && c.Status == ClientCommandStatus.Pending
                            && (c.ExpiresAt == null || c.ExpiresAt >= nowUtc))
                .OrderBy(c => c.CreatedAt)
                .Select(c => new { c.Id, c.CommandType, c.PayloadJson, c.CreatedAt })
                .ToListAsync();

            await db.SaveChangesAsync();

            // เสริม real-time status ให้ console ที่เปิดอยู่ - ไม่กระทบ response ที่คืนให้ agent เลย
            client.IsConnectedViaSignalR = connectionRegistry.IsConnected(clientGuid);
            client.HasEverConnectedViaSignalR = connectionRegistry.HasEverConnected(clientGuid);
            await hub.Clients.BroadcastClientStatus(client);

            // ---- Blocklist ของ Program Control (2026-08-31) ----
            // ส่งรายการ SHA-256 ที่ตัดสินแล้วว่า Block ไปกับ poll เลย แทนที่จะให้ Agent ถามทีละไฟล์:
            // Agent จึงปิดโปรแกรมที่ "เคยเจอที่ไหนสักเครื่องในห้องแล้ว" ได้ทันทีที่เห็น โดยไม่ต้องรอ
            // round trip ซึ่งเป็นช่วงเวลาที่เกมได้รันไปแล้วจริงๆ
            //
            // ส่งเฉพาะตอนที่เปลี่ยนจริง: Agent แนบ version ที่ตัวเองถืออยู่มาด้วย ถ้าตรงกันก็ส่ง null กลับไป
            // แปลว่า "ใช้ของเดิมต่อ" - กันไม่ให้ทุก poll ของ 40 เครื่องลากรายการยาวๆ ข้ามเน็ตทุก 30 วิ
            // โดยไม่มีอะไรเปลี่ยน (ใช้ MaxAsync ของ Id ร่วมกับจำนวนแถวเป็น version: ครอบคลุมทั้งการเพิ่ม
            // แถวใหม่และการที่ครูเปลี่ยนคำตัดสินของแถวเดิม เพราะ...)
            var blockedQuery = db.ProgramVerdicts.Where(v => v.Action == ProgramAction.Block);
            var blockedCount = await blockedQuery.CountAsync();
            var blockedMaxId = blockedCount == 0 ? 0 : await blockedQuery.MaxAsync(v => v.Id);

            // ...การเปลี่ยนคำตัดสินของแถวเดิมทำให้ "จำนวนแถวที่เป็น Block" เปลี่ยนเสมอ (แถวหนึ่งเข้าหรือ
            // ออกจากเซ็ต) ยกเว้นกรณีที่ครูสลับ 2 แถวพร้อมกันพอดีในรอบ poll เดียว ซึ่งจะตกไปรู้ในรอบถัดไป
            // ที่มีอะไรเปลี่ยนอีก - ยอมรับได้ ไม่คุ้มกับการเก็บ column version แยกที่ต้องดูแลให้ตรงตลอด
            var currentBlocklistVersion = $"{blockedCount}-{blockedMaxId}";

            List<string>? blockedHashes = null;
            if (!string.Equals(blocklistVersion, currentBlocklistVersion, StringComparison.Ordinal))
            {
                blockedHashes = await blockedQuery.Select(v => v.Sha256).ToListAsync();
            }

            // "ต้องล็อกอินไหม" (WP3-1, 2026-10-31) - ดู docs/roadmap.md "poll ส่งค่า 'ต้องล็อกอินไหม' ไปให้
            // Agent" ตรรกะ: Off = ไม่บังคับเลย, AllRooms = บังคับทุกเครื่อง, SelectedRooms = บังคับเฉพาะ
            // เครื่องที่ client.RoomId อยู่ในรายการที่ตั้งไว้ (เครื่องที่ยังไม่จัดห้อง = RoomId null ไม่ตรง
            // กับห้องไหนเลย จึงไม่ถูกบังคับในโหมดนี้) ปิด = ปลดเองภายใน 1 รอบ poll ตามที่ roadmap ระบุ - ทำได้
            // เองโดยไม่ต้องมี flag พิเศษอะไรเพิ่ม เพราะ requireLogin คำนวณใหม่จากค่าปัจจุบันทุกครั้งที่ poll อยู่แล้ว
            var signInSettings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            var requireLogin = signInSettings.RequireLoginMode switch
            {
                RequireLoginMode.Off => false,
                RequireLoginMode.AllRooms => true,
                RequireLoginMode.SelectedRooms => client.RoomId is not null &&
                    (signInSettings.RequireLoginRoomIdsCsv ?? string.Empty)
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Contains(client.RoomId.Value.ToString()),
                _ => false
            };

            return Results.Ok(new
            {
                enabled = globalEnabled && client.IsEnabled,
                rules,
                websiteRules,
                browserHardening,
                pollIntervalOverrideSeconds = client.PollIntervalOverrideSeconds,
                pendingCommands,
                blocklistVersion = currentBlocklistVersion,
                blockedHashes,
                requireLogin
            });
        });

        return app;
    }
}
