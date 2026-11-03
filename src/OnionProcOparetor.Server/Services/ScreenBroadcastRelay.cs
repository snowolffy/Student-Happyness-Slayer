using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using OnionProcOparetor.Server.Hubs;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// "ส่งจอครู" (WP4-4, 2026-11-01) - รีเลย์ภาพจอครูไปหา Agent ที่กำลังดูอยู่ ผ่าน LabHub ที่มีอยู่แล้ว
/// (ไม่สร้าง hub ใหม่ - เหตุผล: Console และ Agent มี HubConnection ไปที่ /hubs/lab ค้างอยู่ตลอดอายุ
/// โปรแกรมอยู่แล้ว เพิ่ม method ใหม่บน hub เดิมใช้โครงสร้าง connection/retry/auth ที่มีอยู่แล้วทั้งหมด
/// โดยไม่ต้องเพิ่ม HubConnection ตัวที่สองทั้งฝั่ง Console และ Agent)
///
/// ไม่เขียนลงดิสก์เลย (ตรงตามข้อจำกัด disk freeze แม้ Server เองไม่ติด Reboot Restore Rx ก็ตาม - frame
/// เป็นข้อมูลสดที่ไม่มีประโยชน์จะเก็บ) เก็บแค่ "เฟรมล่าสุด" ไว้ชั่วคราวในหน่วยความจำเพื่อให้ Agent ที่
/// join กลางอากาศ (เช่น ส่ง StartScreenBroadcast ช้ากว่า Console ไม่กี่ร้อย ms) เห็นภาพแรกได้เร็วขึ้น
/// โดยไม่ต้องรอ Console ส่งเฟรมถัดไป
///
/// drop stale frame ต่อ connection: ถ้า connection ใด "ยังส่งเฟรมก่อนหน้าไม่จบ" (เครือข่ายช้า/Agent
/// ประมวลผลช้า) จะข้ามเฟรมใหม่ของ connection นั้นไปเฉยๆ (ไม่ await ให้ connection ช้าตัวเดียวหน่วง
/// connection อื่นทั้งหมด และไม่ปล่อยให้ queue ของ SignalR ต่อ connection นั้นพอกโตไม่มีที่สิ้นสุด)
/// ถือเป็นชั้นเสริมจาก SignalR เดิม - ไม่ได้มาแทน back-pressure ของ transport
/// </summary>
public class ScreenBroadcastRelay
{
    /// <summary>ConnectionId ของ Agent ที่กำลัง "ดู" จอครูอยู่ตอนนี้ (join ผ่าน JoinScreenBroadcast)</summary>
    private readonly ConcurrentDictionary<string, byte> _viewerConnectionIds = new();

    /// <summary>0 = ไม่ได้ส่งอยู่ (รับเฟรมใหม่ได้), 1 = กำลังส่งเฟรมก่อนหน้าอยู่ (ข้ามเฟรมใหม่ไปก่อน) - ต่อ connection</summary>
    private readonly ConcurrentDictionary<string, int> _sendingFlags = new();

    private byte[]? _latestFrame;

    public void AddViewer(string connectionId)
    {
        _viewerConnectionIds[connectionId] = 0;
        _sendingFlags[connectionId] = 0;
    }

    public void RemoveViewer(string connectionId)
    {
        _viewerConnectionIds.TryRemove(connectionId, out _);
        _sendingFlags.TryRemove(connectionId, out _);
    }

    public bool HasViewers => !_viewerConnectionIds.IsEmpty;

    /// <summary>เฟรมล่าสุดที่ Console ส่งมา (ไว้ส่งให้ viewer ที่เพิ่ง join ทันทีถ้า caller เลือกจะใช้) - null = ยังไม่เคยมีเฟรมเข้ามาเลย</summary>
    public byte[]? LatestFrame => _latestFrame;

    /// <summary>
    /// รับเฟรมใหม่จาก Console แล้วส่งต่อให้ viewer ทุกตัวที่ join อยู่ตอนนี้ - ไม่ await การส่งแต่ละ
    /// connection ให้จบก่อนจะไปตัวถัดไป (fire-and-forget ต่อ connection) เพื่อไม่ให้ connection ช้า
    /// ตัวเดียวหน่วงทั้งห้อง และ "ข้าม" connection ที่ยังส่งเฟรมก่อนหน้าไม่จบ (drop stale frame)
    /// </summary>
    public void BroadcastFrame(IHubCallerClients clients, byte[] jpegBytes)
    {
        _latestFrame = jpegBytes;

        foreach (var connectionId in _viewerConnectionIds.Keys)
        {
            // TryUpdate(1, 0) = atomic "ได้สิทธิ์ส่ง" เฉพาะตอนค่าเดิมเป็น 0 (ไม่ได้กำลังส่งอยู่) -
            // ถ้า compare ไม่ผ่าน (ค่าเดิมเป็น 1 อยู่แล้ว) แปลว่า connection นี้ยังส่งเฟรมก่อนหน้าไม่จบ
            // ข้ามเฟรมนี้ไปเลย (drop stale frame) ไม่ต่อคิว
            if (_sendingFlags.TryUpdate(connectionId, 1, 0))
            {
                _ = SendAndReleaseAsync(clients, connectionId, jpegBytes);
            }
        }
    }

    private async Task SendAndReleaseAsync(IHubCallerClients clients, string connectionId, byte[] jpegBytes)
    {
        try
        {
            await clients.Client(connectionId).SendAsync("ScreenFrame", jpegBytes);
        }
        catch
        {
            // connection อาจหลุดไปแล้วพอดีระหว่างนี้ - RemoveViewer จะถูกเรียกจาก LabHub.OnDisconnectedAsync
            // อยู่แล้ว ที่นี่แค่ไม่ปล่อยให้ exception ของเฟรมเดียวทำให้ loop ของ BroadcastFrame สะดุด
        }
        finally
        {
            _sendingFlags.TryUpdate(connectionId, 0, 1);
        }
    }
}
