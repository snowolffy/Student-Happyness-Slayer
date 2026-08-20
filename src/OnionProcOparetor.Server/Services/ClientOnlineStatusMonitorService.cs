using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Hubs;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สแกนเป็นระยะหาเครื่อง client ที่เปลี่ยนสถานะ online/offline (ดู ClientMachine.IsOnline) แล้ว
/// broadcast ให้ Console ทันที - ชั้นเสริมสำหรับเครื่องที่ "ไม่เคย" connect SignalR เลย (เช่น firewall
/// บล็อก WebSocket แต่ HTTP poll ยังผ่านได้) เพราะเครื่องกลุ่มนั้นจะไม่มี LabHub.OnDisconnectedAsync
/// มา trigger การ broadcast ให้เอง ต้องมี background scan มาจับเองแทน
///
/// เดิมชื่อ MissingClientMonitorService (เปลี่ยนชื่อ 2026-08-20 พร้อมกับยุบ concept "Missing"
/// (IsEnabled + หายเกิน 5 นาที) รวมกับ "Offline" (หายเกิน 90 วิ อย่างเดียว) เป็น IsOnline ตัวเดียว
/// - ดู ClientMachine.cs) เดิม scan เฉพาะเครื่องที่ IsEnabled=true เท่านั้น เพราะ Missing ผูกกับ
/// enforcement โดยตรง ตอนนี้ online/offline ไม่เกี่ยวกับ enforcement อีกต่อไปแล้ว (2 คอลัมน์แยกกันเด็ดขาด
/// ใน Console) เลย scan "ทุกเครื่อง" ไม่ใช่แค่เครื่องที่ enabled
///
/// ชั้นเสริมเท่านั้น: ถ้า service นี้หยุดทำงาน Console ก็ยังเห็นค่า IsOnline ล่าสุดผ่าน GET /api/clients
/// (auto-refresh ทุก 10 วิ) อยู่ดี แค่ไม่มี push แจ้งเตือนทันทีที่ threshold ถูกข้าม
/// </summary>
public class ClientOnlineStatusMonitorService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<LabHub> _hub;
    private readonly AgentConnectionRegistry _connectionRegistry;
    private readonly ILogger<ClientOnlineStatusMonitorService> _logger;

    // เก็บสถานะ online ล่าสุดที่เคย broadcast ไปแล้วต่อเครื่อง (keyed ด้วย ClientMachine.Id) เพื่อ
    // broadcast แค่ตอน "เปลี่ยนสถานะ" เท่านั้น ไม่ spam ทุกรอบสแกนขณะที่เครื่องยัง state เดิมอยู่
    private readonly ConcurrentDictionary<int, bool> _lastKnownOnlineState = new();

    public ClientOnlineStatusMonitorService(
        IServiceScopeFactory scopeFactory,
        IHubContext<LabHub> hub,
        AgentConnectionRegistry connectionRegistry,
        ILogger<ClientOnlineStatusMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _connectionRegistry = connectionRegistry;
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
                _logger.LogError(ex, "ClientOnlineStatusMonitorService: สแกนรอบนี้ล้มเหลว - ข้ามไปรอบถัดไป");
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var allClients = await db.ClientMachines.ToListAsync(ct);
        var currentIds = new HashSet<int>(allClients.Select(c => c.Id));

        // ลบ state ของเครื่องที่ถูกลบออกจากระบบไปแล้ว
        foreach (var staleId in _lastKnownOnlineState.Keys.Except(currentIds).ToList())
        {
            _lastKnownOnlineState.TryRemove(staleId, out _);
        }

        foreach (var client in allClients)
        {
            client.IsConnectedViaSignalR = _connectionRegistry.IsConnected(client.ClientGuid);
            var isOnlineNow = client.IsOnline;
            var wasOnline = _lastKnownOnlineState.GetValueOrDefault(client.Id, true); // default true กัน spam broadcast ตอน service เพิ่ง start ถ้าเครื่อง online อยู่แล้ว

            if (isOnlineNow == wasOnline)
            {
                continue;
            }

            _lastKnownOnlineState[client.Id] = isOnlineNow;

            if (!isOnlineNow)
            {
                _logger.LogInformation(
                    "Client '{MachineName}' ({ClientGuid}) offline - LastSeenAt {LastSeenAt:u} (เกิน {Threshold} วิ, ไม่มี SignalR connection)",
                    client.MachineName, client.ClientGuid, client.LastSeenAt, ClientMachine.OnlineThresholdSeconds);
            }

            await _hub.Clients.BroadcastClientStatus(client);
        }
    }
}
