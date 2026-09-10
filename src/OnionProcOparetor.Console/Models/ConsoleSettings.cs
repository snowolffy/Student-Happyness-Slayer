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
}
