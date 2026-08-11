namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// จุดเดียวที่ toggle "Bypass" จริง - เรียกจาก POST /bypass/set (local loopback API, ดู Program.cs)
/// ตอน AgentTray กด toggle ในหน้า Status ไม่ต้อง auth เพิ่มเติม (loopback + login gate ของ AgentTray
/// เองพอแล้ว - endpoint นี้ไม่เปิดสู่เครือข่ายภายนอกอยู่แล้ว)
///
/// ตัดขาด/ต่อกลับผ่าน LabHubConnection.DisposeAsync()/StartAsync() ที่มีอยู่แล้ว (ไม่ต้องเพิ่ม method
/// ใหม่ฝั่งนั้นเลย) - DisposeAsync ตัด SignalR ทันที, StartAsync สร้าง connection ใหม่ + retry loop
/// เดิมที่ Worker ใช้ตอน start เองอยู่แล้ว ส่วน HTTP poll loop ฝั่ง Worker.cs เช็ค AgentBypassState.IsBypassed
/// เองทุกรอบ - ไม่ต้องมีอะไรพิเศษเพิ่มที่นี่สำหรับ poll
///
/// SemaphoreSlim กัน race ถ้ามีการยิง toggle ซ้อนกันเร็วๆ (ไม่ควรเกิดจริงเพราะมาจาก UI เดียว แต่กันไว้)
/// </summary>
public class BypassCoordinator
{
    private readonly ILogger<BypassCoordinator> _logger;
    private readonly AgentBypassState _bypassState;
    private readonly LabHubConnection _labHubConnection;
    private readonly ClientState _clientState;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public BypassCoordinator(
        ILogger<BypassCoordinator> logger,
        AgentBypassState bypassState,
        LabHubConnection labHubConnection,
        ClientState clientState,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _bypassState = bypassState;
        _labHubConnection = labHubConnection;
        _clientState = clientState;
        _appLifetime = appLifetime;
    }

    /// <summary>คืนค่า bypass state ที่ยืนยันแล้วหลัง toggle (เผื่อ caller อยากโชว์ผลจริงกลับไปที่ UI)</summary>
    public async Task<bool> SetBypassedAsync(bool bypassed)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_bypassState.IsBypassed == bypassed)
            {
                return bypassed; // ตั้งไว้แบบนี้อยู่แล้ว - ไม่ต้องทำอะไรซ้ำ
            }

            _bypassState.Set(bypassed);

            if (bypassed)
            {
                _logger.LogInformation("Bypass เปิดใช้งาน - ตัดการเชื่อมต่อจาก Server ทั้งหมด (SignalR ตัดทันที, HTTP poll จะหยุดในรอบถัดไป)");
                await _labHubConnection.DisposeAsync();
            }
            else
            {
                _logger.LogInformation("Bypass ปิดใช้งาน - เชื่อมต่อ Server ใหม่ (SignalR reconnect, HTTP poll กลับมาทำงานรอบถัดไป)");
                _ = _labHubConnection.StartAsync(_clientState.ClientGuid, _appLifetime.ApplicationStopping);
            }

            return bypassed;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
