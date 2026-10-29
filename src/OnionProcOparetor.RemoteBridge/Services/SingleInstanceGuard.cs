namespace OnionProcOparetor.RemoteBridge.Services;

/// <summary>กันไม่ให้ RemoteBridge รันซ้อนกันหลาย instance บนเครื่องเดียวกัน (เช่นตั้ง auto-start ไว้แล้วเผลอ double-click เปิดเองอีกรอบ)</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, "Global\\OnionProcOparetor.RemoteBridge.SingleInstance", out var createdNew);
        return createdNew;
    }

    public void Dispose() => _mutex?.Dispose();
}
