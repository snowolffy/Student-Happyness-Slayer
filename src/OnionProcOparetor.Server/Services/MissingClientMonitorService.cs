using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สแกนเป็นระยะหาเครื่อง client ที่ IsEnabled=true (กำลังถูก enforce rules อยู่) แต่ Agent
/// หายไปนานผิดปกติ (ดู ClientMachine.IsMissingUnexpectedly / MissingUnexpectedlyThresholdSeconds)
/// จำเป็นต้องมี background scan แยกต่างหาก เพราะเครื่องที่หายไปจริงๆ จะไม่ poll เข้ามาอีกเลย
/// (endpoint /api/poll ที่ broadcast สถานะปกติจะไม่ถูกเรียกจากเครื่องนั้นอีกต่อไป) - ชั้นเสริม
/// เท่านั้น: ถ้า service นี้หยุดทำงาน Console ก็ยังเห็นค่า IsMissingUnexpectedly ล่าสุดผ่าน
/// GET /api/clients (auto-refresh ทุก 10 วิ) อยู่ดี แค่ไม่มี push แจ้งเตือนทันทีที่ threshold ถูกข้าม
/// </summary>
public class MissingClientMonitorService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<LabHub> _hub;
    private readonly ILogger<MissingClientMonitorService> _logger;

    // เก็บสถานะ "หายผิดปกติ" ล่าสุดที่เคย broadcast ไปแล้วต่อเครื่อง (keyed ด้วย ClientMachine.Id)
    // เพื่อ broadcast แค่ตอน "เปลี่ยนสถานะ" เท่านั้น (เพิ่งหาย หรือกลับมาแล้ว) ไม่ spam ทุกรอบสแกน
    // ขณะที่เครื่องยังหายอยู่เหมือนเดิม
    private readonly ConcurrentDictionary<int, bool> _lastKnownMissingState = new();

    public MissingClientMonitorService(
        IServiceScopeFactory scopeFactory,
        IHubContext<LabHub> hub,
        ILogger<MissingClientMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MissingClientMonitorService: สแกนรอบนี้ล้มเหลว - ข้ามไปรอบถัดไป");
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var enabledClients = await db.ClientMachines.Where(c => c.IsEnabled).ToListAsync(ct);
        var currentIds = new HashSet<int>(enabledClients.Select(c => c.Id));

        // ลบ state ของเครื่องที่ถูกลบออกจากระบบหรือถูก disable ไปแล้ว (ไม่ enforce อีกต่อไป
        // เลยไม่ต้องแจ้งเตือนกรณีนี้อีก)
        foreach (var staleId in _lastKnownMissingState.Keys.Except(currentIds).ToList())
        {
            _lastKnownMissingState.TryRemove(staleId, out _);
        }

        foreach (var client in enabledClients)
        {
            var isMissingNow = client.IsMissingUnexpectedly;
            var wasMissing = _lastKnownMissingState.GetValueOrDefault(client.Id, false);

            if (isMissingNow == wasMissing)
            {
                continue;
            }

            _lastKnownMissingState[client.Id] = isMissingNow;

            if (isMissingNow)
            {
                _logger.LogWarning(
                    "Client '{MachineName}' ({ClientGuid}) หายไปผิดปกติ - LastSeenAt {LastSeenAt:u} (เกิน {Threshold} วิ)",
                    client.MachineName, client.ClientGuid, client.LastSeenAt, ClientMachine.MissingUnexpectedlyThresholdSeconds);
            }

            await _hub.Clients.BroadcastClientStatus(client);
        }
    }
}
