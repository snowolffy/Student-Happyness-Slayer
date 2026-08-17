using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// สั่ง OnionProcOparetor.AgentTray (WPF tray app ที่รันอยู่ใน interactive user session) ให้ทำ
/// อะไรบางอย่างที่ต้องมี desktop ของ user จริง - จำเป็นเพราะ Agent เป็น Windows Service รันใน
/// Session 0 ซึ่งมองไม่เห็น desktop ของ user เลย (MessageBox.Show หรือ LockWorkStation() ที่เรียก
/// ตรงจาก Service จะไม่มีผลกับ user session เลย)
///
/// สื่อสารผ่าน named pipe (Agent = client, AgentTray = server) - mechanism เดิมที่มีอยู่แล้ว
/// (ClientApiClient ฝั่ง Tray คุยกับ Agent ผ่าน local HTTP :8788) เป็นแบบ on-demand เท่านั้น
/// (Tray ดึงข้อมูลเองตอน user เปิดหน้า Status/กด Refresh - ไม่มี background listener ใดๆ)
/// ใช้ push แบบนี้ไม่ได้ เลยต้องมี channel ใหม่ที่ Tray ฟังอยู่ตลอดเวลา
///
/// Protocol: ส่ง JSON บรรทัดเดียวเป็น envelope { Type, Message?, Title? } - Type บอกว่า
/// AgentTray ควรทำอะไร (ดู AgentTrayPipeMessage) ออกแบบให้ขยาย Type ใหม่ๆ ต่อได้ง่าย
///
/// fail-secure: ถ้า AgentTray ไม่ได้รันอยู่ (เช่นไม่มี user login อยู่หน้าเครื่อง) จะไม่ throw
/// ออกไป แค่ log warning แล้วปล่อยผ่าน - command ยัง ack กลับ server ตามปกติ ไม่ retry ส่งซ้ำ
/// </summary>
public class AgentTrayNotifier
{
    /// <summary>ต้องตรงกับ BroadcastPipeListener.PipeName ฝั่ง AgentTray เป๊ะๆ</summary>
    public const string PipeName = "OnionProcOparetor.AgentTray.Broadcast";

    private readonly ILogger<AgentTrayNotifier> _logger;

    public AgentTrayNotifier(ILogger<AgentTrayNotifier> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendBroadcastMessageAsync(BroadcastMessagePayload payload, CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage
        {
            Type = "Broadcast",
            Message = payload.Message,
            Title = payload.Title
        };

        return SendAsync(envelope, $"BroadcastMessage (title: {payload.Title})", ct);
    }

    /// <summary>
    /// สั่งให้ AgentTray แสดงหน้าจอล็อกเต็มจอ (LockScreenWindow) - ต้องส่งผ่าน AgentTray เสมอ เพราะ
    /// Session 0 (Agent) ไม่มี desktop ให้แสดง window ได้เลย
    /// </summary>
    public Task<bool> SendShowLockScreenAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "ShowLockScreen" };
        return SendAsync(envelope, "ShowLockScreen", ct);
    }

    /// <summary>สั่งให้ AgentTray ปิดหน้าจอล็อก - ทางเดียวที่ปลดล็อกเครื่องได้ตามดีไซน์ (ครูกดจาก Console เท่านั้น)</summary>
    public Task<bool> SendHideLockScreenAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "HideLockScreen" };
        return SendAsync(envelope, "HideLockScreen", ct);
    }

    /// <summary>
    /// สั่งให้ AgentTray start winvnc.exe เอง (ต้องทำจาก AgentTray เท่านั้น - รันใน user session จริง
    /// ต่างจาก Agent เอง (Session 0) ที่ capture หน้าจอไม่ได้เลย - ดู comment ยาวใน AgentTrayPipeMessage)
    /// คืนค่า bool บอกว่า "ส่งคำสั่งถึง AgentTray สำเร็จไหม" เท่านั้น (ไม่ใช่ว่า VNC session เปิดสำเร็จไหม -
    /// อันนั้นมาทาง local HTTP callback /vnc/session-result แยกต่างหาก เพราะ start จริงใช้เวลา) ถ้าคืน
    /// false (เช่นไม่มี user login อยู่หน้าเครื่อง) ผู้เรียกต้องแจ้ง Console ว่าล้มเหลวทันที ไม่ต้องรอ callback
    /// </summary>
    public Task<bool> SendStartVncSessionAsync(string requestingConsoleSessionId, CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "StartVncSession", RequestingConsoleSessionId = requestingConsoleSessionId };
        return SendAsync(envelope, "StartVncSession", ct);
    }

    /// <summary>สั่งให้ AgentTray หยุด winvnc.exe - เรียกจาก VncSessionManager.MonitorSessionAsync ตอนไม่มี Console ต่อเข้ามาเลย (lazy-start ต้องไม่ถือ resource ค้างถ้าไม่มีใครใช้)</summary>
    public Task<bool> SendStopVncSessionAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "StopVncSession" };
        return SendAsync(envelope, "StopVncSession", ct);
    }

    /// <summary>
    /// สั่งให้ AgentTray บล็อก physical keyboard/mouse ของนักเรียนชั่วคราว (Toggle Target Input) - ต้อง
    /// ส่งผ่าน AgentTray เสมอ เพราะ Session 0 (Agent) ไม่มีสิทธิ์ install low-level hook ที่เห็น input
    /// ของ user session ได้เลย (ดู PhysicalInputBlocker ฝั่ง AgentTray)
    /// </summary>
    public Task<bool> SendBlockTargetInputAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "BlockTargetInput" };
        return SendAsync(envelope, "BlockTargetInput", ct);
    }

    /// <summary>สั่งให้ AgentTray คืนสิทธิ์ physical input ให้นักเรียน</summary>
    public Task<bool> SendUnblockTargetInputAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "UnblockTargetInput" };
        return SendAsync(envelope, "UnblockTargetInput", ct);
    }

    /// <summary>
    /// สั่งให้ AgentTray แสดงหน้าต่างนับถอยหลังมุมจอก่อน shutdown/restart (ShutdownNotifyWindow)
    /// แทน Broadcast popup ธรรมดาที่ใช้อยู่เดิม - popup เดิมโชว์ข้อความครั้งเดียวแล้วเงียบไปเลย
    /// จนเครื่องดับ (user ไม่รู้ว่าเหลือเวลาเท่าไหร่จริงๆ) อันใหม่นับถอยหลังให้เห็นตลอด
    ///
    /// สำคัญเรื่อง drift: caller ต้อง await method นี้ให้เสร็จ "ก่อน" เริ่มจับเวลาจริงฝั่ง Agent
    /// (ดู CommandProcessor.HandlePowerActionAsync) เพื่อไม่ให้เวลาที่ใช้ต่อ pipe + ส่ง JSON
    /// (ปกติไม่กี่ ms แต่ถ้า Tray ไม่ได้รันอยู่จะ timeout ถึง 3 วิ) ถูกนับรวมไปในเวลาที่ user เห็น
    /// คืนค่า bool เหมือน method อื่นๆ (ส่งถึง Tray สำเร็จไหม) ให้ผู้เรียกใช้ตัดสินใจ retry ได้ (ดู Part 4)
    /// </summary>
    public Task<bool> SendShutdownNotifyAsync(bool isRestart, int delaySeconds, CancellationToken ct)
    {
        var action = isRestart ? "Restart" : "Shutdown";
        var envelope = new AgentTrayPipeMessage
        {
            Type = "ShowShutdownNotify",
            Action = action,
            DelaySeconds = delaySeconds
        };

        return SendAsync(envelope, $"ShowShutdownNotify ({action}, {delaySeconds} วิ)", ct);
    }

    /// <summary>
    /// คืน true ถ้าส่งถึง AgentTray สำเร็จจริง, false ถ้าไม่สำเร็จ (ยังคง fail-secure เหมือนเดิม -
    /// ไม่ throw ออกไป เพื่อไม่ให้ command processing ทั้งสายพัง) ผู้เรียกใช้ค่านี้ไปรายงาน
    /// "ผลจริง" กลับ Server ได้ (ดู CommandProcessor) - เดิม method นี้คืน void เลยไม่มีใคร
    /// รู้เลยว่า action ที่สั่งไปทำได้จริงหรือเปล่า
    /// </summary>
    private async Task<bool> SendAsync(AgentTrayPipeMessage envelope, string logContext, CancellationToken ct)
    {
        try
        {
            await SendOnceAsync(envelope, ct);
            _logger.LogInformation("ส่ง {LogContext} ให้ AgentTray สำเร็จ", logContext);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ส่ง {LogContext} ให้ AgentTray ไม่สำเร็จ (Tray อาจไม่ได้รันอยู่ - เช่นไม่มี user login อยู่หน้าเครื่อง)",
                logContext);
            return false;
        }
    }

    /// <summary>ส่งจริงครั้งเดียว - throw ถ้าไม่สำเร็จ (ผู้เรียกเป็นคนจัดการ)</summary>
    private static async Task SendOnceAsync(AgentTrayPipeMessage envelope, CancellationToken ct)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);

        // timeout สั้นๆ พอ - ถ้า Tray ไม่ได้รันอยู่ (ไม่มี user login) ต้อง fail เร็ว ไม่ค้าง command processing
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeout);
        await client.ConnectAsync(timeoutCts.Token);

        var json = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await client.WriteAsync(bytes, ct);
        await client.FlushAsync(ct);
    }

    /// <summary>เวลารอต่อ pipe สูงสุดต่อ 1 ครั้ง - สั้นพอที่จะไม่ค้าง command processing นาน</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
}
