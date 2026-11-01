namespace OnionProcOparetor.Agent.Models;

/// <summary>
/// ผลลัพธ์จาก GET /api/poll/{clientGuid} ฝั่ง Server
/// </summary>
public class PollResponse
{
    /// <summary>true = enforcement เปิดอยู่ (global + per-machine ต้องเปิดพร้อมกัน)</summary>
    public bool Enabled { get; set; }

    public List<RuleDto> Rules { get; set; } = new();

    /// <summary>กฎบล็อกเว็บที่เปิดใช้งานอยู่ (Website Control, 2026-09-11) - ดู WebsiteControlService</summary>
    public List<WebsiteRuleDto> WebsiteRules { get; set; } = new();

    /// <summary>ตั้งค่า "ป้องกันการเลี่ยง" (WP2-1, 2026-10-31) - null ถ้าแถวใน DB หายไปจริงๆ (ไม่ควรเกิด -
    /// มี seed ใน migration) ดู BrowserHardeningService</summary>
    public BrowserHardeningSettingsDto? BrowserHardening { get; set; }

    /// <summary>[OPTIONAL] ถ้า Server ตั้ง override ไว้ ให้ใช้ค่านี้แทน PollIntervalSeconds ใน appsettings.json</summary>
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>Command ที่ยังไม่ deliver ของเครื่องนี้ (fallback path คู่ขนานกับ SignalR ReceiveCommand)</summary>
    public List<PendingCommandDto> PendingCommands { get; set; } = new();

    /// <summary>
    /// เวอร์ชันปัจจุบันของ blocklist ฝั่ง Server (Program Control, 2026-08-31)
    /// Agent เก็บไว้แล้วแนบกลับไปกับ poll รอบถัดไป
    /// </summary>
    public string? BlocklistVersion { get; set; }

    /// <summary>
    /// รายการ SHA-256 ที่ต้องบล็อก - null แปลว่า "เวอร์ชันเดิม ใช้รายการที่ถืออยู่ต่อ"
    /// ไม่ใช่ "ไม่มีรายการเลย" (ดู ProgramControlService.ApplyBlocklist - ตีความสองอย่างนี้สลับกัน
    /// จะกลายเป็นล้าง blocklist ทิ้งทุกรอบ poll)
    /// </summary>
    public List<string>? BlockedHashes { get; set; }

    /// <summary>
    /// "โหมดสอบ" (WP2-2, 2026-10-31) - null = ไม่มีโหมดสอบทำงานอยู่กับเครื่องนี้ ไม่ null = กำลังทำงาน
    /// อยู่ ดู ExamModeState.UpdateFromServer - Agent เก็บ ExpiresAtUtc ไว้เองแล้วหมดเวลาด้วยนาฬิกา
    /// ตัวเอง ไม่ต้องรอ Server บอกว่า "หมดแล้ว" อีกครั้ง (ทำงานต่อได้แม้ Server ล่มระหว่างที่ poll ไม่ติด)
    /// </summary>
    public ExamModeDto? ExamMode { get; set; }

    /// <summary>
    /// "ต้องล็อกอินก่อนใช้เครื่องไหม" (WP3-3, 2026-10-31) - คำนวณมาจาก SignInSettings.RequireLoginMode
    /// ของเครื่องนี้แล้ว (ดู PollEndpoints.cs) Agent ไม่ต้องรู้เรื่อง RequireLoginMode/ห้องเลย แค่เชื่อ
    /// ค่า bool นี้ตรงๆ ดู Worker.cs ที่ผสมกับ bypass/enabled อีกชั้นก่อนแจ้ง AgentTray (เหมือน ExamMode)
    /// </summary>
    public bool RequireLogin { get; set; }

    /// <summary>ข้อความบนจอล็อกอินนักเรียน (SignInSettings.LoginScreenMessage) - null/ว่าง = ไม่แสดง</summary>
    public string? LoginScreenMessage { get; set; }

    /// <summary>ข้อความเริ่มต้นตอนครูล็อกจอ (SignInSettings.LockScreenDefaultMessage) - ใช้เมื่อครูไม่ได้
    /// พิมพ์ข้อความทับเฉพาะครั้งตอนกดล็อก (ดู CommandProcessor.HandleShowLockScreenAsync)</summary>
    public string? LockScreenDefaultMessage { get; set; }

    /// <summary>ออกจากระบบเองเมื่อไม่ได้ใช้งานกี่นาที (SignInSettings.AutoLogoutIdleMinutes) - AgentTray
    /// เป็นคนจับเวลา idle จริง (ดู IdleActivityMonitor) Agent แค่ relay ค่านี้ผ่าน status/push เท่านั้น</summary>
    public int AutoLogoutIdleMinutes { get; set; } = 15;

    /// <summary>
    /// true = เครื่องนี้มีรายการค้างอยู่ในคิวยกมือตอนนี้ (WP4-2, 2026-11-01 - แก้ตาม feedback ก่อน merge)
    /// ดู RaiseHandQueue.GetStatusForClient ฝั่ง Server - Agent cache ค่านี้ไว้ (ดู ClientState.UpdateRaiseHand)
    /// ให้ RaiseHandBadgeWindow ฝั่ง AgentTray poll /status ของ Agent ถี่กว่ารอบ poll ปกติ (ทุก ~5 วิ) แล้ว
    /// ปิดตัวเองเมื่อค่านี้กลายเป็น false โดยไม่ต้องมีช่องทาง push ใหม่
    /// </summary>
    public bool RaisedHand { get; set; }

    /// <summary>ตำแหน่งในคิวปัจจุบัน (1 = คิวแรก) - มีค่าเฉพาะตอน RaisedHand เป็น true</summary>
    public int? RaiseHandQueuePosition { get; set; }
}

/// <summary>ส่วนหนึ่งของ PollResponse - ดู ExamMode ด้านบน</summary>
public class ExamModeDto
{
    public DateTime ExpiresAtUtc { get; set; }
    public List<string> AllowedDomains { get; set; } = new();
}