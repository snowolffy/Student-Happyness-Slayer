namespace OnionProcOparetor.Console.Models;

/// <summary>
/// Settings ฝั่ง Console เท่านั้น (แยกจาก Agent's ServerSettings ใน appsettings.json - ดู
/// OnionProcOparetor.Agent's ServerSettings ใน Worker.cs) persist ผ่าน SettingsService ที่
/// %AppData%\OnionProcOparetor\Console\console-settings.json แยกเป็น group ย่อยตามหมวดโดยเจตนา
/// เผื่อขยายเพิ่มทีหลัง (เช่น Ui.Theme ที่ยังไม่มี UI ให้แก้ตอนนี้)
/// </summary>
public class ConsoleSettings
{
    public VncSettings Vnc { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
}

public class VncSettings
{
    /// <summary>ค่า default ต้องตรงกับ VncSessionPool.MaxConcurrentSessions เดิม (24) ก่อนจะกลายเป็น configurable</summary>
    public int MaxConcurrentSessions { get; set; } = 24;

    /// <summary>
    /// default false (ปิด grid ทั้งหมดเมื่อออกจากแท็บ LIVE VIEW) โดยเจตนา - เป็น safety net กัน session
    /// ค้างเปิดโดยไม่มีใครดู ซึ่งจะไปกินโควตา MaxConcurrentSessions เปล่าๆ ถ้าเปิดเป็น true grid จะยังเดินต่อ
    /// ที่พื้นหลังตอนสลับไปแท็บอื่น (แต่ปิด Dashboard ทั้งหน้าต่างยังคง dispose ทุก session เสมอไม่ว่าค่านี้จะเป็นอะไร
    /// เพราะ session ผูกอยู่กับ process ของ Console เอง ไม่มี process อื่นถือ session ต่อได้)
    /// </summary>
    public bool KeepLiveViewOpenOnTabLeave { get; set; } = false;
}

public class ConnectionSettings
{
    public string ServerHost { get; set; } = "localhost";
    public string ServerPort { get; set; } = "8787";

    /// <summary>ความถี่ auto-refresh ของ DashboardWindow (DispatcherTimer) - ค่า default เดิมคือ 10 วิ</summary>
    public int DashboardPollIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// WP1b (2026-10-30) - ต่อ Server ด้วย HTTP (false) หรือ HTTPS (true) **ค่าเริ่มต้นคือ HTTP** ตามที่
    /// roadmap ระบุไว้ว่าช่วงย้ายระบบต้องมีตัวเลือก "ยอมรับ HTTP เดิม" จนกว่าทั้งแล็บจะย้ายไป HTTPS จริง
    /// มาจาก checkbox บนหน้าล็อกอิน (MainWindow) และจำไว้เหมือน ServerHost/ServerPort
    /// </summary>
    public bool UseHttps { get; set; } = false;

    /// <summary>
    /// WP1b (2026-10-30) - thumbprint ของ certificate ที่ผู้ใช้ "ไว้ใจ" ไว้แล้วสำหรับ Server แต่ละตัว
    /// คีย์ = "host:port" (lower-case) ค่า = X509Certificate2.Thumbprint แก้ไขผ่าน
    /// ServerCertificatePinning.TrustCertificate เท่านั้น (ดู class นั้นสำหรับ flow เต็มของการไว้ใจ cert
    /// ครั้งแรกและการตรวจจับ cert ที่เปลี่ยนไปจากที่ pin ไว้) แยกคีย์ตาม host:port เพราะ Console เครื่อง
    /// เดียวอาจต่อ Server ทดสอบคนละตัวกันได้ (เช่นตอน field test บน port 18787/18788)
    /// </summary>
    public Dictionary<string, string> TrustedCertificateThumbprints { get; set; } = new();
}

public class UiSettings
{
    /// <summary>"Light" / "Dark" / "System" - null = ยังไม่เคยเลือก ซึ่ง ThemeService ตีความเป็น System</summary>
    public string? Theme { get; set; }

    /// <summary>
    /// sidebar ของ Dashboard ถูกย่อไว้หรือไม่ - จำต่อเครื่อง ไม่ผูกกับบัญชี เพราะมันเป็นเรื่อง
    /// "จอนี้กว้างแค่ไหน" ไม่ใช่ "คนนี้ชอบแบบไหน" (ครูคนเดียวกันใช้ทั้งเครื่องจอใหญ่และโน้ตบุ๊ก)
    /// </summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>
    /// โชว์ปุ่ม Tailscale (LOCK / UNLOCK / STATUS / LOGIN) ในแผงคำสั่งไหม - **ค่าเริ่มต้นคือไม่โชว์**
    ///
    /// สี่ปุ่มนี้เป็นงานดูแลระบบเครือข่าย ไม่ใช่งานคุมห้องเรียน ครูแทบไม่แตะเลย แต่มันนั่งปนอยู่กับ
    /// LOCK WORKSTATION / SHUTDOWN ซึ่งเป็นปุ่มที่กดจริงทุกวัน ทำให้แผงยาวขึ้นโดยเปล่าประโยชน์และ
    /// เพิ่มโอกาสกดพลาด - ใครที่ต้องใช้จริงเปิดได้จากหน้า Settings
    ///
    /// เป็นค่าต่อเครื่อง Console ไม่ผูกกับบัญชี เหมือนค่าอื่นในกลุ่มนี้
    /// </summary>
    public bool ShowTailscaleActions { get; set; }

    /// <summary>
    /// แสดงเวลาในตารางเป็น UTC แทนเวลาไทยไหม - **ค่าเริ่มต้นคือเวลาไทย** (false) ดู Services/TimeDisplay
    /// เก็บเป็น "UTC หรือไม่" แทน "เวลาไทยหรือไม่" เพื่อให้ไฟล์ settings เก่าที่ไม่มีช่องนี้ได้ค่า false
    /// = เวลาไทย ซึ่งเป็นค่าที่ต้องการ ไม่ต้องมี migration ของไฟล์
    /// </summary>
    public bool ShowTimesInUtc { get; set; }

    /// <summary>
    /// ห้องที่เลือกไว้ล่าสุดที่ตัวกรองห้องบนหัวแท็บ Clients (WP3, 2026-10-30) - จำต่อเครื่อง Console
    /// เหมือนค่าอื่นในกลุ่มนี้ (ครูคนเดียวกันอาจสลับเครื่อง Console คนละห้องกัน)
    ///
    /// null = "ทุกห้อง" (ค่าเริ่มต้น) · 0 = sentinel แทน "ยังไม่จัดห้อง" (Room.Id จริงฝั่ง Server เริ่ม
    /// นับจาก 1 เสมอผ่าน EF identity column จึงไม่ชนกับห้องจริง) · ค่าอื่น = Room.Id
    /// </summary>
    public int? SelectedClientsRoomFilter { get; set; }
}
