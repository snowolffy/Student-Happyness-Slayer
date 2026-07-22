using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// ฟัง named pipe รอรับ broadcast message จาก OnionProcOparetor.Agent (Windows Service ที่รันใน
/// Session 0) - วนรับ connection ทีละอันไม่มีที่สิ้นสุด (ฝั่ง Agent ต่อมา ส่ง 1 message แล้วตัด
/// การเชื่อมต่อทันที ไม่ใช่ connection ค้างยาว)
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
            var dto = JsonSerializer.Deserialize<BroadcastMessageDto>(
                line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto is not null && !string.IsNullOrWhiteSpace(dto.Message))
            {
                MessageReceived?.Invoke(dto);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BroadcastPipeListener] แปลง message ไม่สำเร็จ: {ex.Message}");
        }
    }
}
