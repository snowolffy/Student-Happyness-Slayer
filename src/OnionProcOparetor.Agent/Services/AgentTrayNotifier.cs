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

    public Task SendBroadcastMessageAsync(BroadcastMessagePayload payload, CancellationToken ct)
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
    public Task SendShowLockScreenAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "ShowLockScreen" };
        return SendAsync(envelope, "ShowLockScreen", ct);
    }

    /// <summary>สั่งให้ AgentTray ปิดหน้าจอล็อก - ทางเดียวที่ปลดล็อกเครื่องได้ตามดีไซน์ (ครูกดจาก Console เท่านั้น)</summary>
    public Task SendHideLockScreenAsync(CancellationToken ct)
    {
        var envelope = new AgentTrayPipeMessage { Type = "HideLockScreen" };
        return SendAsync(envelope, "HideLockScreen", ct);
    }

    private async Task SendAsync(AgentTrayPipeMessage envelope, string logContext, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            // timeout สั้นๆ พอ - ถ้า Tray ไม่ได้รันอยู่ (ไม่มี user login) ต้อง fail เร็ว ไม่ค้าง command processing
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeoutCts.Token);

            var json = JsonSerializer.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await client.WriteAsync(bytes, ct);
            await client.FlushAsync(ct);

            _logger.LogInformation("ส่ง {LogContext} ให้ AgentTray สำเร็จ", logContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ส่ง {LogContext} ให้ AgentTray ไม่สำเร็จ (Tray อาจไม่ได้รันอยู่ - เช่นไม่มี user login อยู่หน้าเครื่อง) - ข้ามไป",
                logContext);
        }
    }
}
