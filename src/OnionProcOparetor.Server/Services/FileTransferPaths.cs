using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// ตรรกะ path/ชื่อไฟล์ทั้งหมดของแจก/เก็บไฟล์ (WP4-3, 2026-11-01) แยกเป็น static ล้วนๆ ไม่พึ่ง DbContext/
/// HttpContext เลย เพื่อ unit test ได้ตรงๆ (ดู pattern เดียวกับ OpenUrlValidation/LaunchProgramValidation
/// ของ WP4-1 - โปรเจกต์นี้ไม่มี test project ใน solution เลย ต้องทดสอบผ่าน throwaway console app แยก)
///
/// **จุดสำคัญที่สุดของทั้งไฟล์**: ชื่อไฟล์ที่มาจากครู/นักเรียน (OpenFileDialog, ชื่อไฟล์ที่อัปโหลดมาจาก
/// multipart form) เป็น "ข้อมูลที่เชื่อไม่ได้" เสมอ - อาจมี "..", "/", "\", หรือ absolute path ปลอมมา
/// (เช่น "..\\..\\..\\Windows\\System32\\evil.exe") ทุกจุดที่เอาชื่อไฟล์ไปต่อเป็น path จริงต้องผ่าน
/// SanitizeFileName ก่อนเสมอ ไม่มีข้อยกเว้น แล้วตรวจซ้ำอีกชั้นด้วย IsPathInsideBase ก่อนเขียนดิสก์จริง
/// (defense-in-depth - แม้ sanitize จะถูกต้อง 100% แล้วก็ตาม)
/// </summary>
public static class FileTransferPaths
{
    private static readonly char[] InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

    /// <summary>ความยาวชื่อไฟล์สูงสุดหลัง sanitize (กัน path ยาวเกิน MAX_PATH ตอนรวมกับ folder ลึกๆ)</summary>
    private const int MaxFileNameLength = 120;

    /// <summary>
    /// ชื่ออุปกรณ์สงวนของ Windows (device name) - ใช้เป็นชื่อไฟล์/โฟลเดอร์ "เฉพาะส่วน stem" ไม่ได้เลย
    /// ไม่ว่าจะมี extension ต่อท้ายหรือไม่ (เช่น "CON", "CON.txt", "com1.log" ล้วนสร้างไฟล์ไม่ได้จริง
    /// บน Windows - ชนกับ device namespace ไม่ใช่ filesystem) GetInvalidFileNameChars ไม่ครอบคลุมเคสนี้
    /// ให้เลย ต้องเช็คแยก
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// ทำชื่อไฟล์ดิบ (จากครู/นักเรียน - เชื่อไม่ได้) ให้ปลอดภัยสำหรับใช้เป็น "ชื่อไฟล์เดี่ยว" (ไม่มี
    /// directory separator เหลืออยู่เลย) คืนค่าเสมอ ไม่ throw - ชื่อที่ว่าง/เป็นแค่ ".."/มีแต่อักขระ
    /// ต้องห้ามทั้งหมด จะได้ DefaultFileName แทน
    ///
    /// ทำไมใช้ Path.GetFileName ก่อนเป็นอันดับแรก: มันตัดทุกอย่างก่อน directory separator ตัวสุดท้ายทิ้ง
    /// ให้เอง (ทั้ง "/" และ "\" บน Windows) - "..\\..\\evil.exe" จะเหลือแค่ "evil.exe" ทันที ไม่ต้องเขียน
    /// regex แยก path traversal เองซึ่งเสี่ยงพลาด edge case (เช่น URL-encoded, backslash ผสม forward slash)
    /// </summary>
    public static string SanitizeFileName(string? rawFileName, string defaultFileName = "file")
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return defaultFileName;
        }

        // ตัดทุกอย่างก่อน separator ตัวสุดท้ายทิ้ง (กัน ../, ..\, absolute path ทั้ง "C:\x" และ "\\server\share")
        var name = System.IO.Path.GetFileName(rawFileName.Trim());

        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultFileName;
        }

        // แทนอักขระที่ผิดกฎของชื่อไฟล์ Windows ทั้งหมดด้วย "_" (GetFileName ไม่ได้กรองสิ่งนี้ให้)
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidFileNameChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }
        name = new string(chars);

        // ชื่อที่เหลือแต่ "." หรือ ".." ล้วนๆ (ไม่มีอักขระอื่น) ยังอันตรายอยู่ (Windows ตีความพิเศษ) - กันซ้ำ
        if (name == "." || name == "..")
        {
            return defaultFileName;
        }

        // ตัด "." นำหน้าซ้ำๆ ทิ้ง (ป้องกัน ".." ที่หลุดมาจากการแทนอักขระข้างบนโดยบังเอิญ เช่น "../" ที่ "/"
        // ถูกแทนเป็น "_" กลายเป็น ".._" - ไม่อันตรายแล้วแต่เพื่อความสะอาด) ไม่ตัดจนว่างเปล่าถ้าเป็นแค่ ".."
        name = name.TrimStart('.');
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultFileName;
        }

        // ชื่ออุปกรณ์สงวนของ Windows (CON, PRN, AUX, NUL, COM1-9, LPT1-9) เทียบที่ "stem" (ไม่รวม
        // extension) เพราะ "CON.txt" ก็ชนเหมือนกับ "CON" เปล่าๆ
        if (ReservedDeviceNames.Contains(System.IO.Path.GetFileNameWithoutExtension(name)))
        {
            name = "_" + name;
        }

        if (name.Length > MaxFileNameLength)
        {
            var extension = System.IO.Path.GetExtension(name);
            var stem = System.IO.Path.GetFileNameWithoutExtension(name);
            var keepStemLength = Math.Max(1, MaxFileNameLength - extension.Length);
            name = stem[..Math.Min(stem.Length, keepStemLength)] + extension;
        }

        return name;
    }

    /// <summary>
    /// ทำชื่อนักเรียน/ชื่อเครื่องให้ปลอดภัยสำหรับใช้เป็น "ชื่อโฟลเดอร์เดี่ยว" (เหตุผลเดียวกับ SanitizeFileName
    /// - ชื่อนักเรียนมาจากข้อมูลที่ Admin/ครูกรอกไว้ใน Roster ซึ่งทางทฤษฎีก็พิมพ์อักขระแปลกๆ ได้เหมือนกัน)
    /// </summary>
    public static string SanitizeFolderSegment(string? rawName, string defaultName = "unknown") =>
        SanitizeFileName(rawName, defaultName);

    /// <summary>ที่เก็บไฟล์ effective จริง - StoragePath ว่าง/null ใช้ default (%ProgramData%\...\files)</summary>
    public static string ResolveStorageRoot(FileTransferOptions options) =>
        string.IsNullOrWhiteSpace(options.StoragePath) ? DefaultStorageRoot : options.StoragePath;

    public static readonly string DefaultStorageRoot = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "files");

    /// <summary>path เต็มของไฟล์แจกหนึ่งไฟล์บนดิสก์ - storedFileName มาจาก Server เอง (Guid) ไม่ใช่ input
    /// จากครู จึงไม่ต้อง sanitize ซ้ำ แต่ยังตรวจ IsPathInsideBase ปิดท้ายเสมอ (defense-in-depth)</summary>
    public static string BuildDistributedFilePath(FileTransferOptions options, string storedFileName)
    {
        var root = ResolveStorageRoot(options);
        var dir = System.IO.Path.Combine(root, "distributed");
        var fullPath = System.IO.Path.Combine(dir, storedFileName);

        if (!IsPathInsideBase(fullPath, dir))
        {
            // ไม่ควรเกิดได้จริงเพราะ storedFileName เป็น Guid ที่ server สร้างเอง - throw ทันทีถ้าเกิดขึ้น
            // เพราะแปลว่ามี bug อื่นร้ายแรงกว่าที่คาดไว้ (ดีกว่าเงียบแล้วเขียนไฟล์ผิดที่)
            throw new InvalidOperationException($"StoredFileName '{storedFileName}' สร้าง path ที่หลุดออกจาก '{dir}'");
        }

        return fullPath;
    }

    /// <summary>
    /// path เต็มของไฟล์ที่เก็บได้หนึ่งไฟล์บนดิสก์ - jobName/collectorFolderName/originalFileName ทั้ง 3 ตัว
    /// เป็น input ที่เชื่อไม่ได้ ต้อง sanitize มาแล้วทุกตัวก่อนเรียก method นี้ (ดู FileTransferEndpoints -
    /// เรียก SanitizeFileName/SanitizeFolderSegment ก่อนเสมอ) ตรวจ IsPathInsideBase ปิดท้ายอีกชั้นเหมือนกัน
    /// </summary>
    public static string BuildCollectedFilePath(
        FileTransferOptions options, int jobId, string sanitizedJobName, string sanitizedCollectorFolderName, string sanitizedFileName)
    {
        var root = ResolveStorageRoot(options);
        var jobDir = System.IO.Path.Combine(root, "collected", $"{jobId}_{sanitizedJobName}");
        var collectorDir = System.IO.Path.Combine(jobDir, sanitizedCollectorFolderName);
        var fullPath = System.IO.Path.Combine(collectorDir, sanitizedFileName);

        if (!IsPathInsideBase(fullPath, System.IO.Path.Combine(root, "collected")))
        {
            throw new InvalidOperationException($"ชื่อไฟล์/โฟลเดอร์ที่ sanitize แล้วยังสร้าง path ที่หลุดออกจากที่เก็บได้ (fullPath='{fullPath}')");
        }

        return fullPath;
    }

    /// <summary>path สัมพัทธ์ที่เก็บไว้ใน CollectedFile.StoredRelativePath (ใต้ {root}\collected\)</summary>
    public static string BuildCollectedFileRelativePath(int jobId, string sanitizedJobName, string sanitizedCollectorFolderName, string sanitizedFileName) =>
        System.IO.Path.Combine($"{jobId}_{sanitizedJobName}", sanitizedCollectorFolderName, sanitizedFileName);

    /// <summary>
    /// ตรวจว่า fullPath (หลัง Path.GetFullPath เพื่อ resolve ".."/"." ทั้งหมดแล้ว) ยังอยู่ "ใต้" basePath
    /// จริง - เป็นด่านสุดท้ายกัน path traversal แม้ sanitize ด้านบนจะพลาดอะไรไปก็ตาม (defense-in-depth
    /// ไม่ใช่ด่านเดียวที่พึ่ง)
    /// </summary>
    public static bool IsPathInsideBase(string fullPath, string basePath)
    {
        var resolvedFull = System.IO.Path.GetFullPath(fullPath);
        var resolvedBase = System.IO.Path.GetFullPath(basePath);

        if (!resolvedBase.EndsWith(System.IO.Path.DirectorySeparatorChar))
        {
            resolvedBase += System.IO.Path.DirectorySeparatorChar;
        }

        return resolvedFull.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase);
    }
}
