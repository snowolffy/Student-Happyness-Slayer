using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// ฟัง named pipe รอรับ message จาก OnionProcOparetor.Agent (Windows Service ที่รันใน Session 0)
/// - วนรับ connection ทีละอันไม่มีที่สิ้นสุด (ฝั่ง Agent ต่อมา ส่ง 1 message แล้วตัดการเชื่อมต่อทันที
/// ไม่ใช่ connection ค้างยาว) ใช้ envelope { Type, Message?, Title? } เดียวกันสำหรับทุก message
/// เพื่อขยาย Type ใหม่ๆ ต่อได้ง่าย (ตอนนี้รองรับ "Broadcast", "ShowLockScreen", "HideLockScreen",
/// "ShowShutdownNotify")
///
/// ต้องเปิด ACL ให้ "Everyone" อ่าน/เขียนได้ เพราะ Agent (client) รันเป็น Windows Service
/// (มักเป็นบัญชีคนละตัวกับ user session นี้ เช่น LocalSystem) การเชื่อมต่อข้าม session/account
/// แบบนี้ต้องอาศัย security descriptor ที่อนุญาตไว้ชัดเจน ไม่งั้น default ACL อาจกันการเชื่อมต่อไว้
/// </summary>
public class BroadcastPipeListener
{
    /// <summary>ต้องตรงกับ AgentTrayNotifier.PipeName ฝั่ง Agent เป๊ะๆ</summary>
    public const string PipeName = "OnionProcOparetor.AgentTray.Broadcast";

    /// <summary>เรียกทุกครั้งที่ได้ broadcast message ใหม่ - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<BroadcastMessageDto>? MessageReceived;

    /// <summary>เรียกทุกครั้งที่ Agent สั่งแสดงหน้าจอล็อก - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? ShowLockScreenRequested;

    /// <summary>เรียกทุกครั้งที่ Agent สั่งปิดหน้าจอล็อก - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? HideLockScreenRequested;

    /// <summary>เรียกทุกครั้งที่ Agent สั่งเปิด VNC session (payload = RequestingConsoleSessionId) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<string>? StartVncSessionRequested;

    /// <summary>เรียกทุกครั้งที่ Agent สั่งหยุด VNC session (ไม่มี Console ต่อเข้ามาเลย/reconnect ไม่ทันเวลา - ดู VncSessionManager.MonitorSessionAsync) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? StopVncSessionRequested;

    /// <summary>เรียกทุกครั้งที่ครูกด "BLOCK STUDENT INPUT" ระหว่าง full-screen VNC (ดู PhysicalInputBlocker) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? BlockTargetInputRequested;

    /// <summary>เรียกทุกครั้งที่ครูกด "UNBLOCK STUDENT INPUT" หรือออกจาก full-screen - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? UnblockTargetInputRequested;

    /// <summary>
    /// เรียกทุกครั้งที่ Agent สั่งให้โชว์หน้าต่างนับถอยหลังก่อน shutdown/restart
    /// - เรียกจาก background thread ไม่ใช่ UI thread
    /// </summary>
    public event Action<ShutdownNotifyDto>? ShutdownNotifyRequested;

    /// <summary>เริ่มฟัง pipe แบบ fire-and-forget - มี retry loop ในตัว ไม่ throw ออกมาแม้เปิด pipe ไม่สำเร็จ</summary>
    public void Start(CancellationToken ct)
    {
        _ = RunAsync(ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(ct);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    HandleMessageLine(line);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BroadcastPipeListener] รับ connection ไม่สำเร็จ: {ex.Message}");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void HandleMessageLine(string line)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<AgentTrayPipeMessage>(
                line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (envelope is null)
            {
                return;
            }

            switch (envelope.Type)
            {
                case "Broadcast":
                    if (!string.IsNullOrWhiteSpace(envelope.Message))
                    {
                        MessageReceived?.Invoke(new BroadcastMessageDto { Message = envelope.Message, Title = envelope.Title });
                    }
                    break;

                case "ShowLockScreen":
                    ShowLockScreenRequested?.Invoke();
                    break;

                case "HideLockScreen":
                    HideLockScreenRequested?.Invoke();
                    break;

                case "StartVncSession":
                    if (!string.IsNullOrWhiteSpace(envelope.RequestingConsoleSessionId))
                    {
                        StartVncSessionRequested?.Invoke(envelope.RequestingConsoleSessionId);
                    }
                    break;

                case "StopVncSession":
                    StopVncSessionRequested?.Invoke();
                    break;

                case "BlockTargetInput":
                    BlockTargetInputRequested?.Invoke();
                    break;

                case "UnblockTargetInput":
                    UnblockTargetInputRequested?.Invoke();
                    break;

                case "ShowShutdownNotify":
                    // clamp ซ้ำอีกชั้น (Console + Agent clamp มาแล้ว) - ฝั่ง UI ต้องไม่พังถ้าได้ค่า
                    // แปลกๆ เช่น 0 หรือค่าลบ ซึ่งจะทำให้ progress bar หาร 0
                    ShutdownNotifyRequested?.Invoke(new ShutdownNotifyDto
                    {
                        IsRestart = string.Equals(envelope.Action, "Restart", StringComparison.OrdinalIgnoreCase),
                        DelaySeconds = Math.Clamp(envelope.DelaySeconds ?? 10, 1, 120)
                    });
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"[BroadcastPipeListener] ไม่รู้จัก message type: '{envelope.Type}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BroadcastPipeListener] แปลง message ไม่สำเร็จ: {ex.Message}");
        }
    }
}
