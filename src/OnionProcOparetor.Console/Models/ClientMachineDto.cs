namespace OnionProcOparetor.Console.Models;

/// <summary>ข้อมูลเครื่อง client ที่ได้จาก GET /api/clients</summary>
public class ClientMachineDto
{
    public int Id { get; set; }
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? LastKnownIp { get; set; }

    /// <summary>true = เครื่องนี้กำลังต่อผ่าน Tailscale fallback อยู่ตอนนี้ ไม่ใช่ LAN ปกติ - ดู
    /// ClientMachine.UsingFallback ฝั่ง Server สำหรับที่มาเต็มๆ (last-known จาก poll ล่าสุด)</summary>
    public bool UsingFallback { get; set; }

    public bool IsEnabled { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? PollIntervalOverrideSeconds { get; set; }

    /// <summary>ห้องที่เครื่องนี้อยู่ (WP3, 2026-10-30) - null = "ยังไม่จัดห้อง" มาจาก GET /api/clients ตรงๆ
    /// (Server serialize ClientMachine entity ทั้งตัว ไม่ใช่ DTO แยก จึงมี field นี้ติดมาอยู่แล้วแม้ก่อนหน้านี้
    /// Console ยังไม่เคยอ่าน - ดู ตัวเลือกห้องบนหัวแท็บ Clients ใน DashboardWindow.ApplyClientsFilter)</summary>
    public int? RoomId { get; set; }

    public int? SeatRow { get; set; }
    public int? SeatCol { get; set; }

    /// <summary>
    /// คำนวณฝั่ง Server (ClientMachine.IsOnline) ส่งมาเป็น field นี้ตรงๆ ทั้งจาก GET /api/clients และ
    /// SignalR ClientStatusChanged - ไม่คำนวณเองฝั่ง Console อีกต่อไป (เดิมมี IsOffline คำนวณจาก
    /// LastSeenAt ที่นี่แยกกับ IsMissingUnexpectedly ที่คำนวณฝั่ง Server คนละเกณฑ์กัน สับสน และเสี่ยง
    /// clock skew ระหว่าง 2 เครื่อง - ยุบเหลือ IsOnline ตัวเดียว คำนวณที่ Server แหล่งเดียว 2026-08-20)
    ///
    /// เป็นคนละเรื่องกับ IsEnabled โดยสิ้นเชิง - IsOnline บอกว่า "เครื่องติดต่อได้ไหม" ส่วน IsEnabled บอกว่า
    /// "ครูเปิดใช้งาน enforcement เครื่องนี้ไว้ไหม" ดู DashboardWindow.xaml's STATUS/ENFORCEMENT column
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// ค่า GlobalState.IsEnabled ที่ Dashboard เติมให้ทุกครั้งก่อนผูกเข้าตาราง (ไม่ได้มากับ
    /// GET /api/clients) - pattern เดียวกับ ClientMachine.IsConnectedViaSignalR ฝั่ง Server ที่ set
    /// จากภายนอกก่อน serialize ทุกครั้ง ดู DashboardWindow.ApplyClientsFilter สำหรับจุดที่ set
    ///
    /// default = true เพื่อให้พฤติกรรมเดิม (ไม่มี gate) เป็นค่าตั้งต้นถ้าลืม set ที่ไหนสักจุด
    /// </summary>
    public bool GlobalEnforcementEnabled { get; set; } = true;

    /// <summary>
    /// สิ่งที่ Agent เครื่องนี้ทำอยู่จริง - ตรงกับ `enabled` ที่ Server คำนวณให้ตอน poll เป๊ะๆ
    /// (globalEnabled &amp;&amp; client.IsEnabled ดู /api/poll)
    /// </summary>
    public bool EffectiveEnabled => IsEnabled && GlobalEnforcementEnabled;

    /// <summary>
    /// ข้อความบน badge คอลัมน์ ENFORCEMENT - "OFF (GLOBAL)" คือกรณีที่เคยทำให้หาสาเหตุไม่เจอมาก่อน
    /// (2026-09-04): เครื่องเปิด enforcement ไว้ของมันเอง แต่ถูก gate ทั้งห้องกดทับอยู่ ตารางเดิม
    /// ขึ้น ENABLED เฉยๆ ทั้งที่ Agent รายงาน Disabled - สองฝั่งขัดกันโดยไม่มีอะไรอธิบาย
    /// </summary>
    public string EnforcementBadgeText =>
        IsEnabled && !GlobalEnforcementEnabled ? "OFF (GLOBAL)" : IsEnabled ? "ENABLED" : "DISABLED";

    /// <summary>Tag ของ StatusBadge (Online/Warning) - ดูสีตาม "ผลจริง" ไม่ใช่ค่ารายเครื่อง</summary>
    public string EnforcementBadgeTag => EffectiveEnabled ? "Online" : "Warning";
}