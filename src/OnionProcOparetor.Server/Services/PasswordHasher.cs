using System.Security.Cryptography;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// Hash/verify password ด้วย PBKDF2 (Rfc2898DeriveBytes)
/// ไม่เก็บ password จริงเด็ดขาด เก็บแค่ hash + salt
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;       // 128 bit
    private const int HashSize = 32;       // 256 bit
    private const int Iterations = 100_000; // ยิ่งเยอะยิ่งช้าลง brute-force แต่ก็ช้าตอน login ด้วย เลือกค่ากลางๆ

    /// <summary>สร้าง salt แบบสุ่มใหม่ (เรียกครั้งเดียวตอนสร้าง user)</summary>
    public static string GenerateSalt()
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        return Convert.ToBase64String(saltBytes);
    }

    /// <summary>hash password ด้วย salt ที่กำหนด คืนค่าเป็น base64 string เก็บลง DB ได้เลย</summary>
    public static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: saltBytes,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>เทียบ password ที่ user กรอกกับ hash ที่เก็บไว้ ว่าตรงกันไหม</summary>
    public static bool VerifyPassword(string password, string salt, string expectedHash)
    {
        var actualHash = HashPassword(password, salt);
        // ใช้ fixed-time compare กัน timing attack
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(actualHash),
            Convert.FromBase64String(expectedHash));
    }
}
