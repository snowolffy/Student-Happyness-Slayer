using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ตรวจรหัสฉุกเฉิน (TOTP, RFC 6238) แบบ offline ล้วนๆ ไม่ติดต่อ Server เลย (WP3-4, 2026-10-31) - ตามที่
/// roadmap กำหนดตรงๆ ว่า "Agent ตรวจ TOTP ได้เองโดยไม่ต้องใช้ Server"
///
/// คัดลอกสูตร RFC 6238/4226 มาจาก Server/Services/TotpService.cs เป๊ะๆ โดยตั้งใจ (ไม่ reference
/// โปรเจกต์ Server ข้าม - repo นี้ไม่มี shared library ระหว่าง Agent/Server เหมือนที่ WP3-2 ต้อง
/// คัดลอก Theme folder มาเองเพราะเหตุผลเดียวกัน) secret ที่ใช้ตรวจมาจาก appsettings.json's
/// EmergencySettings:Secret ที่ setup-client.ps1 patch เข้ามาตอนติดตั้ง (MSI property
/// EMERGENCYTOTPSECRET) - ไม่ใช่ secret เดียวกับที่ Server เก็บแบบ sync สดๆ: ครูต้อง copy ค่าจาก
/// Console (ปุ่ม "แสดง QR...") ไปตั้งตอน build/install MSI เอง ทุกครั้งที่ rotate ต้องลง MSI ใหม่ด้วย
///
/// singleton เพราะต้องถือ queue ของ "การใช้รหัสฉุกเฉิน" ที่ยังไม่ได้ส่งขึ้น Server ไว้ข้าม request/
/// loop iteration ทั้งหมด (ดู EmergencyCodeUploadService ที่มาดึงคิวนี้ไปส่งเป็นระยะ) คิวนี้อยู่ใน
/// memory เท่านั้น - Agent restart/reboot ก่อนส่งสำเร็จ = ข้อมูลหาย (ยอมรับตามที่ roadmap บอกไว้ตรงๆ
/// ว่า "เครื่อง lab มี disk freeze ไม่ควรพึ่งการ persist ฝั่ง client")
/// </summary>
public sealed class EmergencyCodeService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const int WindowSteps = 1; // ±1 ช่วง (30 วิ) ตามที่ roadmap ระบุ

    private readonly string? _secretBase32;
    private readonly ILogger<EmergencyCodeService> _logger;
    private readonly ConcurrentQueue<DateTime> _pendingUsage = new();

    public EmergencyCodeService(IOptions<EmergencySettings> settings, ILogger<EmergencyCodeService> logger)
    {
        _secretBase32 = string.IsNullOrWhiteSpace(settings.Value.Secret) ? null : settings.Value.Secret;
        _logger = logger;

        if (_secretBase32 is null)
        {
            _logger.LogWarning("EmergencySettings:Secret ว่างเปล่า - รหัสฉุกเฉินใช้งานไม่ได้บนเครื่องนี้ (MSI ยังไม่ได้ patch secret เข้ามา)");
        }
    }

    /// <summary>true = เครื่องนี้ตั้งค่า secret ไว้แล้ว (AgentTray ใช้ซ่อน/แสดงปุ่มรหัสฉุกเฉิน)</summary>
    public bool IsConfigured => _secretBase32 is not null;

    /// <summary>เวลาที่ปลดล่าสุดด้วยรหัสฉุกเฉิน (UTC) - null ถ้ายังไม่เคยปลดเลยตั้งแต่ Agent เริ่มทำงานรอบนี้</summary>
    public DateTime? LastUnlockedAtUtc { get; private set; }

    /// <summary>
    /// ตรวจรหัส 6 หลัก - ถ้าถูก บันทึกเวลาปลดเข้าคิวให้ EmergencyCodeUploadService เอาไปส่ง Server ทีหลัง
    /// คืน false ทันทีถ้าเครื่องนี้ไม่ได้ตั้งค่า secret ไว้ (ไม่ throw)
    /// </summary>
    public bool TryUnlock(string? code)
    {
        if (_secretBase32 is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!Verify(_secretBase32, code, now))
        {
            return false;
        }

        LastUnlockedAtUtc = now;
        _pendingUsage.Enqueue(now);
        _logger.LogWarning("ปลดเครื่องด้วยรหัสฉุกเฉินสำเร็จที่ {Time} UTC - รอส่งขึ้น Server ตอนกลับมาออนไลน์", now);
        return true;
    }

    /// <summary>ดึงรายการที่ค้างส่งทั้งหมดออกจากคิว (ใช้ตอนจะยิงอัปโหลด) - ถ้าส่งไม่สำเร็จผู้เรียกต้อง Requeue เอง</summary>
    public List<DateTime> DequeueAllPending()
    {
        var items = new List<DateTime>();
        while (_pendingUsage.TryDequeue(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>ส่งไม่สำเร็จ - ใส่กลับเข้าคิวเพื่อลองรอบหน้า (ไม่เรียงเวลาใหม่ - ลำดับไม่สำคัญ แค่ไม่อยากทิ้งข้อมูล)</summary>
    public void RequeuePending(IEnumerable<DateTime> items)
    {
        foreach (var item in items)
        {
            _pendingUsage.Enqueue(item);
        }
    }

    // ---- RFC 4226/6238 - พอร์ตมาจาก Server/Services/TotpService.cs เป๊ะๆ (ดู doc comment บนคลาส) ----

    private static bool Verify(string secretBase32, string? code, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim();
        if (code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = Base32Decode(secretBase32);
        var counter = new DateTimeOffset(nowUtc, TimeSpan.Zero).ToUnixTimeSeconds() / StepSeconds;

        for (var i = -WindowSteps; i <= WindowSteps; i++)
        {
            if (ComputeHotp(key, counter + i) == code)
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeHotp(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var mod = (int)Math.Pow(10, Digits);
        return (binaryCode % mod).ToString().PadLeft(Digits, '0');
    }

    private static byte[] Base32Decode(string base32)
    {
        var cleaned = base32.Trim().TrimEnd('=').Replace(" ", "").Replace("-", "").ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        int bitBuffer = 0, bitsInBuffer = 0;

        foreach (var c in cleaned)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
