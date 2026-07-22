using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Hubs;

/// <summary>
/// Real-time push คู่ขนานกับ HTTP polling เดิม - เป็นชั้นเสริม ไม่ใช่ตัวแทนที่
/// ถ้า Agent/Console หลุดจาก hub นี้ ระบบยังทำงานได้ปกติผ่าน poll เดิมทุกอย่าง (ดู /api/poll/{clientGuid})
///
/// Group ที่ใช้:
/// - group ชื่อ = ClientGuid ของแต่ละเครื่อง agent (join ผ่าน RegisterAgent) ใช้ push command เจาะจงเครื่องได้
/// - group "console" (join ผ่าน RegisterConsole) ใช้ broadcast สถานะเครื่องให้ console admin ทุกตัวที่เปิดอยู่
/// </summary>
public class LabHub : Hub
{
    public const string ConsoleGroupName = "console";

    private readonly AppDbContext _db;

    public LabHub(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Agent เรียกทันทีหลัง connect เสร็จ เพื่อ join group เฉพาะเครื่องตัวเอง (ชื่อ group = clientGuid)</summary>
    public async Task RegisterAgent(string clientGuid)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, clientGuid);

        // อัปเดต LastSeenAt ทันทีที่ต่อ SignalR สำเร็จ (เสริมจาก poll เดิมที่อัปเดตอยู่แล้วทุกรอบ)
        // ถ้ายังไม่เคยมี ClientMachine ใน DB เลย (ยังไม่เคย poll มาก่อน) ข้ามไปก่อน - รอ poll รอบแรกสร้าง record ให้ตามเดิม
        var client = await _db.ClientMachines.FirstOrDefaultAsync(c => c.ClientGuid == clientGuid);
        if (client is not null)
        {
            client.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await Clients.Group(ConsoleGroupName).SendAsync("ClientStatusChanged", client);
        }
    }

    /// <summary>Console เรียกทันทีหลัง connect เสร็จ เพื่อ join group "console" รับ status update แบบ broadcast</summary>
    public Task RegisterConsole() => Groups.AddToGroupAsync(Context.ConnectionId, ConsoleGroupName);
}

/// <summary>
/// Helper method สำหรับ push จากฝั่ง server - ใช้ได้ทั้งจาก Program.cs (ผ่าน IHubContext&lt;LabHub&gt;.Clients)
/// และจากในตัว LabHub เอง (ผ่าน Clients) เพราะทั้งคู่ implement IHubClients ร่วมกัน
/// </summary>
public static class LabHubClientsExtensions
{
    /// <summary>
    /// Push command ไปหา Agent เครื่องที่ระบุทันที ถ้า connected ผ่าน SignalR อยู่ขณะนี้ (ไม่มี guarantee การส่งถึง - ต้องพึ่ง poll fallback เสมอ)
    /// ส่ง commandId ไปด้วยเสมอ - Agent ใช้ตัวนี้ dedup + ack กลับมา (path เดียวกับที่ใช้ตอนเจอ command ผ่าน poll fallback)
    /// </summary>
    public static Task SendCommandToAgent(this IHubClients clients, string clientGuid, int commandId, string commandType, string? payloadJson)
        => clients.Group(clientGuid).SendAsync("ReceiveCommand", commandId, commandType, payloadJson);

    /// <summary>Broadcast สถานะเครื่อง client ล่าสุดไปให้ console admin ทุกตัวที่ join group "console" อยู่</summary>
    public static Task BroadcastClientStatus(this IHubClients clients, ClientMachine client)
        => clients.Group(LabHub.ConsoleGroupName).SendAsync("ClientStatusChanged", client);
}
