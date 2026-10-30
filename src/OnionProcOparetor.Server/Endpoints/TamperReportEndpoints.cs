using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;

namespace OnionProcOparetor.Server.Endpoints;

/// <summary>รายงาน tamper จาก agent-watchdog.ps1</summary>
public static class TamperReportEndpoints
{
    public static WebApplication MapTamperReportEndpoints(this WebApplication app)
    {
        // ---- Tamper report จาก agent-watchdog.ps1 (installer/CustomActions/) ----
        // ยิงมา "ก่อน" เครื่องจะ reboot ตัวเอง เป็น best-effort ล้วนๆ: watchdog รอแค่ไม่กี่วินาที
        // แล้ว reboot ต่อไม่ว่าจะสำเร็จหรือไม่ - เป้าหมายเดียวคือให้ครูเห็นที่ Console ว่า "เครื่อง
        // COM-xx หลุดไปเพราะถูก tamper" แทนที่จะเห็นแค่ "หลุดไปเฉยๆ" ซึ่งแยกจากเน็ตมีปัญหาไม่ออก
        //
        // ไม่ต้อง auth เหมือน /api/logs และ /api/poll (ข้อจำกัดเดิมของทั้งระบบ - ดู PROGRESS/
        // SECURITY note: agent-facing endpoints ทั้งหมดยังไม่มี per-agent secret) endpoint นี้จึง
        // "เขียนได้อย่างเดียว ไม่เปลี่ยน state ของ enforcement อะไรเลย" โดยตั้งใจ: ปลอมเข้ามาได้
        // อย่างมากก็สร้าง log ปลอม ไม่สามารถใช้ปิด/เปิดกฎ หรือสั่งการเครื่องอื่นได้
        app.MapPost("/api/tamper-report", async (
            TamperReportRequest req,
            AppDbContext db,
            IActivityLogService activityLog,
            IHubContext<LabHub> hub) =>
        {
            // ตัดความยาวทุก field ก่อนเก็บ - ผู้ส่งไม่ได้ผ่าน auth จึงต้องถือว่าเป็น input ที่ควบคุมไม่ได้
            // (endpoint นี้เป็นจุดใหม่ ไม่อยากให้เพิ่มพื้นที่ disk-fill ให้กว้างกว่าที่ /api/logs มีอยู่แล้ว)
            static string Clamp(string? value, int max) =>
                string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

            var machineName = Clamp(req.MachineName, 128);
            var reasonCode = Clamp(req.ReasonCode, 64);
            var detail = Clamp(req.Detail, 1024);

            var log = new LogEntry
            {
                ClientGuid = Clamp(req.ClientGuid, 64),
                MachineName = machineName,
                // ไม่มี process ที่เกี่ยวข้องจริง - ใส่ marker ที่ค้นหาเจอง่ายใน Console แทนการปล่อยว่าง
                ProcessPath = "[TAMPER]",
                DetectedPublisher = reasonCode,
                WasSignedMatch = false,
                MatchedRuleId = null,
                MatchedRuleName = "Tamper Protection",
                ActionTaken = $"TAMPER DETECTED ({reasonCode}) - เครื่องกำลังจะ restart อัตโนมัติ | {detail}",
                Timestamp = DateTime.UtcNow
            };

            db.LogEntries.Add(log);
            await db.SaveChangesAsync();

            await activityLog.LogAgentConnectivityAsync(
                "AgentTamperDetected",
                $"เครื่อง {machineName} ตรวจพบการแก้ไข/ถอน Agent ที่ไม่ได้รับอนุญาต ({reasonCode}) และกำลังรีสตาร์ทตัวเอง",
                log.ClientGuid,
                machineName);

            // push ให้ Console ที่เปิดอยู่เห็นทันที ไม่ต้องรอรอบ refresh ถัดไป - เครื่องกำลังจะ reboot
            // ในอีกไม่กี่วินาที ถ้ารอ poll ปกติครูจะเห็นตอนที่เครื่องหายไปแล้วซึ่งช้าเกินไปที่จะไปดูทัน
            await hub.Clients.Group("console").SendAsync("TamperDetected", new
            {
                clientGuid = log.ClientGuid,
                machineName,
                reasonCode,
                detail,
                detectedAtUtc = log.Timestamp
            });

            return Results.Ok(new { received = true });
        });

        return app;
    }
}
