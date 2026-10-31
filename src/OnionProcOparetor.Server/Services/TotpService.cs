using System.Security.Cryptography;
using System.Text;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// RFC 6238 (TOTP) + RFC 4226 (HOTP) ล้วนๆ ไม่มี dependency นอก BCL (WP3-4, 2026-10-31) - ใช้ทั้งฝั่ง
/// Server (ตั้งค่า/ทดสอบ/ตรวจรหัสฉุกเฉิน) เดียวกันนี้ Agent มีสำเนาของตัวเองใน
/// Agent/Services/EmergencyCodeService.cs (ไม่มี shared library ระหว่าง 2 โปรเจกต์นี้ในระบบ - ดู
/// comment ของ WP3-2 ใน docs/roadmap.md ว่าทำไม) และ docs/apps-script/emergency-code.gs พอร์ตสูตร
/// เดียวกันนี้ไปเป็น JavaScript - ทั้ง 3 ที่ต้องให้ผลตรงกันเป๊ะๆ สำหรับ secret+เวลาเดียวกัน (ดูสคริปต์
/// ทดสอบ scratch ที่ยืนยันกับ RFC 6238 Appendix B test vectors แล้ว - ไม่ได้ commit ไว้ในนี้)
/// </summary>
public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const int DefaultDigits = 6;
    public const int DefaultStepSeconds = 30;

    /// <summary>สุ่ม secret ใหม่ 20 ไบต์ (160 บิต - ค่ามาตรฐานของ Authenticator ทั่วไป) แล้ว encode เป็น Base32</summary>
    public static string GenerateSecretBase32() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int bitBuffer = 0, bitsInBuffer = 0;
        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(Base32Alphabet[(bitBuffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
        {
            sb.Append(Base32Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1F]);
        }

        return sb.ToString();
    }

    /// <summary>ยอมรับ key ที่มีช่องว่าง/ตัวพิมพ์เล็ก/ขีด (คนพิมพ์ด้วยมือตอนตั้งค่า Apps Script) ตัดตัวที่ไม่ใช่ A-Z2-7 ทิ้งเงียบๆ</summary>
    public static byte[] Base32Decode(string base32)
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

    /// <summary>RFC 4226 HOTP ตรงๆ (dynamic truncation) - ใช้ counter ที่คำนวณจากเวลาแล้ว (TOTP = HOTP ที่ counter = floor(T/step))</summary>
    private static string ComputeHotp(byte[] key, long counter, int digits)
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

        var mod = (int)Math.Pow(10, digits);
        return (binaryCode % mod).ToString().PadLeft(digits, '0');
    }

    public static long CounterFor(DateTimeOffset timestamp, int stepSeconds = DefaultStepSeconds) =>
        timestamp.ToUnixTimeSeconds() / stepSeconds;

    /// <summary>รหัส TOTP ปัจจุบัน (ใช้ตอน "ทดสอบรหัส" ฝั่ง Console แสดงตัวอย่าง/debug เท่านั้น)</summary>
    public static string ComputeCode(string secretBase32, DateTimeOffset timestamp, int digits = DefaultDigits, int stepSeconds = DefaultStepSeconds) =>
        ComputeHotp(Base32Decode(secretBase32), CounterFor(timestamp, stepSeconds), digits);

    /// <summary>
    /// ตรวจรหัสที่ครูพิมพ์มา ยอมคลาดเคลื่อน ±window ช่วง (ตามที่ roadmap ระบุ ±1 = 30 วิ ก่อน/หลัง)
    /// กันเรื่องนาฬิกาเครื่อง/มือถือไม่ตรงกันเป๊ะๆ และเวลาที่ครูพิมพ์กว่าจะกดส่ง
    /// </summary>
    public static bool Verify(string secretBase32, string? code, DateTimeOffset now, int window = 1, int digits = DefaultDigits, int stepSeconds = DefaultStepSeconds)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim();
        if (code.Length != digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = Base32Decode(secretBase32);
        var counter = CounterFor(now, stepSeconds);

        for (var i = -window; i <= window; i++)
        {
            if (ComputeHotp(key, counter + i, digits) == code)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>otpauth:// URI มาตรฐาน - ใช้ generate QR ฝั่ง Console (QRCoder) ให้ Authenticator สแกนได้ตรงๆ</summary>
    public static string BuildOtpAuthUri(string secretBase32, string issuer, string account, int digits = DefaultDigits, int stepSeconds = DefaultStepSeconds) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&digits={digits}&period={stepSeconds}&algorithm=SHA1";
}
