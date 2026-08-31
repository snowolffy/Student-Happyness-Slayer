using System.Diagnostics;
using System.Security.Cryptography;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เก็บ "ข้อเท็จจริง" ของโปรแกรมหนึ่งตัวเพื่อส่งไปให้ Server จำแนก (2026-08-31)
///
/// ทุก method ในนี้ต้องไม่ throw ออกไปข้างนอกเด็ดขาด - ถูกเรียกจาก hot path ของการตรวจ process
/// ซึ่งเจอ process ที่อ่านอะไรไม่ได้เลยเป็นเรื่องปกติ (system process, process ที่ตายไปแล้วระหว่าง
/// ที่กำลังอ่าน, ไฟล์ที่ถูก lock) ข้อมูลที่ขาดไปแค่ทำให้คะแนนต่ำลง ไม่ควรทำให้การตรวจทั้งรอบล้ม
/// </summary>
public sealed class ProgramFingerprinter
{
    /// <summary>
    /// ไม่ hash ไฟล์ที่ใหญ่เกินนี้ (250 MB) - เกม AAA มีไฟล์ .exe ใหญ่ได้จริง แต่การอ่านทั้งไฟล์
    /// จากดิสก์ของเครื่อง lab ทุกครั้งที่เห็น process ใหม่จะกินเวลาและ I/O มากเกินไป
    /// ไฟล์ที่ใหญ่กว่านี้ยังส่ง fingerprint ไปได้ตามปกติ แค่ไม่มี hash ให้ผูก verdict ระยะยาว
    /// (จะถูกจำแนกใหม่ทุกครั้ง ซึ่งยังทำงานถูก แค่ไม่ประหยัด)
    /// </summary>
    private const long MaxHashFileSizeBytes = 250L * 1024 * 1024;

    /// <summary>เก็บชื่อไฟล์ข้างเคียงแค่เท่านี้ - ต้องการแค่พอเจอ marker ไม่ได้ต้องการสารบัญทั้งโฟลเดอร์</summary>
    private const int MaxSiblingFiles = 60;

    private const int MaxLoadedModules = 120;

    private readonly ILogger<ProgramFingerprinter> _logger;

    public ProgramFingerprinter(ILogger<ProgramFingerprinter> logger)
    {
        _logger = logger;
    }

    public ProgramFingerprintDto? Build(string executablePath, Process? process, PublisherCheckResult publisherResult)
    {
        try
        {
            var fileInfo = new FileInfo(executablePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var fingerprint = new ProgramFingerprintDto
            {
                FileName = fileInfo.Name,
                FullPath = executablePath,
                FileSizeBytes = fileInfo.Length,
                MachineName = Environment.MachineName,
                SignerName = publisherResult.IsSignedMatch ? publisherResult.PublisherName : null,
                CompanyName = publisherResult.PublisherName,
                IsSigned = publisherResult.IsSignedMatch,
                Sha256 = ComputeSha256(executablePath, fileInfo.Length)
            };

            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                fingerprint.ProductName = versionInfo.ProductName;
                fingerprint.FileDescription = versionInfo.FileDescription;
            }
            catch
            {
                // ไฟล์ที่ไม่มี version resource เลย - ปกติสำหรับโปรแกรมที่ compile เองแบบง่ายๆ
            }

            fingerprint.SiblingFiles = ReadSiblingFiles(fileInfo.DirectoryName);
            fingerprint.LoadedModules = ReadLoadedModules(process);

            return fingerprint;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "สร้าง fingerprint ของ {Path} ไม่สำเร็จ", executablePath);
            return null;
        }
    }

    private string ComputeSha256(string path, long length)
    {
        if (length > MaxHashFileSizeBytes)
        {
            return string.Empty;
        }

        try
        {
            // FileShare.ReadWrite|Delete จำเป็น: ไฟล์ .exe ของ process ที่กำลังรันอยู่ถูก map ไว้
            // โดยระบบ การเปิดแบบ share น้อยกว่านี้จะโดน IOException ทันทีในเคสที่พบบ่อยที่สุด
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "คำนวณ SHA-256 ของ {Path} ไม่สำเร็จ", path);
            return string.Empty;
        }
    }

    private static List<string> ReadSiblingFiles(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new List<string>();
        }

        try
        {
            return Directory.EnumerateFiles(directory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Take(MaxSiblingFiles)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> ReadLoadedModules(Process? process)
    {
        if (process is null)
        {
            return new List<string>();
        }

        try
        {
            // Process.Modules อ่านไม่ได้ในหลายกรณีที่พบบ่อย (process 32-bit เมื่อเราเป็น 64-bit,
            // process ที่ protected, process ที่เพิ่งตายไป) - ทุกกรณีคืนลิสต์ว่างแล้วปล่อยให้
            // สัญญาณอื่น (path/ไฟล์ข้างเคียง/publisher) ทำงานแทน ไม่ใช่เรื่องผิดปกติที่ต้อง log
            var modules = new List<string>(MaxLoadedModules);
            foreach (ProcessModule module in process.Modules)
            {
                modules.Add(module.ModuleName);
                if (modules.Count >= MaxLoadedModules) break;
            }
            return modules;
        }
        catch
        {
            return new List<string>();
        }
    }
}
