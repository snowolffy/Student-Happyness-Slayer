using System.Collections.Concurrent;
using OnionProcOparetor.Console.Models;

namespace OnionProcOparetor.Console.Services;

public enum VncSessionStatus
{
    Connecting,
    Connected,
    Failed,
    Closed
}

/// <summary>
/// Session เดียวใน VncSessionPool - ผูกกับเครื่อง client หนึ่งเครื่อง เป็นเจ้าของ RfbClient +
/// background framebuffer loop ของตัวเองทั้งหมด ไม่แชร์อะไรกับ handle อื่นเลย (ตามที่ตกลง: Dispose ต้อง
/// ทำงานถูกต้องเป็นรายตัว ปิด session หนึ่งไม่กระทบ session อื่นที่ยังเปิดอยู่)
/// </summary>
public sealed class VncSessionHandle : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private RfbClient? _rfbClient;
    private TaskCompletionSource? _connectedTcs;

    public ClientMachineDto Client { get; }
    public VncViewMode Mode { get; private set; }
    public VncSessionStatus Status { get; private set; } = VncSessionStatus.Connecting;
    public string? FailureReason { get; private set; }

    /// <summary>
    /// ขนาดภาพที่ควรใช้สร้าง WriteableBitmap - คำนวณจาก Mode ของ handle เอง (ไม่ใช่จาก
    /// RfbClient.EffectiveWidth/Height ที่ขึ้นกับจังหวะที่ framebuffer loop thread เริ่มรันจริง) เพื่อไม่
    /// ให้ติด race ตอนอ่านค่าทันทีที่ Status เปลี่ยนเป็น Connected (ดู comment ยาวใน
    /// RfbClient.EffectiveWidth) null ถ้ายังไม่ Connected (ยังไม่รู้ FramebufferWidth/Height จริงจาก
    /// ServerInit)
    /// </summary>
    public ushort? DisplayWidth => _rfbClient is null ? null : Mode == VncViewMode.Thumbnail ? _rfbClient.ThumbnailWidth : _rfbClient.FramebufferWidth;
    public ushort? DisplayHeight => _rfbClient is null ? null : Mode == VncViewMode.Thumbnail ? _rfbClient.ThumbnailHeight : _rfbClient.FramebufferHeight;

    /// <summary>เรียกทุกครั้งที่ Status เปลี่ยน - เรียกจาก background thread (เหมือน RfbClient callback อื่นๆ ในโปรเจกต์นี้) ผู้ subscribe ต้อง marshal เข้า UI thread เอง</summary>
    public event Action? StatusChanged;

    /// <summary>เรียกทุกครั้งที่ decode rectangle ใหม่เสร็จ - เรียกจาก background thread เหมือนกัน (พิกัดอยู่ใน effective size ปัจจุบันของ RfbClient - ดู RfbRectangle doc comment)</summary>
    public event Action<RfbRectangle>? RectangleDecoded;

    internal VncSessionHandle(ClientMachineDto client, VncViewMode initialMode)
    {
        Client = client;
        Mode = initialMode;
    }

    /// <summary>
    /// เรียกครั้งเดียวจาก VncSessionPool.OpenAsync หลังจอง slot สำเร็จ - เริ่มงานเบื้องหลังผ่าน Task.Run
    /// แล้วคืน Task ที่ resolve ทันทีที่ "ช่วง connect" จบ (Connected หรือ Failed เท่านั้น - ไม่ใช่ทั้งอายุ
    /// ของ session) เพื่อให้ OpenAsync ไม่ต้อง block รอทั้ง framebuffer streaming loop ซึ่งทำงานยาวจนกว่า
    /// จะ Dispose - แยกสองช่วงนี้ออกจากกันชัดเจนคือหัวใจของการเปิดหลายเครื่องพร้อมกันได้จริง
    /// </summary>
    internal Task StartAsync(ApiClient apiClient, LabHubClient labHubClient, TimeSpan sessionReadyTimeout)
    {
        var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectedTcs = connectedTcs;

        _ = Task.Run(() => RunAsync(apiClient, labHubClient, sessionReadyTimeout));

        return connectedTcs.Task;
    }

    private async Task RunAsync(ApiClient apiClient, LabHubClient labHubClient, TimeSpan sessionReadyTimeout)
    {
        var ct = _cts.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(Client.LastKnownIp))
            {
                SetFailed("ไม่ทราบ IP เป้าหมาย (LastKnownIp ว่าง - รอ Agent poll เข้ามาอีกครั้ง)");
                return;
            }

            var sessionResult = await RequestVncSessionAsync(apiClient, labHubClient, sessionReadyTimeout, ct);
            ct.ThrowIfCancellationRequested();

            if (!sessionResult.Success)
            {
                SetFailed(sessionResult.FailureReason ?? "เปิด VNC session ไม่สำเร็จ");
                return;
            }

            var rfbClient = new RfbClient();
            rfbClient.LogMessage += msg => ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] {msg}");

            RfbHandshakeResult handshakeResult;
            try
            {
                handshakeResult = await rfbClient.ConnectAndHandshakeAsync(Client.LastKnownIp, sessionResult.Port, sessionResult.Password!, ct);
            }
            catch (OperationCanceledException)
            {
                rfbClient.Dispose();
                throw;
            }

            ct.ThrowIfCancellationRequested(); // เผื่อ dispose เข้ามาพอดีตอน handshake เพิ่งสำเร็จ - ปิดทิ้งแทนใช้ต่อ
            if (!handshakeResult.Success)
            {
                rfbClient.Dispose();
                SetFailed(handshakeResult.FailureReason ?? "RFB handshake ไม่สำเร็จ");
                return;
            }

            lock (_stateLock)
            {
                _rfbClient = rfbClient;
            }
            SetConnected();
            ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] Connected - เริ่ม RunFramebufferUpdateLoopAsync (mode เริ่มต้น: {Mode})");

            await rfbClient.RunFramebufferUpdateLoopAsync(Mode, rect => RectangleDecoded?.Invoke(rect), ct);
        }
        catch (OperationCanceledException)
        {
            // Dispose ระหว่างกำลังเชื่อมต่อ/สตรีมอยู่ - ปิดปกติ ไม่ต้องรายงานเป็น error
            ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] Cancelled (dispose ปกติ)");
            _connectedTcs?.TrySetResult();
        }
        catch (Exception ex)
        {
            ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] Exception ไม่คาดคิดใน RunAsync: {ex}");
            SetFailed($"เชื่อมต่อไม่สำเร็จ: {ex.Message}");
        }
    }

    /// <summary>สลับ mode โดยไม่ reconnect - ดู RfbClient.RequestModeSwitch (eventually-consistent, ไม่รอผลจริง) เรียกได้เฉพาะหลัง Status เป็น Connected แล้วเท่านั้น (ไม่งั้นยังไม่มี RfbClient ให้สลับ)</summary>
    public void RequestModeSwitch(VncViewMode newMode)
    {
        ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] RequestModeSwitch: {Mode} -> {newMode}");
        Mode = newMode;
        _rfbClient?.RequestModeSwitch(newMode);
    }

    private void SetConnected()
    {
        lock (_stateLock)
        {
            Status = VncSessionStatus.Connected;
        }
        StatusChanged?.Invoke();
        _connectedTcs?.TrySetResult();
    }

    private void SetFailed(string reason)
    {
        ConsoleDiagnostics.WriteLine($"[VncSessionHandle:{Client.MachineName}] Failed: {reason}");
        lock (_stateLock)
        {
            Status = VncSessionStatus.Failed;
            FailureReason = reason;
        }
        StatusChanged?.Invoke();
        _connectedTcs?.TrySetResult();
    }

    /// <summary>
    /// ส่ง command StartVncSession แล้วรอผลผ่าน LabHubClient event - filter ด้วย Client.ClientGuid เสมอ
    /// (ตั้งแต่ Step 4e event มี clientGuid มาด้วยเพราะ pool เปิดได้หลายเครื่องพร้อมกัน - ถ้าไม่ filter
    /// จะรับ event ของ session อื่นผิดตัวได้ ดู comment ยาวใน LabHubClient.VncSessionReady) มี timeout
    /// กันค้างถ้า Agent ไม่ตอบ (offline/SignalR หลุด) - subscribe/unsubscribe event ชั่วคราวเฉพาะช่วงรอนี้
    /// </summary>
    private async Task<(bool Success, int Port, string? Password, string? FailureReason)> RequestVncSessionAsync(
        ApiClient apiClient, LabHubClient labHubClient, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(bool, int, string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReady(string clientGuid, int port, string password)
        {
            if (clientGuid == Client.ClientGuid)
            {
                tcs.TrySetResult((true, port, password, null));
            }
        }

        void OnFailed(string clientGuid, string reason)
        {
            if (clientGuid == Client.ClientGuid)
            {
                tcs.TrySetResult((false, 0, null, reason));
            }
        }

        labHubClient.VncSessionReady += OnReady;
        labHubClient.VncSessionFailed += OnFailed;

        try
        {
            var sent = await apiClient.SendCommandAsync(Client.ClientGuid, "StartVncSession",
                new { requestingConsoleSessionId = labHubClient.SessionId });

            if (!sent)
            {
                return (false, 0, null, "ส่ง command StartVncSession ไปยัง Server ไม่สำเร็จ (HTTP)");
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct));
            if (completed != tcs.Task)
            {
                return (false, 0, null, $"รอ Agent ตอบกลับเกิน {timeout.TotalSeconds} วิ (Agent อาจ offline หรือ SignalR ไม่ connected)");
            }

            return await tcs.Task;
        }
        finally
        {
            labHubClient.VncSessionReady -= OnReady;
            labHubClient.VncSessionFailed -= OnFailed;
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _rfbClient?.Dispose();
        Status = VncSessionStatus.Closed;
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Pool ของ VncSessionHandle หลายตัวพร้อมกัน keyed ด้วย ClientGuid - รองรับเปิดดูหลายเครื่องพร้อมกันแบบ
/// grid (Step 4e) แต่ละ handle เป็นอิสระจากกันเต็มที่ (RfbClient + background loop +
/// CancellationTokenSource แยกของตัวเอง) ปิด handle หนึ่งไม่กระทบตัวอื่นเลย
///
/// ป้องกัน race ตอนเปิดหลาย session พร้อมกันเร็วๆ (เช่นเลือกกลุ่มเครื่องแล้วกด "add to grid" ทีเดียว) ด้วย
/// two-phase reserve-then-connect: ส่วน "เช็ค limit + จอง slot" เป็น critical section สั้นๆ ผ่าน
/// SemaphoreSlim เท่านั้น (ไม่ lock ทั้งการเชื่อมต่อ) งาน network/handshake ที่ช้าเกิดขึ้นนอก critical
/// section เสมอ ทำให้เปิดหลายเครื่องพร้อมกันจริงๆ ได้ (ไม่ต้องรอทีละเครื่องเรียงคิว)
/// </summary>
public sealed class VncSessionPool
{
    /// <summary>
    /// เพดานจำนวน session พร้อมกันสูงสุดที่ Console เปิดเองได้ - กันผู้ใช้เผลอเลือกเปิดทีเดียวหมด (เช่น 40
    /// เครื่อง) แล้ว network/CPU ล่ม ค่า default 24 ตอนนี้ - จงใจทำเป็น static field ที่แก้ค่าได้ (ไม่ใช่
    /// const) จุดเดียวในระบบ แทนที่จะ hardcode 24 กระจายไปตามไฟล์ต่างๆ ของ Step 4f เพื่อให้ session ที่ทำ
    /// SettingsService คู่ขนานอยู่ตอนนี้เข้ามาตั้งค่านี้จาก config ได้ทีหลังง่ายๆ (เช่น
    /// `VncSessionPool.MaxConcurrentSessions = settings.VncConcurrentLimit;` ตอน startup) โดยไม่ต้องแก้
    /// call site ที่อ้างถึงค่านี้เลยสักจุด (ทุกจุดอ้าง `VncSessionPool.MaxConcurrentSessions` เหมือนเดิม)
    /// TODO: รอ SettingsService เสร็จแล้วเปลี่ยนจาก static field ธรรมดานี้เป็นอ่านจาก config จริงตอน
    /// startup (ไม่ใช่งานของรอบนี้ - ห้ามแตะไฟล์ settings/config ที่ session นั้นสร้างเอง)
    /// </summary>
    public static int MaxConcurrentSessions { get; set; } = 24;

    private static readonly TimeSpan SessionReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiClient _apiClient;
    private readonly LabHubClient _labHubClient;
    private readonly ConcurrentDictionary<string, VncSessionHandle> _sessions = new();
    private readonly SemaphoreSlim _reservationLock = new(1, 1);

    public VncSessionPool(ApiClient apiClient, LabHubClient labHubClient)
    {
        _apiClient = apiClient;
        _labHubClient = labHubClient;
    }

    public int Count => _sessions.Count;

    public IReadOnlyCollection<VncSessionHandle> ActiveSessions => _sessions.Values.ToArray();

    public VncSessionHandle? TryGet(string clientGuid) => _sessions.GetValueOrDefault(clientGuid);

    /// <summary>
    /// เปิด session ใหม่สำหรับเครื่องนี้ - ถ้าเปิดอยู่แล้ว (หรือกำลังเปิดอยู่) คืน handle เดิม (idempotent
    /// ไม่เปิดซ้ำ) ถ้าเกิน MaxConcurrentSessions ปฏิเสธพร้อมข้อความชัดเจน งาน network/handshake จริงเกิด
    /// ขึ้นนอก reservation lock เพื่อให้เปิดหลายเครื่องพร้อมกัน (batch add) ได้จริงไม่ต้องรอทีละเครื่อง
    /// </summary>
    public async Task<(bool Success, VncSessionHandle? Handle, string? FailureReason)> OpenAsync(
        ClientMachineDto client, VncViewMode mode, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(client.ClientGuid, out var existing))
        {
            return (true, existing, null);
        }

        VncSessionHandle handle;
        await _reservationLock.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(client.ClientGuid, out existing))
            {
                return (true, existing, null); // อีก caller ชนะไปก่อนระหว่างที่กำลังรอ lock อยู่
            }

            if (_sessions.Count >= MaxConcurrentSessions)
            {
                return (false, null,
                    $"เปิดดูพร้อมกันได้สูงสุด {MaxConcurrentSessions} เครื่อง (ตอนนี้เปิดอยู่ {_sessions.Count} เครื่อง) - ปิดบางเครื่องก่อนเปิดเพิ่ม");
            }

            handle = new VncSessionHandle(client, mode);
            _sessions[client.ClientGuid] = handle;
        }
        finally
        {
            _reservationLock.Release();
        }

        await handle.StartAsync(_apiClient, _labHubClient, SessionReadyTimeout);
        return (true, handle, null);
    }

    /// <summary>ปิด session เดียว - ไม่กระทบ session อื่นที่ยังเปิดอยู่เลย (ตามที่ตกลง) เรียกซ้ำได้ปลอดภัย (idempotent)</summary>
    public async Task CloseAsync(string clientGuid)
    {
        if (_sessions.TryRemove(clientGuid, out var handle))
        {
            await handle.DisposeAsync();
        }
    }

    /// <summary>ปิดทุก session พร้อมกัน (เช่นตอนปิด Dashboard) - แต่ละ handle dispose แยกอิสระ ตัวหนึ่งพังไม่กระทบตัวอื่น</summary>
    public async Task CloseAllAsync()
    {
        var clientGuids = _sessions.Keys.ToList();
        await Task.WhenAll(clientGuids.Select(CloseAsync));
    }
}
