using System.Security.Cryptography;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// สุ่มรหัสผ่านเริ่มต้นของนักเรียน (WP3-1, 2026-10-31) - ดู docs/roadmap.md "รหัสเริ่มต้น: สุ่ม 6 หลักต่อคน"
/// ใช้ RandomNumberGenerator (crypto-random) แม้เป็นแค่รหัสชั่วคราวที่บังคับเปลี่ยนตอน login ครั้งแรก -
/// เหตุผลเดียวกับ token อื่นในระบบนี้ (StaffSessionTokenStore ฯลฯ) ไม่มีเหตุผลให้ใช้ PRNG ธรรมดาเมื่อ
/// crypto-random มีให้ใช้ฟรีและเร็วพอกันอยู่แล้ว
/// </summary>
public static class StudentPasswordGenerator
{
    /// <summary>สุ่มเลข 6 หลัก "000000"-"999999" (เก็บ leading zero ไว้ - เป็น string ไม่ใช่ int)</summary>
    public static string GenerateRandomSixDigit()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    /// <summary>
    /// รหัสจากวันเกิดรูปแบบ ววดดปปปป (วัน-เดือน-ปี พ.ศ. 4 หลัก) ตาม wireframe nf-password - คืน null ถ้า
    /// ไม่มีวันเกิดให้แปลง (ผู้เรียกต้องจัดการกรณีนี้เอง เช่น fallback ไปสุ่ม 6 หลักแทน)
    /// </summary>
    public static string? FromBirthDate(DateOnly? birthDate)
    {
        if (birthDate is not { } date) return null;

        var buddhistYear = date.Year + 543;
        return $"{date.Day:D2}{date.Month:D2}{buddhistYear:D4}";
    }
}
