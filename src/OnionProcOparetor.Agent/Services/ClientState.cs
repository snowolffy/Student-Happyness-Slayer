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

    // ---- Sign-in (WP3-3, 2026-10-31) - cache ดิบจาก poll ล่าสุด ยังไม่ผสมกับ bypass/enabled (Worker.cs
    // เป็นคนผสมอีกชั้นก่อนตัดสินใจแจ้ง AgentTray จริง - เหมือน pattern ของ ExamModeState) ----
    public bool RequireLogin { get; private set; }
    public string? LoginScreenMessage { get; private set; }
    public string? LockScreenDefaultMessage { get; private set; }
    public int AutoLogoutIdleMinutes { get; private set; } = 15;

    /// <summary>นักเรียนที่ login อยู่ที่เครื่องนี้ตอนนี้ (ถ้ามี) - AgentTray เป็นคนแจ้งเข้ามาหลัง login/
    /// logout สำเร็จกับ Server ตรงๆ (ดู POST /student-session ใน Program.cs) Agent ไม่ตรวจ/ไม่ validate
    /// ซ้ำ แค่เก็บไว้แนบไปกับ poll (ให้เห็นใน query string) และ log local เท่านั้น</summary>
    public string? CurrentStudentCode { get; private set; }
    public string? CurrentStudentName { get; private set; }

    /// <summary>ยกมือ (WP4-2, 2026-11-01 - แก้ตาม feedback ก่อน merge) - cache ดิบจาก poll ล่าสุด เหมือน
    /// RequireLogin ด้านบน ให้ /status แจก RaiseHandBadgeWindow ฝั่ง AgentTray ปิดป้ายเองได้เมื่อครูกด
    /// "ไปช่วยแล้ว"/ล้างคิวจาก Console โดยไม่ต้องมีช่องทาง push ใหม่</summary>
    public bool RaisedHand { get; private set; }
    public int? RaiseHandQueuePosition { get; private set; }

    public void UpdatePollResult(bool enabled, int rulesCount, bool succeeded)
    {
        IsEnabled = enabled;
        RulesCount = rulesCount;
        LastPollAt = DateTime.UtcNow;
        LastPollSucceeded = succeeded;
    }

    public void SetPollIntervalOverride(int? seconds) => PollIntervalOverrideSeconds = seconds;

    /// <summary>เรียกจาก Worker.cs ทุกครั้งที่ poll สำเร็จ (ดู ExecuteAsync) - sync ดิบจาก Server ตรงๆ</summary>
    public void UpdateSignInSettings(bool requireLogin, string? loginScreenMessage, string? lockScreenDefaultMessage, int autoLogoutIdleMinutes)
    {
        RequireLogin = requireLogin;
        LoginScreenMessage = loginScreenMessage;
        LockScreenDefaultMessage = lockScreenDefaultMessage;
        AutoLogoutIdleMinutes = autoLogoutIdleMinutes;
    }

    /// <summary>เรียกจาก Worker.cs ทุกครั้งที่ poll สำเร็จ (ดู ExecuteAsync) - sync ดิบจาก Server ตรงๆ เหมือน UpdateSignInSettings</summary>
    public void UpdateRaiseHand(bool raisedHand, int? queuePosition)
    {
        RaisedHand = raisedHand;
        RaiseHandQueuePosition = queuePosition;
    }

    /// <summary>เรียกจาก POST /student-session (local loopback API) ตอน AgentTray login/เปลี่ยนรหัสผ่านสำเร็จกับ Server</summary>
    public void SetCurrentStudent(string? code, string? name)
    {
        CurrentStudentCode = code;
        CurrentStudentName = name;
    }

    /// <summary>เรียกจาก POST /student-session ตอน AgentTray logout (ไม่ว่าด้วยวิธีไหน)</summary>
    public void ClearCurrentStudent() => SetCurrentStudent(null, null);

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
