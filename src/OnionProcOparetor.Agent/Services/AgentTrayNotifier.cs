using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ส่ง broadcast message ให้ OnionProcOparetor.AgentTray (WPF tray app ที่รันอยู่ใน interactive
/// user session) แสดงเป็น popup ให้ user เห็น - จำเป็นเพราะ Agent เป็น Windows Service รันใน
/// Session 0 ซึ่งมองไม่เห็น desktop ของ user เลย (MessageBox.Show ตรงๆ จาก Service จะไม่โชว์ให้ใครเห็น)
///
/// สื่อสารผ่าน named pipe ใหม่ (Agent = client, AgentTray = server) - mechanism เดิมที่มีอยู่แล้ว
/// (ClientApiClient ฝั่ง Tray คุยกับ Agent ผ่าน local HTTP :8788) เป็นแบบ on-demand เท่านั้น
/// (Tray ดึงข้อมูลเองตอน user เปิดหน้า Status/กด Refresh - ไม่มี background listener ใดๆ)
/// ใช้ push ข้อความแบบนี้ไม่ได้ เลยต้องมี channel ใหม่ที่ Tray ฟังอยู่ตลอดเวลา
///
/// fail-secure: ถ้า AgentTray ไม่ได้รันอยู่ (เช่นไม่มี user login อยู่หน้าเครื่อง) จะไม่ throw
/// ออกไป แค่ log warning แล้วปล่อยผ่าน - command ยัง ack กลับ server ตามปกติ ไม่ retry ส่ง popup ซ้ำ
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

    public async Task SendBroadcastMessageAsync(BroadcastMessagePayload payload, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            // timeout สั้นๆ พอ - ถ้า Tray ไม่ได้รันอยู่ (ไม่มี user login) ต้อง fail เร็ว ไม่ค้าง command processing
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeoutCts.Token);

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await client.WriteAsync(bytes, ct);
            await client.FlushAsync(ct);

            _logger.LogInformation("ส่ง BroadcastMessage ให้ AgentTray สำเร็จ (title: {Title})", payload.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ส่ง BroadcastMessage ให้ AgentTray ไม่สำเร็จ (Tray อาจไม่ได้รันอยู่ - เช่นไม่มี user login อยู่หน้าเครื่อง) - ข้ามไป");
        }
    }
}
