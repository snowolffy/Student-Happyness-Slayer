using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using OnionProcOparetor.AgentTray.Models;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// ฟัง named pipe รอรับ message จาก OnionProcOparetor.Agent (Windows Service ที่รันใน Session 0)
/// - วนรับ connection ทีละอันไม่มีที่สิ้นสุด (ฝั่ง Agent ต่อมา ส่ง 1 message แล้วตัดการเชื่อมต่อทันที
/// ไม่ใช่ connection ค้างยาว) ใช้ envelope { Type, Message?, Title? } เดียวกันสำหรับทุก message
/// เพื่อขยาย Type ใหม่ๆ ต่อได้ง่าย (ตอนนี้รองรับ "Broadcast", "ShowLockScreen", "HideLockScreen",
/// "ShowShutdownNotify")
///
/// ต้องตั้ง ACL เองเพราะ Agent (client) รันเป็น Windows Service คนละบัญชีกับ user session นี้
/// (LocalSystem) การเชื่อมต่อข้าม session/account แบบนี้ต้องอาศัย security descriptor ที่อนุญาตไว้
/// ชัดเจน ไม่งั้น default ACL จะกันการเชื่อมต่อไว้ - แต่ต้องอนุญาตเฉพาะ LocalSystem เท่านั้น
/// ไม่ใช่ Everyone (ดูเหตุผลเต็มที่ RunAsync ตรงจุดที่สร้าง PipeSecurity)
///
/// บั๊กจริงที่เจอตอน field test (2026-08-20): เดิม accept-loop เรียก HandleMessageLine "อยู่ใน loop
/// เดียวกัน" ก่อนวนกลับไป WaitForConnectionAsync รอบถัดไป - ถ้า handler ของ message ใดค้าง (เช่น
/// StartVncSession ที่ไปค้างใน VncProcessManager เพราะ createpassword.exe/winvnc.exe แฮงค์) จะไม่มีวัน
/// วนกลับไปรับ connection ใหม่ได้อีกเลย (maxNumberOfServerInstances=1 ด้วย) = AgentTray ไม่ตอบสนอง
/// "ทุก" คำสั่งถัดไปทั้งหมด (lock/unlock/broadcast/shutdown notify) ไม่ใช่แค่ VNC เท่านั้น ต้องปิด process
/// เองแล้วให้ Agent ยิงคำสั่งใหม่มา (trigger auto-recover ที่สตาร์ท instance ใหม่) ถึงจะกลับมาทำงาน
///
/// แก้โดยแยก "accept connection" ออกจาก "ประมวลผล message" เป็นคนละงาน เชื่อมกันด้วย Channel -
/// accept-loop แค่ read บรรทัดเดียวแล้วส่งเข้า channel ทันที ไม่รอ handler เลย วนกลับไปรับ connection
/// ถัดไปได้ต่อเนื่องเสมอไม่ว่า handler ของ message ก่อนหน้าจะค้างอยู่นานแค่ไหน ส่วนการประมวลผลจริงยังรัน
/// เรียงลำดับทีละอันเหมือนเดิม (single consumer) เพื่อคงพฤติกรรมเดิมที่ handler บางตัวไม่ idempotent
/// (เช่น ShutdownNotify ที่ต้อง "ทับด้วยคำสั่งล่าสุดเสมอ" - ดู OnShutdownNotifyRequested ฝั่ง App.xaml.cs)
/// ต้องมาถึง UI ตามลำดับที่ Agent ส่งจริง
///
/// รอบที่สอง (2026-09-10): การแยก accept ออกจาก handler ยังไม่พอ เพราะตัว accept-loop เองก็ค้างได้
/// ที่ ReadLineAsync - ดู ReadTimeout กับ RestartAccept() ข้างล่าง
/// </summary>
public class BroadcastPipeListener
{
    /// <summary>ต้องตรงกับ AgentTrayNotifier.PipeName ฝั่ง Agent เป๊ะๆ</summary>
    public const string PipeName = "OnionProcOparetor.AgentTray.Broadcast";

    /// <summary>
    /// เวลารอ "หนึ่งบรรทัด" หลัง client ต่อเข้ามาแล้ว - ต้องมี ไม่ใช่มีก็ดี
    ///
    /// ฝั่ง Agent (AgentTrayNotifier.SendOnceAsync) เขียน JSON + "\n" ทันทีหลัง ConnectAsync สำเร็จ
    /// ปกติจึงถึงภายในไม่กี่ ms - 10 วิเผื่อไว้เหลือเฟือ แต่ถ้า connection ไหน "ต่อเข้ามาแล้วไม่ส่งอะไรเลย"
    /// (client ถูกแช่แข็งกลางคันตอนเครื่อง sleep, ถูก kill ระหว่างเขียน, หรือ handle ค้างจนไม่เห็น EOF)
    /// ของเดิมจะรอตรงนั้น "ตลอดไป" เพราะใช้ ct อายุเท่าโปรแกรม และ maxNumberOfServerInstances=1
    /// แปลว่าไม่มีทางสร้าง server instance ใหม่มารับ connection อื่นได้เลย = Tray หูหนวกถาวร
    /// ทุกคำสั่งจาก Console หลังจากนั้นเงียบหมด (lock/unlock/broadcast/shutdown/VNC) ทั้งที่ process
    /// ยังรันอยู่ tray icon ยังกดได้ปกติ - ตรงกับอาการที่ผู้ใช้รายงาน
    ///
    /// ที่แย่กว่านั้นคือกู้เองไม่ได้: Agent ส่งไม่สำเร็จ -> สตาร์ท AgentTray ใหม่ (AgentTrayRecoveryService)
    /// -> ตัวใหม่เจอ SingleInstanceGuard ว่ามีตัวเก่าถืออยู่ -> ปิดตัวเองทันที safety net ฝั่ง Agent จึง
    /// แก้ได้เฉพาะเคส "Tray ตาย" ไม่ใช่เคส "Tray ค้าง" ต้องไป kill process เองที่เครื่องเท่านั้น
    /// </summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<string> _messageQueue = Channel.CreateUnbounded<string>();

    /// <summary>
    /// token ของ "การรับ connection รอบปัจจุบัน" (linked กับ token อายุเท่าโปรแกรมที่ Start() รับมา)
    /// มีไว้ให้ RestartAccept() ยกเลิกรอบที่ค้างอยู่ได้โดยไม่ฆ่าทั้ง listener - เขียน/อ่านข้าม thread
    /// (accept-loop กับ SystemEvents thread) จึงต้อง volatile
    /// </summary>
    private volatile CancellationTokenSource? _acceptCycleCts;

    /// <summary>เรียกทุกครั้งที่ได้ broadcast message ใหม่ - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<BroadcastMessageDto>? MessageReceived;

    /// <summary>เรียกทุกครั้งที่ Agent สั่งแสดงหน้าจอล็อก (WP3-3: payload = ข้อความที่ Agent ตัดสินแล้ว
    /// null/ว่าง = ให้ผู้ฟังใช้ข้อความ const เริ่มต้นของตัวเอง) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<string?>? ShowLockScreenRequested;

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

    /// <summary>เรียกทุกครั้งที่ Agent สั่งให้สแกนโปรแกรมที่มีหน้าต่างบน desktop (payload = RequestingConsoleSessionId) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<string>? GetRunningProcessesRequested;

    /// <summary>เรียกทุกครั้งที่ Agent แจ้งสถานะโหมดสอบเปลี่ยน (WP2-2, 2026-10-31) - payload = ExpiresAtUtc
    /// (null = ไม่มีโหมดสอบแล้ว ซ่อนป้าย) เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<DateTime?>? ExamModeStatusChanged;

    /// <summary>เรียกทุกครั้งที่ Agent แจ้งสถานะ "ต้องล็อกอินไหม" เปลี่ยน (WP3-3, 2026-10-31) - payload =
    /// (RequireLogin, LoginScreenMessage) เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<bool, string?>? SignInStatusChanged;

    /// <summary>เรียกตอนครูกด "เริ่มส่งจอ" (WP4-4, 2026-11-01) - payload = (ViewerMode, TeacherDisplayName) เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action<string, string?>? StartScreenViewRequested;

    /// <summary>เรียกตอนครูกด "หยุดส่ง" (หรือ Agent หลุด SignalR กลางทาง) - เรียกจาก background thread ไม่ใช่ UI thread</summary>
    public event Action? StopScreenViewRequested;

    /// <summary>
    /// เรียกทุกครั้งที่ได้เฟรมจอครูใหม่ (5-10 ครั้ง/วิ ระหว่างกำลังส่งจอ) - payload = JPEG bytes ที่ decode
    /// จาก Base64 แล้ว เรียกจาก background thread ไม่ใช่ UI thread "ไม่" log ทุกครั้งที่เรียก (ต่างจาก
    /// event อื่นในคลาสนี้ทั้งหมด) เพราะความถี่สูงเกินกว่าจะมีประโยชน์ใน diagnostic log
    /// </summary>
    public event Action<byte[]>? ScreenFrameReceived;

    /// <summary>เริ่มฟัง pipe แบบ fire-and-forget - มี retry loop ในตัว ไม่ throw ออกมาแม้เปิด pipe ไม่สำเร็จ</summary>
    public void Start(CancellationToken ct)
    {
        _ = RunAsync(ct);
        _ = ProcessQueueAsync(ct);
    }

    /// <summary>
    /// ทิ้ง server instance ของรอบปัจจุบันแล้วเปิดใหม่ - ปลอดภัยเรียกจาก thread ไหนก็ได้ และเรียกตอน
    /// ไม่มีอะไรค้างอยู่ก็ไม่เสียหาย (แค่เสีย connection ที่กำลังอ่านค้างอยู่พอดี ซึ่งฝั่ง Agent retry
    /// ให้อยู่แล้ว)
    ///
    /// เรียกจาก App.xaml.cs ตอนเครื่องตื่นจาก sleep (PowerModeChanged.Resume) - named pipe เป็น
    /// kernel object ที่ข้ามการ suspend/resume มาแล้ว "ไม่การันตี" ว่ายังใช้ได้เหมือนเดิม ทั้ง server
    /// handle เองและ overlapped I/O ที่ค้างอยู่ตอนเครื่องหลับ การเปิดใหม่ทั้งชุดตอนตื่นเป็นวิธีเดียวที่
    /// รับประกันได้ว่ากลับมารับคำสั่งได้จริง โดยไม่ต้องรู้ว่ามันพังตรงไหนกันแน่
    /// </summary>
    public void RestartAccept()
    {
        var cts = _acceptCycleCts;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // accept-loop เพิ่ง dispose มันไปพอดีระหว่างนี้ - แปลว่ามันเดินหน้าต่อเองอยู่แล้ว ไม่ต้องทำอะไร
        }
    }

    /// <summary>
    /// รับ connection ทีละอัน - แค่อ่านบรรทัดเดียวแล้วส่งเข้า queue ทันที ไม่เรียก handler ตรงๆ ที่นี่
    /// (ดู doc comment บนสุดของ class ว่าทำไม) ทำให้วนกลับไปรับ connection ถัดไปได้เสมอไม่ว่า message
    /// ก่อนหน้าจะถูกประมวลผลเสร็จหรือยัง
    /// </summary>
    private async Task RunAsync(CancellationToken ct)
    {
        // ---- ACL ของ pipe (แก้ช่องโหว่จาก audit 2026-08-31) ----
        // เดิมเป็น WorldSid (Everyone) ReadWrite - ผู้เขียนที่ถูกต้องมีตัวเดียวคือ Agent service ที่รัน
        // เป็น LocalSystem แต่ ACL เดิมเปิดให้ "ทุก process ของทุก user" เขียนเข้ามาได้ และ
        // HandleMessageLine ก็ประมวลผลทุกบรรทัดที่ได้รับโดยไม่ตรวจว่าใครส่ง - นักเรียนสิทธิ์ปกติจึงยิง
        // {"Type":"HideLockScreen"} เข้ามาปลดล็อกหน้าจอที่ครูสั่งล็อกได้เอง (และ UnblockTargetInput /
        // StopVncSession ก็เช่นกัน) ซึ่งล้มการรับประกันหลักของทั้งฟีเจอร์ว่า "ปลดได้จาก Console เท่านั้น"
        //
        // จำกัดเหลือ 2 หลักฐานตัวตนเท่านั้น:
        //   LocalSystem  = Agent service (ผู้เขียนที่ถูกต้องเพียงรายเดียว)
        //   ตัว user เอง = process นี้ที่เป็นคนสร้าง pipe - ต้องมีสิทธิ์กับ object ของตัวเอง ไม่งั้น
        //                  NamedPipeServerStreamAcl.Create จะสร้างไม่ผ่าน
        // ไม่ใส่ Administrators โดยตั้งใจ: ไม่ได้ช่วยอะไร (admin take ownership ได้อยู่แล้ว) แต่เพิ่ม
        // พื้นที่ให้ process ที่ elevate แล้วปลอมเป็น Agent ได้ฟรีๆ
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        TrayDiagnostics.Log($"[pipe] accept-loop เริ่มทำงาน (readTimeout={ReadTimeout.TotalSeconds}s)");

        while (!ct.IsCancellationRequested)
        {
            var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _acceptCycleCts = cycleCts;
            var cycleCt = cycleCts.Token;

            try
            {
                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);

                await server.WaitForConnectionAsync(cycleCt);

                using var reader = new StreamReader(server);

                // อ่านแบบมี timeout ผ่าน WhenAny ไม่ใช่แค่ส่ง token เข้า ReadLineAsync เฉยๆ - ถ้าการยกเลิก
                // overlapped read ของ pipe ไม่เป็นผลด้วยเหตุผลใดก็ตาม (ซึ่งเป็นสิ่งที่เราไม่มีทางพิสูจน์ได้
                // จากเครื่อง dev) เส้นทางนี้ยังหลุดออกมาได้เสมอ แล้วปล่อยให้ using dispose ตัว server ทิ้ง
                // ซึ่งเป็นสิ่งที่ทำให้ read ที่ค้างอยู่ตายจริงๆ - กันไม่ให้ "loop ค้าง" เป็นไปได้เลยไม่ว่ากรณีใด
                var readTask = reader.ReadLineAsync(cycleCt).AsTask();
                var completed = await Task.WhenAny(readTask, Task.Delay(ReadTimeout, cycleCt));

                if (completed != readTask)
                {
                    // ครบ ReadTimeout แล้วยังไม่ได้บรรทัด (หรือรอบนี้ถูกสั่งยกเลิก) - ทิ้ง connection นี้ไป
                    // แล้วเปิด server ใหม่ ไม่รอต่อ ฝั่ง Agent retry ให้อยู่แล้วถ้ามันยังมีอะไรจะส่งจริง
                    if (!cycleCt.IsCancellationRequested)
                    {
                        TrayDiagnostics.Log(
                            $"[pipe] client ต่อเข้ามาแล้วไม่ส่งข้อมูลภายใน {ReadTimeout.TotalSeconds} วิ - ตัดทิ้งแล้วเปิด server ใหม่");
                    }

                    // ไม่ await readTask ต่อ แต่ต้อง observe exception ของมันไม่ให้กลายเป็น unobserved
                    _ = readTask.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                    continue;
                }

                var line = await readTask;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    TrayDiagnostics.Log($"[pipe] รับ message: {line}");
                    await _messageQueue.Writer.WriteAsync(line, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                TrayDiagnostics.Log("[pipe] accept-loop หยุด (โปรแกรมกำลังปิด)");
                return;
            }
            catch (OperationCanceledException)
            {
                // เฉพาะรอบนี้ถูกยกเลิก (RestartAccept - เช่นตอนเครื่องตื่นจาก sleep) ไม่ใช่ทั้ง listener
                TrayDiagnostics.Log("[pipe] รอบรับ connection ถูกยกเลิก - เปิด server ใหม่");
            }
            catch (Exception ex)
            {
                TrayDiagnostics.Log($"[pipe] รับ connection ไม่สำเร็จ: {ex.GetType().Name}: {ex.Message}");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                _acceptCycleCts = null;
                cycleCts.Dispose();
            }
        }
    }

    /// <summary>
    /// consumer เดี่ยว ประมวลผล message ทีละอันเรียงตามลำดับที่รับมาจริง (คงพฤติกรรมเดิมไว้) แยกออกจาก
    /// accept-loop ข้างบนโดยสิ้นเชิง - ถ้า handler ตัวใดค้าง จะมีแค่ consumer ตัวนี้ที่หยุดเดินหน้าต่อ
    /// (message ถัดไปจะรอคิวอยู่) แต่ accept-loop จะยังรับ connection ใหม่ๆ เข้ามาเก็บใน queue ต่อได้
    /// เรื่อยๆ ไม่ตายไปด้วยกัน
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var line in _messageQueue.Reader.ReadAllAsync(ct))
            {
                HandleMessageLine(line);
            }
        }
        catch (OperationCanceledException)
        {
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

            // WP2b: เก็บ secret ล่าสุดไว้ไม่ว่า message นี้เป็น Type ไหน (ดู LoopbackSecretStore) - ทำให้
            // AgentTray มี secret ตัวล่าสุดเสมอก่อนจะต้อง callback กลับไปที่ /vnc/session-result หรือ
            // /processes/list-result แม้ Agent restart แล้วสุ่มค่าใหม่ไปแล้วก็ตาม
            LoopbackSecretStore.Update(envelope.Secret);

            switch (envelope.Type)
            {
                case "Broadcast":
                    if (!string.IsNullOrWhiteSpace(envelope.Message))
                    {
                        MessageReceived?.Invoke(new BroadcastMessageDto { Message = envelope.Message, Title = envelope.Title });
                    }
                    break;

                case "ShowLockScreen":
                    ShowLockScreenRequested?.Invoke(envelope.Message);
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

                case "GetRunningProcesses":
                    if (!string.IsNullOrWhiteSpace(envelope.RequestingConsoleSessionId))
                    {
                        GetRunningProcessesRequested?.Invoke(envelope.RequestingConsoleSessionId);
                    }
                    break;

                case "ExamModeStatus":
                    ExamModeStatusChanged?.Invoke(envelope.ExamModeExpiresAtUtc);
                    break;

                case "SignInStatus":
                    SignInStatusChanged?.Invoke(envelope.RequireLogin ?? false, envelope.Message);
                    break;

                case "StartScreenView":
                    StartScreenViewRequested?.Invoke(
                        envelope.ViewerMode ?? "FullscreenLocked", envelope.TeacherDisplayName);
                    break;

                case "StopScreenView":
                    StopScreenViewRequested?.Invoke();
                    break;

                case "ScreenFrame":
                    if (!string.IsNullOrEmpty(envelope.FrameBase64))
                    {
                        try
                        {
                            ScreenFrameReceived?.Invoke(Convert.FromBase64String(envelope.FrameBase64));
                        }
                        catch (FormatException)
                        {
                            // เฟรมเพี้ยน (ตัดครึ่งทาง/ไม่ใช่ base64 ที่ถูกต้อง) - ข้ามเฟรมนี้ไปเฉยๆ ไม่ log
                            // (ความถี่สูงเกินกว่าจะมีประโยชน์ - เฟรมถัดไปจะมาเองใน <200ms)
                        }
                    }
                    break;

                default:
                    TrayDiagnostics.Log($"[pipe] ไม่รู้จัก message type: '{envelope.Type}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[pipe] แปลง message ไม่สำเร็จ: {ex.Message}");
        }
    }
}
