using System.Collections.Concurrent;
using OnionProcOparetor.Agent.Models;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// เก็บสถานะปัจจุบันของ Client Service แบบ in-memory (thread-safe)
/// Worker.cs เขียนอัพเดตทุกครั้งที่ poll/match rule, local API endpoint อ่านไปโชว์ให้ Tray
/// ไม่ต้องพึ่ง DB เพราะข้อมูลนี้เป็นแค่ snapshot ปัจจุบัน ไม่ต้อง persist
/// </summary>
public class ClientState
{
    private readonly ConcurrentQueue<LogEntryDto> _recentLogs = new();
    private const int MaxRecentLogs = 50;

    public bool IsEnabled { get; private set; } = true;
    public int RulesCount { get; private set; }
    public DateTime? LastPollAt { get; private set; }
    public bool LastPollSucceeded { get; private set; }
    public string MachineName { get; private set; } = Environment.MachineName;
    public string ClientGuid { get; set; } = ClientIdentity.GetOrCreateClientGuid();

    /// <summary>
    /// Poll interval override ปัจจุบัน - sync จาก poll response ทุกรอบ (DB เป็นที่มาหลัก) หรือถูก
    /// เขียนทับทันทีจาก CommandProcessor ตอนได้รับ "UpdateSettings" command (ให้มีผลก่อนรอบ poll ถัดไป)
    /// </summary>
    public int? PollIntervalOverrideSeconds { get; private set; }

    public void UpdatePollResult(bool enabled, int rulesCount, bool succeeded)
    {
        IsEnabled = enabled;
        RulesCount = rulesCount;
        LastPollAt = DateTime.UtcNow;
        LastPollSucceeded = succeeded;
    }

    public void SetPollIntervalOverride(int? seconds) => PollIntervalOverrideSeconds = seconds;

    public void AddLog(LogEntryDto log)
    {
        _recentLogs.Enqueue(log);
        // ตัดของเก่าทิ้งถ้าเกิน MaxRecentLogs
        while (_recentLogs.Count > MaxRecentLogs && _recentLogs.TryDequeue(out _))
        {
        }
    }

    public List<LogEntryDto> GetRecentLogs() => _recentLogs.Reverse().ToList();
}
