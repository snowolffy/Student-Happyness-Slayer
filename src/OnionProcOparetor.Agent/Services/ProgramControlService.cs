using System.Collections.Concurrent;
using System.Diagnostics;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// ชั้นควบคุมโปรแกรมที่ทำงาน "คู่ขนาน" กับระบบ rule เดิม (2026-08-31)
///
/// ===================== ทำไมต้องมี ทั้งที่มีระบบ rule อยู่แล้ว =====================
/// ระบบ rule เดิมจับได้เฉพาะโปรแกรมที่ครูรู้จักชื่อ publisher ล่วงหน้าและพิมพ์กฎไว้แล้ว
/// และ Worker.CheckAndActOnProcess ก็ออกทันทีถ้าไฟล์ไม่มี publisher เลย - แปลว่าเกมที่ไม่ได้เซ็น
/// และไม่มี CompanyName (ซึ่งคือเกมโหลด/portable ส่วนใหญ่) ไม่เคยปรากฏแม้แต่ใน log
/// ตัวนี้เป็นตาข่ายรับของที่ครูยังไม่รู้จัก ไม่ได้มาแทนที่ rule
///
/// ===================== ลำดับการทำงาน (สำคัญเรื่องความเร็ว) =====================
/// ชั้นที่ 1 - blocklist ในหน่วยความจำ: เจอ hash ที่ตัดสินแล้วว่า Block -> ปิดทันที ไม่มี round trip
///            รายการนี้มาพร้อม poll response (ดู /api/poll ฝั่ง Server) และเป็นความรู้รวมของทั้งห้อง
///            เครื่องแรกที่เจอเกมหนึ่ง ทำให้อีก 39 เครื่องปิดมันได้ทันทีตั้งแต่วินาทีแรกที่เห็น
/// ชั้นที่ 2 - ยังไม่รู้จัก: เก็บเข้าคิว ส่งไปให้ Server จำแนกเป็นชุด แล้วบังคับใช้ผลที่ได้กลับมา
///            (โปรแกรมได้รันไปแล้วช่วงหนึ่ง - ยอมรับ ดูหัวข้อถัดไป)
///
/// ===================== ทำไมชั้นที่ 2 ถึงยอมให้รันไปก่อน =====================
/// ทางเลือกคือ "ห้ามทุกอย่างที่ยังไม่รู้จัก" ซึ่งบนเครื่อง Windows จริงแปลว่าปิดตัวเองตายตั้งแต่บูต
/// (Windows รัน process ที่ไม่เคยเห็นมาก่อนหลายสิบตัวทุกครั้งที่ login) การจำแนกที่ยังไม่มีคำตอบ
/// จึง fail-open โดยตั้งใจ - ต่างจากระบบ rule เดิมที่ fail-secure ได้เพราะมันมีรายการที่แน่นอนอยู่แล้ว
/// ราคาที่จ่ายคือครั้งแรกที่มีคนเปิดเกมใหม่ในห้อง เกมนั้นได้รันประมาณหนึ่งรอบ poll ก่อนถูกปิด
/// และหลังจากนั้นทั้งห้องปิดได้ทันที
/// </summary>
public sealed class ProgramControlService
{
    /// <summary>ส่งให้ Server ทีละไม่เกินเท่านี้ - กันไม่ให้ request บวมตอนบูตเครื่องที่เจอ process ใหม่พรวดเดียว</summary>
    private const int MaxBatchSize = 25;

    /// <summary>เก็บผลที่รู้แล้วไว้ไม่เกินเท่านี้ กัน memory โตไม่จำกัดตลอดอายุ service</summary>
    private const int MaxCachedVerdicts = 5000;

    private readonly ILogger<ProgramControlService> _logger;
    private readonly ProgramFingerprinter _fingerprinter;
    private readonly ServerClient _serverClient;
    private readonly ClientState _state;

    /// <summary>hash ที่ Server บอกว่าให้ปิด - แทนที่ทั้งชุดทุกครั้งที่ poll ส่งเวอร์ชันใหม่มา</summary>
    private volatile HashSet<string> _blockedHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>เวอร์ชันของ blocklist ที่ถืออยู่ - แนบไปกับ poll เพื่อไม่ต้องรับรายการเดิมซ้ำทุก 30 วิ</summary>
    public string? BlocklistVersion { get; private set; }

    /// <summary>ผลที่เคยถามไปแล้ว - กันถามซ้ำเรื่องเดิมทุกรอบ scan (ทุก 10 วิ)</summary>
    private readonly ConcurrentDictionary<string, ProgramAction> _knownVerdicts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>path ที่กำลังรอผลอยู่ - กันส่งซ้ำระหว่างที่ request ก่อนหน้ายังไม่กลับ</summary>
    private readonly ConcurrentDictionary<string, byte> _pendingLookups = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<ProgramFingerprintDto> _classifyQueue = new();

    public ProgramControlService(
        ILogger<ProgramControlService> logger,
        ProgramFingerprinter fingerprinter,
        ServerClient serverClient,
        ClientState state)
    {
        _logger = logger;
        _fingerprinter = fingerprinter;
        _serverClient = serverClient;
        _state = state;
    }

    /// <summary>เรียกจาก Worker ทุกครั้งที่ poll สำเร็จ - อัปเดต blocklist ถ้า Server ส่งชุดใหม่มา</summary>
    public void ApplyBlocklist(string? version, List<string>? hashes)
    {
        if (version is null)
        {
            return;
        }

        // hashes เป็น null = Server บอกว่า "เวอร์ชันเดิม ไม่มีอะไรเปลี่ยน" ไม่ใช่ "ไม่มีรายการเลย"
        // แยก 2 กรณีนี้ให้ออกสำคัญมาก: ถ้าตีความผิดจะกลายเป็นล้าง blocklist ทิ้งทุก 30 วินาที
        if (hashes is not null)
        {
            _blockedHashes = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("รับ blocklist ใหม่จาก Server: {Count} รายการ (เวอร์ชัน {Version})",
                _blockedHashes.Count, version);
        }

        BlocklistVersion = version;
    }

    /// <summary>
    /// ตรวจโปรแกรมหนึ่งตัว คืน true ถ้าจัดการไปแล้ว (ปิดไปแล้ว) - เรียกจาก Worker หลังจากที่
    /// ระบบ rule เดิมไม่ match อะไรเลย
    /// </summary>
    public async Task<bool> InspectAsync(string executablePath, int processId, PublisherCheckResult publisherResult)
    {
        try
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch
            {
                // process ตายไปแล้วระหว่างทาง - ยังทำ fingerprint จากไฟล์ต่อได้ แค่ไม่มีรายการ
                // module ที่โหลดอยู่ (ซึ่งเป็นสัญญาณที่ดีที่สุดตัวหนึ่ง แต่ไม่ใช่ตัวเดียว)
            }

            using (process)
            {
                var fingerprint = _fingerprinter.Build(executablePath, process, publisherResult);
                if (fingerprint is null || string.IsNullOrEmpty(fingerprint.Sha256))
                {
                    return false;
                }

                // ---- ชั้นที่ 1: รู้จักแล้ว ----
                if (_blockedHashes.Contains(fingerprint.Sha256) ||
                    (_knownVerdicts.TryGetValue(fingerprint.Sha256, out var known) && known == ProgramAction.Block))
                {
                    return KillAndReport(executablePath, processId, fingerprint);
                }

                if (_knownVerdicts.ContainsKey(fingerprint.Sha256))
                {
                    return false; // เคยตัดสินแล้วว่า Allow/Flag - ไม่ต้องถามซ้ำ
                }

                // ---- ชั้นที่ 2: ยังไม่รู้จัก - เข้าคิวไปถาม Server ----
                if (_pendingLookups.TryAdd(fingerprint.Sha256, 0))
                {
                    _classifyQueue.Enqueue(fingerprint);
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ตรวจโปรแกรม {Path} ด้วย Program Control ไม่สำเร็จ", executablePath);
            return false;
        }
    }

    /// <summary>
    /// ส่งคิวที่ค้างอยู่ไปให้ Server จำแนก - เรียกจาก Worker หลัง poll แต่ละรอบ
    /// ไม่เรียกจาก InspectAsync ตรงๆ โดยตั้งใจ: InspectAsync อยู่ใน hot path ของ WMI event handler
    /// การยิง HTTP ตรงนั้นแปลว่า process ใหม่ทุกตัวที่เครื่องเปิดกลายเป็น request หนึ่งใบ
    /// </summary>
    public async Task FlushPendingAsync(CancellationToken ct)
    {
        if (_classifyQueue.IsEmpty)
        {
            return;
        }

        var batch = new List<ProgramFingerprintDto>(MaxBatchSize);
        while (batch.Count < MaxBatchSize && _classifyQueue.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        var response = await _serverClient.ClassifyProgramsAsync(batch, ct);
        if (response is null)
        {
            // ติดต่อ Server ไม่ได้ - ปลด pending ให้ลองใหม่รอบหน้า ไม่ใช่ทิ้งไปเงียบๆ
            // (ถ้าไม่ปลด hash เหล่านี้จะไม่มีวันถูกถามอีกเลยตลอดอายุของ process นี้)
            foreach (var item in batch)
            {
                _pendingLookups.TryRemove(item.Sha256, out _);
            }
            return;
        }

        foreach (var item in batch)
        {
            _pendingLookups.TryRemove(item.Sha256, out _);

            if (!response.Verdicts.TryGetValue(item.Sha256, out var actionValue))
            {
                continue;
            }

            var action = (ProgramAction)actionValue;
            RememberVerdict(item.Sha256, action);

            if (action == ProgramAction.Block)
            {
                _logger.LogInformation("Server ตัดสินว่า '{FileName}' ต้องถูกบล็อก - จะปิดทันทีที่พบครั้งถัดไป", item.FileName);
                KillByPath(item.FullPath, item);
            }
            else if (action == ProgramAction.Flag)
            {
                _logger.LogInformation("Server ตั้งธง '{FileName}' ไว้ให้ครูตรวจสอบ (ยังไม่ปิด)", item.FileName);
            }
        }
    }

    private void RememberVerdict(string sha256, ProgramAction action)
    {
        if (_knownVerdicts.Count >= MaxCachedVerdicts)
        {
            // ล้างทั้งหมดแทนที่จะทำ LRU - เครื่อง lab เจอโปรแกรมไม่ซ้ำกันหลักร้อยต่อวัน การแตะเพดาน
            // 5000 แปลว่ามีอะไรผิดปกติอยู่แล้ว และ blocklist ชั้นที่ 1 (ซึ่งเป็นชั้นที่สำคัญจริง)
            // ไม่ได้อยู่ใน dictionary นี้ - การล้างจึงแค่ทำให้ถาม Server ซ้ำ ไม่ได้ทำให้หลุดการบล็อก
            _logger.LogWarning("cache ผลจำแนกเต็ม {Max} รายการ - ล้างทิ้งทั้งหมดแล้วเริ่มใหม่", MaxCachedVerdicts);
            _knownVerdicts.Clear();
        }

        _knownVerdicts[sha256] = action;
    }

    private bool KillAndReport(string executablePath, int processId, ProgramFingerprintDto fingerprint)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);

            _logger.LogInformation("Program Control ปิด '{FileName}' (PID {Pid}) - อยู่ใน blocklist", fingerprint.FileName, processId);

            _state.AddLog(new LogEntryDto
            {
                ClientGuid = _state.ClientGuid,
                MachineName = Environment.MachineName,
                ProcessPath = executablePath,
                DetectedPublisher = fingerprint.SignerName ?? fingerprint.CompanyName,
                WasSignedMatch = fingerprint.IsSigned,
                MatchedRuleName = "Program Control",
                ActionTaken = "Blocked by Program Control (blocklist)"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ปิด '{FileName}' (PID {Pid}) ไม่สำเร็จ", fingerprint.FileName, processId);
            return false;
        }
    }

    /// <summary>
    /// ปิดทุก process ที่รันจาก path นี้อยู่ - ใช้ตอนผลจำแนกเพิ่งกลับมาจาก Server หลังจากที่
    /// โปรแกรมเปิดไปแล้ว (ไม่มี PID เดิมติดมากับผล และอาจมีหลาย instance เปิดอยู่พร้อมกัน)
    /// </summary>
    private void KillByPath(string fullPath, ProgramFingerprintDto fingerprint)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    continue; // อ่าน path ไม่ได้ (สิทธิ์/bitness) - ข้ามไป ไม่ใช่ process ที่เราหาอยู่แน่ๆ ก็ไม่รู้ แต่เดาไม่ได้ก็ไม่แตะ
                }

                if (!string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                _logger.LogInformation("Program Control ปิด '{FileName}' (PID {Pid}) หลังได้ผลจำแนกจาก Server",
                    fingerprint.FileName, process.Id);
            }
            catch
            {
                // process ตายไปเองแล้ว หรือปิดไม่ได้ - รอบ scan ถัดไปจะเจออีกถ้ายังอยู่
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
