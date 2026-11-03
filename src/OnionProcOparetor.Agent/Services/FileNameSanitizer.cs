namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ทำชื่อไฟล์ให้ปลอดภัยสำหรับใช้เป็น "ชื่อไฟล์เดี่ยว" ก่อนเขียนลง Desktop ของ user (WP4-3, 2026-11-01)
///
/// ก๊อปตรรกะเดียวกับ OnionProcOparetor.Server/Services/FileTransferPaths.SanitizeFileName มาไว้ที่นี่
/// (ไม่มี shared class library ระหว่างโปรเจกต์ในโซลูชันนี้เลย - pattern เดียวกับ AgentTrayPipeMessage ที่
/// ก๊อปมือไว้ 2 ที่ ดู doc comment ของไฟล์นั้น) ชื่อไฟล์ที่มาจาก Content-Disposition ของ Server ก็เป็น
/// "ข้อมูลที่เชื่อไม่ได้" เหมือนกัน (Server เองก็ sanitize ไว้ชั้นหนึ่งแล้ว แต่ Agent ต้องไม่เชื่อ 100%
/// - defense-in-depth เป็นเครื่องที่เขียนไฟล์จริงบนดิสก์ของนักเรียน)
/// </summary>
public static class FileNameSanitizer
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private const int MaxFileNameLength = 120;

    public static string Sanitize(string? rawFileName, string defaultFileName = "file")
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return defaultFileName;
        }

        // Path.GetFileName ตัดทุกอย่างก่อน separator ตัวสุดท้ายทิ้งให้เอง (กัน ../, ..\, absolute path)
        var name = Path.GetFileName(rawFileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultFileName;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidFileNameChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }
        name = new string(chars);

        if (name == "." || name == "..")
        {
            return defaultFileName;
        }

        name = name.TrimStart('.');
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultFileName;
        }

        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var keepStemLength = Math.Max(1, MaxFileNameLength - extension.Length);
            name = stem[..Math.Min(stem.Length, keepStemLength)] + extension;
        }

        return name;
    }
}
