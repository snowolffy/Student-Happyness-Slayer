using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// จุดเดียวที่ตอบคำถาม "โปรแกรมนี้ให้รันได้ไหม" - รวม ProgramClassifier (สัญญาณในเครื่อง),
/// OnlineGameLookup (ฐานข้อมูลเกมออนไลน์) และตาราง ProgramVerdicts (ความจำ + คำตัดสินของครู)
/// เข้าด้วยกัน
///
/// ลำดับความสำคัญที่ยึดตายตัว:
///   1. คำตัดสินของครู (IsManual) - ชนะทุกอย่าง ไม่มีเงื่อนไข classifier ห้ามเขียนทับ
///   2. ผลที่เคยคำนวณไว้แล้ว - ใช้ซ้ำ ไม่คำนวณใหม่ (และไม่ยิงเน็ตซ้ำ)
///   3. คำนวณใหม่
///
/// ข้อ 1 เป็นหัวใจของทั้งดีไซน์: อัลกอริทึมที่แก้คำตัดสินของคนได้เอง คือระบบที่ครูเลิกเชื่อถือ
/// ภายในสัปดาห์แรกที่มันเดาผิดแล้วย้อนกลับมาบล็อกของเดิมซ้ำ
/// </summary>
public sealed class ProgramVerdictService
{
    private readonly ProgramClassifier _classifier;
    private readonly OnlineGameLookup _onlineLookup;
    private readonly ILogger<ProgramVerdictService> _logger;

    public ProgramVerdictService(
        ProgramClassifier classifier,
        OnlineGameLookup onlineLookup,
        ILogger<ProgramVerdictService> logger)
    {
        _classifier = classifier;
        _onlineLookup = onlineLookup;
        _logger = logger;
    }

    /// <summary>
    /// จำแนกโปรแกรมชุดหนึ่งที่ Agent ส่งมา คืนผลเป็น map จาก sha256 -> action
    /// ผู้เรียกต้อง SaveChangesAsync() เอง
    /// </summary>
    public async Task<Dictionary<string, ProgramAction>> ResolveAsync(
        AppDbContext db,
        IReadOnlyList<ProgramFingerprint> fingerprints,
        CancellationToken ct)
    {
        var result = new Dictionary<string, ProgramAction>(StringComparer.OrdinalIgnoreCase);
        if (fingerprints.Count == 0)
        {
            return result;
        }

        var hashes = fingerprints
            .Select(f => f.Sha256)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await db.ProgramVerdicts
            .Where(v => hashes.Contains(v.Sha256))
            .ToDictionaryAsync(v => v.Sha256, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var fingerprint in fingerprints)
        {
            if (string.IsNullOrWhiteSpace(fingerprint.Sha256))
            {
                continue;
            }

            if (existing.TryGetValue(fingerprint.Sha256, out var known))
            {
                known.LastSeenAt = DateTime.UtcNow;
                known.SightingCount++;
                known.LastSeenOnMachine = fingerprint.MachineName ?? known.LastSeenOnMachine;

                result[fingerprint.Sha256] = known.Action;
                continue;
            }

            var verdict = await ClassifyNewAsync(fingerprint, ct);
            db.ProgramVerdicts.Add(verdict);

            // กันกรณีที่ Agent ส่ง hash เดียวกันซ้ำมาในชุดเดียว (เช่น เปิดโปรแกรมเดิม 2 หน้าต่าง)
            // ถ้าไม่ใส่กลับเข้า dictionary จะสร้างแถวซ้ำแล้วชน unique index ตอน SaveChanges
            existing[fingerprint.Sha256] = verdict;
            result[fingerprint.Sha256] = verdict.Action;
        }

        return result;
    }

    private async Task<ProgramVerdict> ClassifyNewAsync(ProgramFingerprint fingerprint, CancellationToken ct)
    {
        // เรียงลำดับชื่อที่จะเอาไปค้นตามความน่าเชื่อถือ: ProductName จาก metadata เป็นชื่อที่ผู้ทำ
        // ตั้งใจใส่ไว้จริง ส่วนชื่อโฟลเดอร์แม่มักเป็นชื่อเต็มของเกมในกรณีที่ .exe ใช้ตัวย่อ
        var candidateNames = new List<string?>
        {
            fingerprint.ProductName,
            Path.GetFileName(Path.GetDirectoryName(fingerprint.FullPath ?? string.Empty)),
            fingerprint.FileName,
            fingerprint.FileDescription
        };

        OnlineLookupOutcome? onlineOutcome = null;
        try
        {
            onlineOutcome = await _onlineLookup.LookupAsync(candidateNames, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ค้นออนไลน์สำหรับ {FileName} ล้มเหลว - ใช้สัญญาณในเครื่องอย่างเดียว", fingerprint.FileName);
        }

        var classification = _classifier.Classify(fingerprint, onlineOutcome);

        return new ProgramVerdict
        {
            Sha256 = fingerprint.Sha256,
            FileName = fingerprint.FileName,
            ProductName = fingerprint.ProductName,
            Publisher = fingerprint.SignerName ?? fingerprint.CompanyName,
            IsSignedPublisher = fingerprint.IsSigned,
            Score = classification.Score,
            Category = classification.Category,
            Action = classification.Action,
            ReasonsJson = JsonSerializer.Serialize(classification.Signals),
            IsManual = false,
            OnlineLookupDone = onlineOutcome is not null,
            OnlineMatchName = onlineOutcome?.MatchedName,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            LastSeenOnMachine = fingerprint.MachineName,
            SightingCount = 1
        };
    }
}
