using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OnionProcOparetor.Console.Services;

/// <summary>ผลลัพธ์ของ handshake - ต่อ+authenticate+อ่าน ServerInit (Step 4b) ก่อนจะเริ่ม stream framebuffer ได้ (Step 4c ผ่าน RunFramebufferUpdateLoopAsync)</summary>
public sealed class RfbHandshakeResult
{
    public required bool Success { get; init; }
    public string? FailureReason { get; init; }
    public ushort FramebufferWidth { get; init; }
    public ushort FramebufferHeight { get; init; }
    public string? DesktopName { get; init; }
}

/// <summary>
/// Mode การรับภาพจาก server - เปลี่ยนได้ระหว่าง session เดียวกันโดยไม่ต้อง reconnect ใหม่ (ดู
/// RfbClient.RequestModeSwitch) เพราะ Step 4e ต้องรองรับ CCTV-wall grid หลายเครื่องพร้อมกัน (Thumbnail)
/// และคลิกขยายเต็มจอ (FullRes) โดยไม่เสีย connection/handshake เดิม
/// </summary>
public enum VncViewMode
{
    /// <summary>32bpp/depth24 ตรงกับ WPF Bgr32 พอดี (Step 4c/4d เดิม) - คุณภาพเต็ม ไม่มี throttle พิเศษ เหมาะกับดูขยายเต็มจอทีละเครื่อง</summary>
    FullRes,

    /// <summary>16bpp RGB565 บน wire (ลด data size ลงครึ่งหนึ่ง) + downscale ตอน decode ลงขนาดเล็ก + throttle ถี่น้อยลงมาก - เหมาะกับแสดงพร้อมกันหลายเครื่องใน grid</summary>
    Thumbnail
}

/// <summary>
/// หนึ่ง rectangle จาก FramebufferUpdate ที่ decode แล้ว - PixelData เรียงเป็น BGRX ทีละ 4 byte/pixel
/// ตรงกับ PixelFormats.Bgr32 ของ WPF เสมอ (stride = Width * 4) ไม่ว่าจะอยู่ mode ไหนก็ตาม แม้ Thumbnail
/// mode จะเจรจา pixel format บน wire เป็น 16bpp RGB565 กับ server จริงๆ (ประหยัด bandwidth) RfbClient
/// ก็ unpack+downscale กลับเป็น BGR32 ให้ก่อนส่งต่อให้ caller เสมอ - caller (WriteableBitmap consumer)
/// เลยไม่ต้องรู้/สนใจว่า mode ไหนกำลังทำงานอยู่เลย ใช้ contract เดียวกันได้หมด
/// (X/Y/Width/Height อยู่ในพิกัดของ "effective size" ปัจจุบัน - ดู RfbClient.EffectiveWidth/EffectiveHeight:
/// เต็ม framebuffer จริงถ้า FullRes, ขนาด thumbnail ที่ downscale แล้วถ้า Thumbnail)
/// </summary>
public sealed class RfbRectangle
{
    public required ushort X { get; init; }
    public required ushort Y { get; init; }
    public required ushort Width { get; init; }
    public required ushort Height { get; init; }
    public required byte[] PixelData { get; init; }
}

/// <summary>เมาส์ปุ่มไหน ตาม RFC 6143 §7.5.5 button-mask (bit 0=left, bit 1=middle, bit 2=right)</summary>
public enum VncMouseButton
{
    Left,
    Middle,
    Right
}

/// <summary>
/// RFB protocol client (RFC 6143) - เชื่อม TCP ตรงไปยัง winvnc.exe บนเครื่อง Agent เป้าหมาย ไม่พึ่ง
/// library ภายนอกใดๆ (ตามที่ตกลง) Step 4b: ProtocolVersion/Security handshake (เฉพาะ VNC
/// Authentication - type 2 เพราะ UltraVNC ที่ VncSessionManager ตั้งค่าไว้ใช้ type นี้)/ClientInit/
/// ServerInit Step 4c: SetPixelFormat/SetEncodings(Raw เท่านั้น)/FramebufferUpdateRequest loop + decode
/// Raw rectangle Step 4e: VncViewMode.Thumbnail - ลด bit depth บน wire + downscale ตอน decode + throttle
/// หนักกว่าเดิมมาก สำหรับแสดงใน grid หลายเครื่องพร้อมกัน สลับ FullRes/Thumbnail ได้ระหว่าง session เดียวกัน
/// (RequestModeSwitch) ไม่ต้อง reconnect Step 4g (เพิ่มในรอบนี้): PointerEvent/KeyEvent (RFC 6143
/// §7.5.4/§7.5.5) - ยังไม่ผูกกับ UI จริง (mouse capture/coordinate scaling ตาม view คือ Step 4h)
///
/// เชื่อม TCP ตรงจาก Console ไปยัง Agent เครื่องเป้าหมาย ไม่ผ่าน Server เป็น relay เลย (Server แค่ช่วย
/// ส่ง port/password ผ่าน SignalR ให้ตอน StartVncSession เท่านั้น - ดู LabHubClient.VncSessionReady)
///
/// ทำไมไม่ขอ resolution เล็กลงตรงๆ จาก server สำหรับ Thumbnail mode: RFC 6143 §7.5.1 SetPixelFormat มีแค่
/// field เรื่อง pixel encoding (bits-per-pixel/depth/shift ฯลฯ) ไม่มี width/height เลย และ
/// FramebufferUpdateRequest (§7.5.3) เป็นแค่การ "ขอดู region ไหนของ framebuffer ที่มีอยู่แล้ว" ไม่ใช่การ
/// ขอเปลี่ยนขนาด framebuffer ต้นทาง - ไม่มี message type มาตรฐานให้ client ขอ scale ลงได้เลยในสิ่งที่
/// implement ไปแล้ว (ServerInit ส่ง width/height มาครั้งเดียวตอน handshake คงที่ตลอด session) UltraVNC
/// เองมี "Server Scale" ก็จริงแต่เป็น global setting ที่ตั้งจาก winvnc.exe ini/UI (มีผลกับทุก viewer ที่ต่อ
/// เข้ามาเหมือนกันหมด ไม่ใช่ per-connection request) ใช้ไม่ได้กับ requirement ที่ grid (ต้องการเล็ก) กับ
/// full-res popup (ต้องการเต็ม) อาจต้องอยู่พร้อมกันได้ในอนาคต (Step 4f) - เลือกทาง fallback ที่โจทย์เสนอไว้
/// แทน: decode เต็ม resolution ที่ได้จาก server ตามปกติ แล้ว downscale เอาเองฝั่ง client ตอน decode
/// (ดู DownscaleToThumbnailRectangle) - ยังไม่ได้ live-verify กับ UltraVNC จริงว่าไม่มี extension อื่นที่
/// เปิดทางนี้ได้ (เช่น TigerVNC's ExtendedDesktopSize) เพราะเราไม่ได้ implement extension นั้นอยู่แล้ว การ
/// ตัดสินใจนี้มาจากการอ่าน spec ตรงๆ ไม่ใช่จาก live debug เหมือนที่เจอตอน Step 4b (ต่างประเภทของหลักฐาน -
/// บอกไว้ตรงๆ ให้ทวนได้)
/// </summary>
public sealed class RfbClient : IDisposable
{
    private const int ChallengeLength = 16;
    private const int DesKeyLength = 8;
    private const byte VncAuthSecurityType = 2;

    private const byte ClientSetPixelFormatMessageType = 0;
    private const byte ClientSetEncodingsMessageType = 2;
    private const byte ClientFramebufferUpdateRequestMessageType = 3;
    private const byte ClientKeyEventMessageType = 4;
    private const byte ClientPointerEventMessageType = 5;
    private const byte ServerFramebufferUpdateMessageType = 0;
    private const int RawEncodingType = 0;
    private const int FullResBytesPerPixel = 4;
    private const int ThumbnailBytesPerPixel = 2;

    /// <summary>ขอรอบถัดไปห่างจากรอบก่อนหน้าอย่างน้อยเท่านี้ใน Thumbnail mode (เทียบ full-res ที่ขอทันทีหลังประมวลผลเสร็จ) - grid ไม่จำเป็นต้อง real-time</summary>
    private static readonly TimeSpan ThumbnailRequestInterval = TimeSpan.FromSeconds(1.5);

    private readonly int _thumbnailMaxWidth;
    private readonly int _thumbnailMaxHeight;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _framebufferWidth;
    private ushort _framebufferHeight;
    private ushort _thumbnailWidth;
    private ushort _thumbnailHeight;
    private VncViewMode _currentMode = VncViewMode.FullRes;
    private VncViewMode? _pendingMode;
    private bool _loopStarted;

    // Step 4g: input event ส่งจาก thread ที่เรียก (เช่น UI thread ในอนาคต) ขณะที่ framebuffer loop thread
    // (RunFramebufferUpdateLoopAsync) ยัง "อ่าน" จาก stream เดิมพร้อมกันอยู่ - NetworkStream รองรับ 1 reader
    // + 1 writer พร้อมกันได้อย่างปลอดภัยตามปกติ (คนละทิศทาง) แต่ "หลาย writer พร้อมกัน" (เช่น mouse-move
    // กับ key-event มาถึงพร้อมกันจากสอง caller) ไม่ปลอดภัย - ใช้ semaphore ธรรมดาคั่นเฉพาะฝั่งเขียนเท่านั้น
    // (WriteAsync ด้านล่าง) กันการเขียนซ้อนกันโดยไม่ต้องแก้ฝั่งอ่านเลย
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Track ปุ่มเมาส์ที่กำลังถูกกดค้างอยู่ (สำหรับ SendMouseMoveAsync ระหว่างลาก - ต้องส่ง button-mask เดิม
    // ต่อไปเรื่อยๆ ไม่ใช่ 0 เสมอ ไม่งั้นเครื่องปลายทางจะตีความว่าปล่อยปุ่มไปแล้วกลางอากาศ)
    private byte _currentButtonMask;

    /// <summary>thumbnailMaxWidth/Height คือ "งบ" พื้นที่สูงสุด - ขนาด thumbnail จริงคำนวณตอนรู้ ServerInit แล้ว รักษาสัดส่วนภาพเดิมเสมอ (ดู ComputeThumbnailDimensions) ไม่ขยายภาพขึ้นถ้าจอต้นทางเล็กกว่างบอยู่แล้ว</summary>
    public RfbClient(int thumbnailMaxWidth = 320, int thumbnailMaxHeight = 240)
    {
        _thumbnailMaxWidth = thumbnailMaxWidth;
        _thumbnailMaxHeight = thumbnailMaxHeight;
    }

    public ushort FramebufferWidth => _framebufferWidth;
    public ushort FramebufferHeight => _framebufferHeight;

    /// <summary>ขนาด thumbnail ที่คำนวณไว้แล้วหลัง ConnectAndHandshakeAsync สำเร็จ (รักษาสัดส่วนภาพเดิม - ดู ComputeThumbnailDimensions) นิ่งตลอด session ไม่เปลี่ยนตาม mode ปัจจุบัน - ใช้คำนวณขนาด WriteableBitmap ล่วงหน้าได้แม้ยังไม่เริ่ม RunFramebufferUpdateLoopAsync (ต่างจาก EffectiveWidth/Height ด้านล่างที่ขึ้นกับ mode ที่ loop กำลังรันอยู่จริง)</summary>
    public ushort ThumbnailWidth => _thumbnailWidth;
    public ushort ThumbnailHeight => _thumbnailHeight;

    public VncViewMode CurrentMode => _currentMode;

    /// <summary>
    /// ขนาดภาพปัจจุบันตาม mode ที่ effective อยู่จริง (ไม่ใช่แค่ framebuffer เต็ม) - caller ใช้ตั้งขนาด
    /// WriteableBitmap ให้ตรง **ระวัง**: ค่านี้อ่านจาก field ภายในที่ loop thread เป็นคนตั้ง (ตอนเข้า
    /// RunFramebufferUpdateLoopAsync ครั้งแรก) ถ้า caller อ่านค่านี้ "ก่อน" loop เริ่มรันจริง (เช่นทันทีที่
    /// handshake สำเร็จ หรือทันทีที่ Status เปลี่ยนเป็น Connected แต่ยังไม่ทัน await
    /// RunFramebufferUpdateLoopAsync) จะยังได้ค่า default (FullRes) เสมอไม่ว่าตั้งใจเปิด mode ไหนไว้จริง
    /// - ถ้าต้องรู้ขนาดล่วงหน้าก่อน loop เริ่ม ให้คำนวณเองจาก mode ที่ตั้งใจ + FramebufferWidth/Height หรือ
    /// ThumbnailWidth/Height แทน (ดู VncSessionHandle.DisplayWidth/DisplayHeight ที่ทำแบบนี้อยู่)
    /// </summary>
    public ushort EffectiveWidth => _currentMode == VncViewMode.Thumbnail ? _thumbnailWidth : _framebufferWidth;
    public ushort EffectiveHeight => _currentMode == VncViewMode.Thumbnail ? _thumbnailHeight : _framebufferHeight;

    /// <summary>เรียกทุก step ของ handshake พร้อมข้อความละเอียด (debug ง่ายถ้าติด ตามที่ขอ) - เรียกจาก thread ที่เรียก ConnectAndHandshakeAsync เอง ไม่ใช่ background thread แยก</summary>
    public event Action<string>? LogMessage;

    public async Task<RfbHandshakeResult> ConnectAndHandshakeAsync(string host, int port, string password, CancellationToken ct = default)
    {
        try
        {
            Log($"กำลังเชื่อมต่อ TCP ไปยัง {host}:{port}...");
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, ct);
            _stream = _tcpClient.GetStream();
            Log("เชื่อมต่อ TCP สำเร็จ");

            var serverVersion = await DoProtocolVersionHandshakeAsync(ct);

            var securityResult = await DoSecurityHandshakeAsync(ct);
            if (!securityResult.Success)
            {
                return Fail(securityResult.FailureReason!);
            }

            var authResult = await DoVncAuthenticationAsync(password, ct);
            if (!authResult.Success)
            {
                return Fail(authResult.FailureReason!);
            }

            await SendClientInitAsync(ct);
            var serverInit = await ReadServerInitAsync(ct);
            _framebufferWidth = serverInit.Width;
            _framebufferHeight = serverInit.Height;
            (_thumbnailWidth, _thumbnailHeight) = ComputeThumbnailDimensions(_framebufferWidth, _framebufferHeight);

            Log("Handshake สำเร็จครบทุกขั้นตอน - เรียก RunFramebufferUpdateLoopAsync ต่อเพื่อเริ่ม stream ภาพ");

            return new RfbHandshakeResult
            {
                Success = true,
                FramebufferWidth = serverInit.Width,
                FramebufferHeight = serverInit.Height,
                DesktopName = serverInit.DesktopName
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"Handshake ล้มเหลว: {ex.Message}");
            return Fail(ex.Message);
        }
    }

    // ==================== 1. ProtocolVersion handshake (RFC 6143 §7.1.1) ====================

    /// <summary>
    /// รองรับเฉพาะ RFB 003.008 เท่านั้น (ตามที่ตกลง - ไม่ implement 3.3/3.7 security handshake ที่ต่างออกไป)
    /// ถ้า server offer version ต่ำกว่านี้จะ fail ชัดเจนแทนที่จะเดา behavior ของ version เก่า
    /// </summary>
    private async Task<string> DoProtocolVersionHandshakeAsync(CancellationToken ct)
    {
        var serverVersionBytes = await ReadExactAsync(12, ct);
        var serverVersion = Encoding.ASCII.GetString(serverVersionBytes).TrimEnd('\n', '\r');
        Log($"ได้รับ ProtocolVersion จาก server: \"{serverVersion}\"");

        if (serverVersion != "RFB 003.008")
        {
            throw new InvalidOperationException(
                $"Server เสนอ RFB version '{serverVersion}' - รองรับเฉพาะ RFB 003.008 เท่านั้นในตอนนี้");
        }

        const string clientVersion = "RFB 003.008\n";
        await WriteAsync(Encoding.ASCII.GetBytes(clientVersion), ct);
        Log($"ส่ง ProtocolVersion กลับ: \"{clientVersion.TrimEnd('\n')}\"");

        return serverVersion;
    }

    // ==================== 2. Security handshake (RFC 6143 §7.1.2) ====================

    private async Task<(bool Success, string? FailureReason)> DoSecurityHandshakeAsync(CancellationToken ct)
    {
        var countByte = await ReadExactAsync(1, ct);
        var securityTypeCount = countByte[0];

        if (securityTypeCount == 0)
        {
            var reason = await ReadLengthPrefixedStringAsync(ct);
            Log($"Server ปฏิเสธการเชื่อมต่อตั้งแต่ security handshake: {reason}");
            return (false, reason);
        }

        var securityTypes = await ReadExactAsync(securityTypeCount, ct);
        Log($"Server เสนอ security type: [{string.Join(", ", securityTypes)}] (2 = VNC Authentication)");

        if (!securityTypes.Contains(VncAuthSecurityType))
        {
            var msg = "Server ไม่รองรับ VNC Authentication (type 2) - เป็น type เดียวที่รองรับตอนนี้";
            Log(msg);
            return (false, msg);
        }

        await WriteAsync([VncAuthSecurityType], ct);
        Log("เลือก security type: VNC Authentication (2)");

        return (true, null);
    }

    // ==================== 3. VNC Authentication (RFC 6143 §7.2.2) ====================

    /// <summary>
    /// RFC 6143 บอกแค่ "เข้ารหัส challenge ด้วย DES โดยใช้ password เป็น key (truncate/pad เป็น 8 byte)"
    /// - แต่ไม่ได้พูดถึงการ reverse bit ในแต่ละ byte ของ key เลย ทั้งที่จำเป็นต้องทำเพื่อให้ auth ผ่านกับ
    /// VNC server จริงทุกตัว (รวม UltraVNC) เพราะ implementation ดั้งเดิมของ AT&T ใช้ DES library ที่
    /// bit-order ของ key กลับด้าน แล้วทุก implementation ที่ตามมา (RealVNC/TightVNC/UltraVNC/ฯลฯ) ก็ต้อง
    /// ทำตามเพื่อ interop กันได้ - ไม่ใช่ UltraVNC extension เฉพาะตัว เป็น universal de facto behavior ที่
    /// RFC เขียนไว้ไม่ครบ มี RFC 6143 Errata ID 4951 ยืนยันว่านี่คือช่องโหว่ในตัวเอกสารเอง (verify แล้วจาก
    /// หลาย independent source ก่อน implement ไม่ได้เดาเอง - ดู conversation ตอนถามยืนยันก่อนเขียนไฟล์นี้)
    /// </summary>
    private async Task<(bool Success, string? FailureReason)> DoVncAuthenticationAsync(string password, CancellationToken ct)
    {
        var challenge = await ReadExactAsync(ChallengeLength, ct);
        Log($"ได้รับ VNC Auth challenge ({ChallengeLength} bytes)");

        var response = EncryptChallenge(challenge, password);
        await WriteAsync(response, ct);
        Log("ส่ง VNC Auth response กลับแล้ว - รอผลจาก server");

        var resultBytes = await ReadExactAsync(4, ct);
        var result = ReadUInt32BigEndian(resultBytes);

        if (result == 0)
        {
            Log("Auth result: OK");
            return (true, null);
        }

        // RFB 3.8: SecurityResult ที่ fail มี reason string ตามมาด้วยเสมอ (ต่างจาก 3.3-3.7 ที่ไม่มี -
        // แต่เรารองรับแค่ 3.8 ตามที่ตกลงไว้แล้วด้านบน)
        var reason = await ReadLengthPrefixedStringAsync(ct);
        Log($"Auth result: Fail - {reason}");
        return (false, $"VNC Authentication ล้มเหลว: {reason}");
    }

    private static byte[] EncryptChallenge(byte[] challenge, string password)
    {
        var key = BuildVncDesKey(password);

        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = key;

        using var encryptor = des.CreateEncryptor();
        // ECB เข้ารหัสแต่ละ 8-byte block อิสระจากกัน (ไม่ chain) - เรียกครั้งเดียวกับ 16 byte เต็ม
        // ให้ผลเหมือนเข้ารหัส 2 block แยกกันทุกประการ
        return encryptor.TransformFinalBlock(challenge, 0, challenge.Length);
    }

    /// <summary>Password -> DES key ตาม RFB VNC Authentication: truncate/pad เป็น 8 byte แล้ว reverse bit ของแต่ละ byte (ดู comment ยาวใน DoVncAuthenticationAsync ว่าทำไม)</summary>
    private static byte[] BuildVncDesKey(string password)
    {
        var passwordBytes = Encoding.ASCII.GetBytes(password);
        var key = new byte[DesKeyLength];

        for (var i = 0; i < DesKeyLength; i++)
        {
            key[i] = i < passwordBytes.Length ? ReverseBits(passwordBytes[i]) : (byte)0;
        }

        return key;
    }

    private static byte ReverseBits(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
        return b;
    }

    // ==================== 4. ClientInit / ServerInit (RFC 6143 §7.3.1 / §7.3.2) ====================

    /// <summary>shared-flag = 1 (share desktop, ไม่ kick viewer อื่นที่อาจต่ออยู่ก่อนแล้ว) - เหมาะกับ use case ดูงาน/ตรวจสอบมากกว่า exclusive control</summary>
    private async Task SendClientInitAsync(CancellationToken ct)
    {
        await WriteAsync([1], ct);
        Log("ส่ง ClientInit (shared-flag = 1)");
    }

    private async Task<(ushort Width, ushort Height, string DesktopName)> ReadServerInitAsync(CancellationToken ct)
    {
        var widthBytes = await ReadExactAsync(2, ct);
        var heightBytes = await ReadExactAsync(2, ct);
        var width = ReadUInt16BigEndian(widthBytes);
        var height = ReadUInt16BigEndian(heightBytes);

        // PIXEL_FORMAT 16 byte เต็ม - Step 4b แค่อ่านผ่านให้ stream sync ถูกต้อง ยังไม่ได้ใช้ค่าจริง
        // (จะใช้ตอน decode framebuffer ใน Step 4c/4d)
        _ = await ReadExactAsync(16, ct);

        var desktopName = await ReadLengthPrefixedStringAsync(ct);

        Log($"ได้รับ ServerInit: {width}x{height}, desktop name: \"{desktopName}\"");

        return (width, height, desktopName);
    }

    /// <summary>รักษาสัดส่วนภาพต้นทางเสมอ ไม่ขยายภาพขึ้นถ้าจอต้นทางเล็กกว่างบ (thumbnailMaxWidth/Height) อยู่แล้ว - อย่างน้อย 1x1 เสมอกัน edge case จอประหลาดๆ</summary>
    private (ushort Width, ushort Height) ComputeThumbnailDimensions(ushort serverWidth, ushort serverHeight)
    {
        var scale = Math.Min((double)_thumbnailMaxWidth / serverWidth, (double)_thumbnailMaxHeight / serverHeight);
        scale = Math.Min(scale, 1.0);

        var width = (ushort)Math.Max(1, Math.Round(serverWidth * scale));
        var height = (ushort)Math.Max(1, Math.Round(serverHeight * scale));
        return (width, height);
    }

    // ==================== 5. Framebuffer streaming (RFC 6143 §7.5.1-7.5.3, §7.6.1) - Step 4c/4e ====================

    /// <summary>
    /// เรียกได้ครั้งเดียวหลัง ConnectAndHandshakeAsync สำเร็จเท่านั้น (ใช้ stream เดิม ต่อ handshake เดิม)
    /// ทำ SetPixelFormat -> SetEncodings(Raw) -> FramebufferUpdateRequest(incremental=0, เต็มจอ) แล้ว loop
    /// อ่าน FramebufferUpdate ทีละรอบ decode Raw rectangle ส่งต่อให้ onRectangle แล้วขอรอบถัดไป
    ///
    /// FullRes mode: ขอ incremental=1 ทันทีที่ประมวลผลรอบก่อนหน้าเสร็จ (ไม่ใช้ timer แยก - RFB spec เอง
    /// ทำงานแบบ "ค้างรอจนกว่าจะมีอะไรเปลี่ยน" อยู่แล้วสำหรับ incremental request ทำให้ได้ throttling
    /// ตามธรรมชาติ) Thumbnail mode: รอให้ครบ ThumbnailRequestInterval ก่อนขอรอบถัดไปเสมอ (grid ไม่จำเป็น
    /// ต้อง real-time เท่า full-res) mode ปัจจุบันเช็คใหม่ทุกรอบ (ไม่ capture ค่าตอนเริ่ม loop) เพื่อรองรับ
    /// RequestModeSwitch ที่อาจถูกเรียกจาก thread อื่นระหว่างที่ loop นี้กำลังรันอยู่
    ///
    /// onRectangle ถูกเรียกบน thread เดียวกับที่ loop นี้รันอยู่ (ไม่ marshal เอง) - ผู้เรียกต้องรัน
    /// method นี้ผ่าน Task.Run เองถ้าต้องการให้ decode/callback ไม่ block UI thread (ดู
    /// DashboardWindow.StreamVncAsync ที่ marshal กลับ UI thread เฉพาะตอนเขียนลง WriteableBitmap
    /// ผ่าน Dispatcher เท่านั้น)
    /// </summary>
    public async Task RunFramebufferUpdateLoopAsync(VncViewMode initialMode, Action<RfbRectangle> onRectangle, CancellationToken ct)
    {
        _currentMode = initialMode;
        _loopStarted = true;

        await SendSetPixelFormatAsync(_currentMode, ct);
        await SendSetEncodingsAsync(ct);
        await SendFramebufferUpdateRequestAsync(incremental: false, 0, 0, _framebufferWidth, _framebufferHeight, ct);
        var lastRequestSentAt = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // ต้องอ่าน response ของ request ที่ส่งไปก่อนหน้าให้ครบก่อนเสมอ (ตรงกับ _currentMode ที่ยังไม่
            // เปลี่ยนตอนนี้) ก่อนจะพิจารณาสลับ mode - **บั๊กที่เจอจริงตอนเทส Step 4e**: เดิมโค้ดเช็ค/apply
            // การสลับ mode (ส่ง SetPixelFormat + request ใหม่) ไว้ที่ "ต้นลูป" ก่อนอ่าน response ทำให้มี
            // request ค้างอยู่ในสาย TCP พร้อมกัน 2 ชุด (ตัวที่ส่งท้ายลูปรอบก่อนหน้า ยังไม่ทันอ่าน + ตัวใหม่ที่
            // เพิ่งส่งตอนสลับ mode) server ตอบกลับตามลำดับที่ส่งมา แต่ reply แรกยังเป็น pixel format เก่าอยู่
            // (server เพิ่งได้ SetPixelFormat ใหม่ ยังไม่ทันมีผลกับ response ที่ค้างคิวอยู่ก่อน) โค้ดกลับ
            // ไปตีความด้วย _currentMode ใหม่ทันที (bytes-per-pixel ผิด) ทำให้ stream desync ทันที (อ่าน
            // header ผิดตำแหน่ง ได้ "message type" มั่วๆ เช่น 111/139 ที่จริงเป็นแค่ pixel data ที่อ่านผิด
            // ตำแหน่ง) - verify จาก log จริงตอน debug (เห็น exception ทันทีหลัง "ส่ง SetPixelFormat: FullRes
            // mode" พอดี ก่อนจะทันส่ง FramebufferUpdateRequest รอบใหม่ด้วยซ้ำ) แก้โดยรับประกันว่ามี request
            // ค้างอยู่ในสายได้แค่ 1 ชุดเสมอ (อ่านให้ครบก่อน แล้วค่อยสลับ mode แล้วค่อยส่งรอบถัดไป)
            var rectangles = await ReadFramebufferUpdateAsync(ct);
            foreach (var rectangle in rectangles)
            {
                onRectangle(rectangle);
            }

            // ตอนนี้ไม่มี request ค้างอยู่แล้ว (อ่านครบตามที่ส่งไปแล้ว) ปลอดภัยที่จะสลับ mode ก่อนส่ง request
            // รอบถัดไปเพียง 1 ชุด - มีแค่ loop thread นี้เท่านั้นที่เขียนลง stream ได้ (กัน concurrent write
            // race) RequestModeSwitch จาก thread อื่นแค่ตั้ง _pendingMode ไว้ ตรงนี้เป็นจุดเดียวที่ apply จริง
            var justSwitchedMode = false;
            if (_pendingMode is { } pendingMode && pendingMode != _currentMode)
            {
                _currentMode = pendingMode;
                Log($"สลับเป็น {_currentMode} mode (ไม่ reconnect - ใช้ TCP connection/handshake เดิม)");
                await SendSetPixelFormatAsync(_currentMode, ct);
                justSwitchedMode = true;
            }
            _pendingMode = null;

            if (_currentMode == VncViewMode.Thumbnail && !justSwitchedMode)
            {
                var remaining = ThumbnailRequestInterval - (DateTime.UtcNow - lastRequestSentAt);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, ct);
                }
            }

            lastRequestSentAt = DateTime.UtcNow;
            // เพิ่ง switch mode มา -> ขอเต็มจอใหม่ (incremental=false) เสมอ เพื่อเติม buffer ปลายทางขนาด
            // ใหม่ให้ครบตั้งแต่ต้น (ต่างจากปกติที่ขอ incremental=true ต่อเนื่อง)
            await SendFramebufferUpdateRequestAsync(incremental: !justSwitchedMode, 0, 0, _framebufferWidth, _framebufferHeight, ct);
        }
    }

    /// <summary>Overload เดิมจาก Step 4c/4d - เริ่มที่ FullRes mode เสมอ (พฤติกรรมเดิมทุกประการ) เก็บไว้ให้โค้ดที่มีอยู่แล้วเรียกได้โดยไม่ต้องแก้ (ดู DashboardWindow.StreamVncAsync) โค้ดใหม่ (Step 4e+ เช่น VncSessionPool) ควรเรียก overload ที่รับ initialMode ตรงๆ แทน</summary>
    public Task RunFramebufferUpdateLoopAsync(Action<RfbRectangle> onRectangle, CancellationToken ct) =>
        RunFramebufferUpdateLoopAsync(VncViewMode.FullRes, onRectangle, ct);

    /// <summary>
    /// ขอสลับ mode ระหว่าง loop กำลังรันอยู่ - ไม่ reconnect ใหม่ (ใช้ TCP connection/handshake เดิม)
    /// ทำงานแบบ eventually-consistent เท่านั้น (ตั้ง flag แล้ว return ทันที ไม่รอผลจริง) เพราะมีแค่ loop
    /// thread เดียวเท่านั้นที่เขียนลง stream ได้ปลอดภัย (กัน concurrent write race) loop จะ apply การ
    /// เปลี่ยนจริงในรอบถัดไปเอง (ดู RunFramebufferUpdateLoopAsync) - เพียงพอสำหรับตอนนี้เพราะยังไม่มี UI
    /// ที่ต้องรอผลแบบ synchronous (Step 4f ถ้าจำเป็นต้องรู้ผลแน่ชัดค่อยเพิ่ม callback/event ทีหลัง)
    /// </summary>
    public void RequestModeSwitch(VncViewMode newMode)
    {
        if (!_loopStarted)
        {
            throw new InvalidOperationException("เรียก RequestModeSwitch ได้หลัง RunFramebufferUpdateLoopAsync เริ่มแล้วเท่านั้น");
        }

        _pendingMode = newMode;
    }

    /// <summary>
    /// กำหนด pixel format ที่ต้องการรับตาม mode: FullRes = 32bpp/depth24/little-endian/true-colour,
    /// red-shift 16/green-shift 8/blue-shift 0 - จงใจเลือกให้ตรงกับ byte layout ของ PixelFormats.Bgr32
    /// ใน WPF พอดี (B,G,R,padding เรียงจาก address ต่ำไปสูงเมื่อเป็น little-endian) เพื่อเขียน pixel data
    /// ที่ได้ลง WriteableBitmap ตรงๆ ไม่ต้องมี loop แปลง byte order/สลับ channel เพิ่มเลย
    ///
    /// Thumbnail = 16bpp RGB565 (มาตรฐาน RFB pixel format ธรรมดา ไม่ใช่ UltraVNC extension) ลด data size
    /// ต่อ pixel ลงครึ่งหนึ่งเทียบกับ 32bpp เลือก 16-bit true-colour แทน 8-bit indexed/palette เพราะ 8-bit
    /// ต้องพึ่ง SetColourMapEntries (message type แยกอีกอันที่ต้อง sync colour map กับ server ก่อน ไม่ได้
    /// implement ไว้) เพิ่ม protocol complexity ไม่คุ้มกับ bandwidth ที่ประหยัดเพิ่มจาก 16-bit ไปอีก - RFC
    /// 6143 §7.5.1
    /// </summary>
    private async Task SendSetPixelFormatAsync(VncViewMode mode, CancellationToken ct)
    {
        var msg = new byte[20];
        msg[0] = ClientSetPixelFormatMessageType;
        // msg[1..3] padding

        if (mode == VncViewMode.Thumbnail)
        {
            msg[4] = 16; // bits-per-pixel
            msg[5] = 16; // depth
            msg[6] = 0;  // big-endian-flag = false
            msg[7] = 1;  // true-colour-flag = true
            WriteUInt16BigEndian(msg, 8, 31);   // red-max (5 bit)
            WriteUInt16BigEndian(msg, 10, 63);  // green-max (6 bit)
            WriteUInt16BigEndian(msg, 12, 31);  // blue-max (5 bit)
            msg[14] = 11; // red-shift
            msg[15] = 5;  // green-shift
            msg[16] = 0;  // blue-shift
        }
        else
        {
            msg[4] = 32; // bits-per-pixel
            msg[5] = 24; // depth
            msg[6] = 0;  // big-endian-flag = false
            msg[7] = 1;  // true-colour-flag = true
            WriteUInt16BigEndian(msg, 8, 255);  // red-max
            WriteUInt16BigEndian(msg, 10, 255); // green-max
            WriteUInt16BigEndian(msg, 12, 255); // blue-max
            msg[14] = 16; // red-shift
            msg[15] = 8;  // green-shift
            msg[16] = 0;  // blue-shift
        }
        // msg[17..19] padding

        await WriteAsync(msg, ct);
        Log($"ส่ง SetPixelFormat: {mode} mode ({(mode == VncViewMode.Thumbnail ? "16bpp RGB565" : "32bpp depth24")}, ตรงกับ WPF Bgr32 หลัง decode เสมอ)");
    }

    /// <summary>รองรับแค่ Raw encoding (type 0) เท่านั้นในตอนนี้ (Hextile/Tight/ZRLE ยังไม่ implement) - RFC 6143 §7.5.2</summary>
    private async Task SendSetEncodingsAsync(CancellationToken ct)
    {
        var msg = new byte[8];
        msg[0] = ClientSetEncodingsMessageType;
        // msg[1] padding
        WriteUInt16BigEndian(msg, 2, 1); // number-of-encodings
        WriteInt32BigEndian(msg, 4, RawEncodingType);

        await WriteAsync(msg, ct);
        Log("ส่ง SetEncodings: [Raw]");
    }

    /// <summary>RFC 6143 §7.5.3</summary>
    private async Task SendFramebufferUpdateRequestAsync(bool incremental, ushort x, ushort y, ushort width, ushort height, CancellationToken ct)
    {
        var msg = new byte[10];
        msg[0] = ClientFramebufferUpdateRequestMessageType;
        msg[1] = (byte)(incremental ? 1 : 0);
        WriteUInt16BigEndian(msg, 2, x);
        WriteUInt16BigEndian(msg, 4, y);
        WriteUInt16BigEndian(msg, 6, width);
        WriteUInt16BigEndian(msg, 8, height);

        await WriteAsync(msg, ct);
    }

    /// <summary>
    /// อ่าน FramebufferUpdate หนึ่งข้อความเต็ม (header + ทุก rectangle ข้างใน) - RFC 6143 §7.6.1 TCP
    /// เป็น stream ไม่ใช่ message-based จริง ทุกจุดในนี้จึงต้อง ReadExactAsync ให้ครบตาม length ที่ระบุ
    /// เสมอ (ห้ามสมมติว่า 1 read = 1 message/1 rectangle เต็ม - fragment ได้ทุกเมื่อ) bytes-per-pixel บน
    /// wire ขึ้นกับ _currentMode ปัจจุบัน (2 byte สำหรับ Thumbnail/RGB565, 4 byte สำหรับ FullRes/BGR32) -
    /// อ่านถูกต้องเพราะ _currentMode ถูกเปลี่ยน (พร้อมส่ง SetPixelFormat ใหม่) ก่อนเรียก method นี้เสมอ
    /// (ดู RunFramebufferUpdateLoopAsync) ไม่มีทางอ่าน response เก่าด้วย mode ใหม่ผิดจังหวะ
    /// </summary>
    private async Task<List<RfbRectangle>> ReadFramebufferUpdateAsync(CancellationToken ct)
    {
        var header = await ReadExactAsync(4, ct);
        var messageType = header[0];
        if (messageType != ServerFramebufferUpdateMessageType)
        {
            throw new InvalidOperationException(
                $"คาดว่าจะได้ FramebufferUpdate (message type 0) แต่ได้ type {messageType} แทน - ยังไม่รองรับ server-to-client message type อื่น (เช่น Bell/ServerCutText) ในตอนนี้");
        }

        var rectangleCount = ReadUInt16BigEndian(header[2..4]);
        var rectangles = new List<RfbRectangle>(rectangleCount);
        var wireBytesPerPixel = _currentMode == VncViewMode.Thumbnail ? ThumbnailBytesPerPixel : FullResBytesPerPixel;

        for (var i = 0; i < rectangleCount; i++)
        {
            var rectHeader = await ReadExactAsync(12, ct);
            var x = ReadUInt16BigEndian(rectHeader[0..2]);
            var y = ReadUInt16BigEndian(rectHeader[2..4]);
            var width = ReadUInt16BigEndian(rectHeader[4..6]);
            var height = ReadUInt16BigEndian(rectHeader[6..8]);
            var encodingType = ReadInt32BigEndian(rectHeader[8..12]);

            if (encodingType != RawEncodingType)
            {
                // เรา SetEncodings ไปแค่ [Raw] เท่านั้น ถ้า server ยังส่ง encoding อื่นมาแปลว่าไม่ทำตาม spec
                // หรือมี pseudo-encoding ที่ไม่ได้คาดไว้ - หยุด/throw เพื่อรายงานแทนที่จะเดาวิธี decode
                throw new NotSupportedException(
                    $"Server ส่ง rectangle encoding type {encodingType} มา แต่ SetEncodings ระบุแค่ Raw (0) ไปแล้ว - รองรับแค่ Raw เท่านั้นในตอนนี้");
            }

            var pixelByteCount = checked(width * height * wireBytesPerPixel);
            var wirePixelData = pixelByteCount > 0 ? await ReadExactAsync(pixelByteCount, ct) : [];

            var rectangle = _currentMode == VncViewMode.Thumbnail
                ? DownscaleToThumbnailRectangle(x, y, width, height, wirePixelData)
                : new RfbRectangle { X = x, Y = y, Width = width, Height = height, PixelData = wirePixelData };

            if (rectangle is not null)
            {
                rectangles.Add(rectangle);
            }
        }

        return rectangles;
    }

    /// <summary>
    /// Decode 16bpp RGB565 rectangle จาก server (เต็ม resolution ของ region นั้นเป๊ะๆ ตาม x/y/width/height
    /// ที่ server บอก) แล้ว downscale ลงตามอัตราส่วนของ thumbnail เทียบกับ framebuffer เต็มทันที ก่อนส่งต่อ
    /// ให้ caller - ไม่เก็บ full-res buffer ค้างไว้เลยเพราะแต่ละ rectangle ที่ server ส่งมาเป็น
    /// self-contained อยู่แล้ว (มี pixel data ของตัวเองครบพอจะ downscale ได้ตรงๆ ไม่ต้องพึ่งข้อมูลจาก
    /// rectangle อื่นก่อนหน้าเลย) ใช้ nearest-neighbor sampling (เร็ว/ง่าย พอสำหรับ preview เล็กๆ ใน grid
    /// ไม่ต้องการคุณภาพระดับ bilinear) output เป็น BGR32 เสมอ (ไม่ใช่ RGB565 แบบที่รับมา) เพื่อให้ caller
    /// ใช้ RfbRectangle contract เดียวกับ FullRes mode ได้เลยไม่ต้องแยก branch ตาม mode คืน null ถ้า
    /// rectangle เล็กเกินจนไม่มี pixel ปลายทางเหลือเลยหลัง downscale (เช่น cursor blink 1x1 บนจอใหญ่มาก)
    /// </summary>
    private RfbRectangle? DownscaleToThumbnailRectangle(ushort srcX, ushort srcY, ushort srcWidth, ushort srcHeight, byte[] wirePixelData)
    {
        if (srcWidth == 0 || srcHeight == 0 || _framebufferWidth == 0 || _framebufferHeight == 0)
        {
            return null;
        }

        var scaleX = (double)_thumbnailWidth / _framebufferWidth;
        var scaleY = (double)_thumbnailHeight / _framebufferHeight;

        var destX = Math.Clamp((int)Math.Floor(srcX * scaleX), 0, _thumbnailWidth - 1);
        var destY = Math.Clamp((int)Math.Floor(srcY * scaleY), 0, _thumbnailHeight - 1);
        var destRight = Math.Clamp((int)Math.Ceiling((srcX + srcWidth) * scaleX), destX + 1, _thumbnailWidth);
        var destBottom = Math.Clamp((int)Math.Ceiling((srcY + srcHeight) * scaleY), destY + 1, _thumbnailHeight);

        var destWidth = destRight - destX;
        var destHeight = destBottom - destY;
        if (destWidth <= 0 || destHeight <= 0)
        {
            return null;
        }

        var destPixels = new byte[destWidth * destHeight * FullResBytesPerPixel];

        for (var dy = 0; dy < destHeight; dy++)
        {
            // pixel ปลายทาง (destX+dx, destY+dy) ในพิกัด thumbnail เต็มจอ -> map กลับไปหา pixel ต้นทางใน
            // พิกัด framebuffer เต็ม แล้วแปลงเป็น offset ภายใน rectangle นี้เอง (ลบ srcX/srcY ออก) clamp
            // กัน out-of-range ตอนขอบ rectangle ไม่ลงตัวพอดีกับอัตราส่วน scale
            var fullY = (destY + dy) / scaleY;
            var srcRelY = Math.Clamp((int)fullY - srcY, 0, srcHeight - 1);

            for (var dx = 0; dx < destWidth; dx++)
            {
                var fullX = (destX + dx) / scaleX;
                var srcRelX = Math.Clamp((int)fullX - srcX, 0, srcWidth - 1);

                var wireOffset = (srcRelY * srcWidth + srcRelX) * ThumbnailBytesPerPixel;
                var raw = (ushort)(wirePixelData[wireOffset] | (wirePixelData[wireOffset + 1] << 8));

                var r5 = (raw >> 11) & 0x1F;
                var g6 = (raw >> 5) & 0x3F;
                var b5 = raw & 0x1F;
                // ขยาย 5/6-bit -> 8-bit ด้วยการ replicate bit บนสุด (มาตรฐานเดียวกับที่ GPU/graphics library
                // ทั่วไปใช้ ให้ค่า 0 -> 0 และค่าสูงสุด -> 255 พอดี ไม่ใช่แค่ shift เฉยๆ ที่จะได้ค่าสูงสุดไม่ถึง 255)
                var r8 = (byte)((r5 << 3) | (r5 >> 2));
                var g8 = (byte)((g6 << 2) | (g6 >> 4));
                var b8 = (byte)((b5 << 3) | (b5 >> 2));

                var destOffset = (dy * destWidth + dx) * FullResBytesPerPixel;
                destPixels[destOffset] = b8;
                destPixels[destOffset + 1] = g8;
                destPixels[destOffset + 2] = r8;
                destPixels[destOffset + 3] = 0;
            }
        }

        return new RfbRectangle
        {
            X = (ushort)destX,
            Y = (ushort)destY,
            Width = (ushort)destWidth,
            Height = (ushort)destHeight,
            PixelData = destPixels
        };
    }

    // ==================== 6. Input events (RFC 6143 §7.5.4/§7.5.5) - Step 4g ====================
    // ยังไม่ผูกกับ UI จริง (mouse capture จาก Image control/coordinate scaling ตาม view ที่ scale ไว้ -
    // เก็บไว้ Step 4h) ทุก method ด้านล่างรับพิกัด x/y เป็นพิกัดจริงของ framebuffer เป้าหมาย (ไม่ใช่พิกัด
    // ที่ scale ไว้ใน view) - ผู้เรียกต้องแปลงพิกัดเองก่อนเรียก

    /// <summary>RFC 6143 §7.5.4 - down-flag + keysym (X11) ดิบๆ ไม่ผ่าน VncKeyMap (ใช้ตรงถ้ารู้ keysym อยู่แล้ว เช่นทดสอบ) ดู SendKeyPressAsync สำหรับแปลงจาก Windows VK code</summary>
    public Task SendKeyEventAsync(bool isDown, uint keysym, CancellationToken ct = default)
    {
        var msg = new byte[8];
        msg[0] = ClientKeyEventMessageType;
        msg[1] = (byte)(isDown ? 1 : 0);
        // msg[2..3] padding
        WriteUInt32BigEndian(msg, 4, keysym);

        return WriteAsync(msg, ct);
    }

    /// <summary>
    /// แปลง Windows VK code -> X11 keysym ผ่าน VncKeyMap แล้วส่ง KeyEvent - ปุ่มที่ไม่มี mapping (ดู
    /// VncKeyMap.UnmappableVirtualKeys) throw NotSupportedException พร้อมเหตุผลแทนที่จะส่ง keysym ผิดๆ
    /// ไปเงียบๆ (กด key หนึ่งแล้วมีผลเป็นอีก key หนึ่งบนเครื่อง target อันตรายกว่าที่คิด - ตามที่ตกลง)
    /// </summary>
    public Task SendKeyPressAsync(int vkCode, bool isDown, CancellationToken ct = default)
    {
        if (!VncKeyMap.VirtualKeyToKeysym.TryGetValue(vkCode, out var keysym))
        {
            var reason = VncKeyMap.UnmappableVirtualKeys.TryGetValue(vkCode, out var why)
                ? why
                : "ไม่มีอยู่ใน VncKeyMap เลย (ไม่เคยพิจารณา ไม่ใช่ตั้งใจข้าม)";
            throw new NotSupportedException($"VK code 0x{vkCode:X2} ไม่มี X11 keysym mapping ที่ถูกต้อง: {reason}");
        }

        return SendKeyEventAsync(isDown, keysym, ct);
    }

    /// <summary>RFC 6143 §7.5.5 ดิบๆ - button-mask ที่ระบุเองตรงๆ (ใช้ตอน caller ต้องการควบคุม mask เองแบบละเอียด เช่นกดหลายปุ่มพร้อมกัน) x/y เป็นพิกัดจริงของ framebuffer เป้าหมาย</summary>
    public Task SendPointerEventAsync(byte buttonMask, ushort x, ushort y, CancellationToken ct = default)
    {
        var msg = new byte[6];
        msg[0] = ClientPointerEventMessageType;
        msg[1] = buttonMask;
        WriteUInt16BigEndian(msg, 2, x);
        WriteUInt16BigEndian(msg, 4, y);

        return WriteAsync(msg, ct);
    }

    /// <summary>ขยับเมาส์เฉยๆ ไม่กดปุ่มเพิ่ม/ปล่อยปุ่ม - คง button-mask ปัจจุบันไว้ (สำคัญตอนลากเมาส์ขณะกดปุ่มค้าง ไม่งั้นเครื่องปลายทางเข้าใจผิดว่าปล่อยปุ่มไปแล้ว)</summary>
    public Task SendMouseMoveAsync(ushort x, ushort y, CancellationToken ct = default) =>
        SendPointerEventAsync(_currentButtonMask, x, y, ct);

    /// <summary>กด/ปล่อยปุ่มเมาส์ตัวเดียว ที่ตำแหน่งที่ระบุ - อัพเดต button-mask ที่ track ไว้ในตัวด้วย (มีผลกับ SendMouseMoveAsync ครั้งถัดไป)</summary>
    public Task SendMouseButtonAsync(VncMouseButton button, bool isDown, ushort x, ushort y, CancellationToken ct = default)
    {
        var bit = (byte)(1 << (int)button);
        _currentButtonMask = isDown ? (byte)(_currentButtonMask | bit) : (byte)(_currentButtonMask & ~bit);

        return SendPointerEventAsync(_currentButtonMask, x, y, ct);
    }

    /// <summary>Click ปกติ = กดแล้วปล่อยทันที (ไม่หน่วงเวลาเทียม - RFC ไม่ได้กำหนดว่าต้องมี gap ระหว่าง down/up)</summary>
    public async Task SendMouseClickAsync(VncMouseButton button, ushort x, ushort y, CancellationToken ct = default)
    {
        await SendMouseButtonAsync(button, isDown: true, x, y, ct);
        await SendMouseButtonAsync(button, isDown: false, x, y, ct);
    }

    // ==================== I/O helpers ====================

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
            {
                throw new InvalidOperationException($"Server ปิด connection ก่อนส่งครบ (คาดว่า {count} bytes, ได้แค่ {offset})");
            }

            offset += read;
        }

        return buffer;
    }

    private async Task<string> ReadLengthPrefixedStringAsync(CancellationToken ct)
    {
        var lengthBytes = await ReadExactAsync(4, ct);
        var length = ReadUInt32BigEndian(lengthBytes);

        if (length == 0)
        {
            return string.Empty;
        }

        var stringBytes = await ReadExactAsync(checked((int)length), ct);

        // RFB spec ระบุ Latin-1 (ISO 8859-1) สำหรับ string field พวกนี้ (ชื่อเครื่อง/reason) - ยึดตาม
        // spec แม้บาง implementation ในโลกจริงจะส่ง UTF-8 มาก็ตาม (ชื่อเครื่อง ASCII ล้วนจะได้ผลเหมือนกันทั้งคู่)
        return Encoding.Latin1.GetString(stringBytes);
    }

    private async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream!.WriteAsync(data, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static ushort ReadUInt16BigEndian(byte[] bytes) => (ushort)((bytes[0] << 8) | bytes[1]);

    private static uint ReadUInt32BigEndian(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static int ReadInt32BigEndian(byte[] bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private RfbHandshakeResult Fail(string reason) => new() { Success = false, FailureReason = reason };

    private void Log(string message) => LogMessage?.Invoke(message);

    public void Dispose()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _writeLock.Dispose();
    }
}
