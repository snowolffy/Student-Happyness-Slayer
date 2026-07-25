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
/// หนึ่ง rectangle จาก FramebufferUpdate ที่ decode แล้ว (Raw encoding เท่านั้น - ดู RunFramebufferUpdateLoopAsync)
/// PixelData เรียงเป็น BGRX ทีละ 4 byte/pixel ตรงกับ PixelFormats.Bgr32 ของ WPF พอดี (stride = Width * 4)
/// เพราะ SetPixelFormat ที่ส่งไปกำหนด little-endian + red-shift 16/green-shift 8/blue-shift 0 ไว้ตรงกัน
/// ตั้งแต่ต้น - เขียนลง WriteableBitmap ได้ตรงๆ ไม่ต้องแปลง byte order/สลับ channel เพิ่ม
/// </summary>
public sealed class RfbRectangle
{
    public required ushort X { get; init; }
    public required ushort Y { get; init; }
    public required ushort Width { get; init; }
    public required ushort Height { get; init; }
    public required byte[] PixelData { get; init; }
}

/// <summary>
/// RFB protocol client (RFC 6143) - เชื่อม TCP ตรงไปยัง winvnc.exe บนเครื่อง Agent เป้าหมาย ไม่พึ่ง
/// library ภายนอกใดๆ (ตามที่ตกลง) Step 4b: ProtocolVersion/Security handshake (เฉพาะ VNC
/// Authentication - type 2 เพราะ UltraVNC ที่ VncSessionManager ตั้งค่าไว้ใช้ type นี้)/ClientInit/
/// ServerInit Step 4c (เพิ่มในรอบนี้): SetPixelFormat/SetEncodings(Raw เท่านั้น)/FramebufferUpdateRequest
/// loop + decode Raw rectangle ยังไม่ทำ input event ใดๆ (mouse/keyboard - Step 4d+)
///
/// เชื่อม TCP ตรงจาก Console ไปยัง Agent เครื่องเป้าหมาย ไม่ผ่าน Server เป็น relay เลย (Server แค่ช่วย
/// ส่ง port/password ผ่าน SignalR ให้ตอน StartVncSession เท่านั้น - ดู LabHubClient.VncSessionReady)
/// </summary>
public sealed class RfbClient : IDisposable
{
    private const int ChallengeLength = 16;
    private const int DesKeyLength = 8;
    private const byte VncAuthSecurityType = 2;

    private const byte ClientSetPixelFormatMessageType = 0;
    private const byte ClientSetEncodingsMessageType = 2;
    private const byte ClientFramebufferUpdateRequestMessageType = 3;
    private const byte ServerFramebufferUpdateMessageType = 0;
    private const int RawEncodingType = 0;
    private const int BytesPerPixel = 4;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _framebufferWidth;
    private ushort _framebufferHeight;

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

            Log("Handshake สำเร็จครบทุกขั้นตอน - เรียก RunFramebufferUpdateLoopAsync ต่อเพื่อเริ่ม stream ภาพ (Step 4c)");

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

    // ==================== 5. Framebuffer streaming (RFC 6143 §7.5.1-7.5.3, §7.6.1) - Step 4c ====================

    /// <summary>
    /// เรียกได้ครั้งเดียวหลัง ConnectAndHandshakeAsync สำเร็จเท่านั้น (ใช้ stream เดิม ต่อ handshake เดิม)
    /// ทำ SetPixelFormat -> SetEncodings(Raw) -> FramebufferUpdateRequest(incremental=0, เต็มจอ) แล้ว loop
    /// อ่าน FramebufferUpdate ทีละรอบ decode Raw rectangle ส่งต่อให้ onRectangle แล้วขอรอบถัดไปแบบ
    /// incremental=1 ทันทีที่ประมวลผลรอบก่อนหน้าเสร็จ (ไม่ใช้ timer แยก - เลือกแบบนี้เพราะ implement ง่ายกว่า
    /// และ RFB spec เองก็ทำงานแบบ "ค้างรอจนกว่าจะมีอะไรเปลี่ยน" อยู่แล้วสำหรับ incremental request ทำให้ได้
    /// throttling ตามธรรมชาติโดยไม่ต้องเขียน timer เพิ่ม)
    ///
    /// onRectangle ถูกเรียกบน thread เดียวกับที่ loop นี้รันอยู่ (ไม่ marshal เอง) - ผู้เรียกต้องรัน
    /// method นี้ผ่าน Task.Run เองถ้าต้องการให้ decode/callback ไม่ block UI thread (ดู
    /// VncTestWindow.StreamFramebufferAsync ที่ marshal กลับ UI thread เฉพาะตอนเขียนลง WriteableBitmap
    /// ผ่าน Dispatcher เท่านั้น)
    /// </summary>
    public async Task RunFramebufferUpdateLoopAsync(Action<RfbRectangle> onRectangle, CancellationToken ct)
    {
        await SendSetPixelFormatAsync(ct);
        await SendSetEncodingsAsync(ct);
        await SendFramebufferUpdateRequestAsync(incremental: false, 0, 0, _framebufferWidth, _framebufferHeight, ct);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var rectangles = await ReadFramebufferUpdateAsync(ct);
            foreach (var rectangle in rectangles)
            {
                onRectangle(rectangle);
            }

            await SendFramebufferUpdateRequestAsync(incremental: true, 0, 0, _framebufferWidth, _framebufferHeight, ct);
        }
    }

    /// <summary>
    /// กำหนด pixel format ที่ต้องการรับ: 32bpp/depth 24/little-endian/true-colour, red-shift 16,
    /// green-shift 8, blue-shift 0 - จงใจเลือกให้ตรงกับ byte layout ของ PixelFormats.Bgr32 ใน WPF
    /// พอดี (B,G,R,padding เรียงจาก address ต่ำไปสูงเมื่อเป็น little-endian) เพื่อเขียน pixel data ที่ได้
    /// ลง WriteableBitmap ตรงๆ ไม่ต้องมี loop แปลง byte order/สลับ channel เพิ่มเลย (RFC 6143 §7.5.1)
    /// </summary>
    private async Task SendSetPixelFormatAsync(CancellationToken ct)
    {
        var msg = new byte[20];
        msg[0] = ClientSetPixelFormatMessageType;
        // msg[1..3] padding
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
        // msg[17..19] padding

        await WriteAsync(msg, ct);
        Log("ส่ง SetPixelFormat: 32bpp depth24 little-endian (R shift16/G shift8/B shift0) - ตรงกับ WPF Bgr32");
    }

    /// <summary>รองรับแค่ Raw encoding (type 0) เท่านั้นในตอนนี้ (Hextile/Tight/ZRLE เก็บไว้ Step 4d+ ตามที่ตกลง) - RFC 6143 §7.5.2</summary>
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
    /// เสมอ (ห้ามสมมติว่า 1 read = 1 message/1 rectangle เต็ม - fragment ได้ทุกเมื่อ)
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

            var pixelByteCount = checked(width * height * BytesPerPixel);
            var pixelData = pixelByteCount > 0 ? await ReadExactAsync(pixelByteCount, ct) : [];

            rectangles.Add(new RfbRectangle { X = x, Y = y, Width = width, Height = height, PixelData = pixelData });
        }

        return rectangles;
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

    private Task WriteAsync(byte[] data, CancellationToken ct) => _stream!.WriteAsync(data, ct).AsTask();

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

    private RfbHandshakeResult Fail(string reason) => new() { Success = false, FailureReason = reason };

    private void Log(string message) => LogMessage?.Invoke(message);

    public void Dispose()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}
