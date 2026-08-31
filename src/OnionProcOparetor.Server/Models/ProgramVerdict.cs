namespace OnionProcOparetor.Server.Models;

/// <summary>ผลตัดสินว่าโปรแกรมหนึ่งควรถูกทำอะไร</summary>
public enum ProgramAction
{
    /// <summary>ปล่อยผ่าน ไม่ต้องทำอะไร</summary>
    Allow = 0,

    /// <summary>ไม่ปิด แต่บันทึกไว้ให้ครูเห็นในคิวตรวจสอบ</summary>
    Flag = 1,

    /// <summary>ปิดทันทีทุกครั้งที่เจอ</summary>
    Block = 2
}

/// <summary>
/// หมวดหมู่ที่ classifier เดาได้ - ใช้แสดงผลและใช้เป็นเหตุผลประกอบให้ครูตัดสินใจ
/// ไม่ได้ใช้ตัดสินเองโดยตรง (การตัดสินใช้ Score เทียบ threshold)
/// </summary>
public enum ProgramCategory
{
    Unknown = 0,
    Game = 1,
    GameLauncher = 2,
    Emulator = 3,
    Streaming = 4,
    Chat = 5,
    RemoteAccess = 6,
    SystemOrProductivity = 7,
    Inappropriate = 8
}

/// <summary>
/// ผลการจำแนกโปรแกรมหนึ่งตัว key ด้วย SHA-256 ของไฟล์ (2026-08-31)
///
/// ทำไม key ด้วย hash ไม่ใช่ path หรือชื่อไฟล์: ทั้งสองอย่างนั้นนักเรียนเปลี่ยนได้ฟรีในหนึ่งวินาที
/// (rename game.exe เป็น chrome.exe, ก๊อปไปวางที่อื่น) - ซึ่งเป็นข้อจำกัดของระบบกฎ publisher เดิม
/// ที่ตรวจจับได้เฉพาะโปรแกรมที่มี publisher ตรงกับที่ครูพิมพ์ไว้เท่านั้น hash เปลี่ยนก็ต่อเมื่อ
/// เนื้อไฟล์เปลี่ยนจริง ซึ่งสำหรับเกมแปลว่าต้องไป patch binary เอง
///
/// ตารางนี้เป็น "ความรู้สะสมของทั้งห้อง": เครื่องแรกที่เจอโปรแกรมใหม่ทำให้ทุกเครื่องที่เหลือรู้จัก
/// มันทันทีโดยไม่ต้องคำนวณซ้ำ และการตัดสินใจของครูหนึ่งครั้งมีผลกับทั้ง 40 เครื่องตลอดไป
/// </summary>
public class ProgramVerdict
{
    public int Id { get; set; }

    /// <summary>SHA-256 ของไฟล์ (hex ตัวเล็ก) - unique</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>ชื่อไฟล์ที่เจอครั้งแรก (ไว้ให้ครูอ่านออก - ไม่ใช้ตัดสินอะไร)</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>ProductName จาก metadata ถ้ามี</summary>
    public string? ProductName { get; set; }

    /// <summary>publisher ที่ตรวจได้ (signature ก่อน ถ้าไม่มีค่อย CompanyName)</summary>
    public string? Publisher { get; set; }

    /// <summary>true = publisher มาจาก Authenticode ที่ verify ผ่าน ไม่ใช่ metadata ที่แก้ได้ด้วย resource editor</summary>
    public bool IsSignedPublisher { get; set; }

    /// <summary>คะแนน 0-100 ยิ่งสูงยิ่งมั่นใจว่าเป็นเกม/โปรแกรมที่ไม่เหมาะสม</summary>
    public int Score { get; set; }

    public ProgramCategory Category { get; set; }

    public ProgramAction Action { get; set; }

    /// <summary>
    /// เหตุผลที่ได้คะแนนนี้ (บรรทัดละสัญญาณ) - สำคัญมากต่อการใช้งานจริง: ครูต้องเห็นว่า "ทำไม"
    /// ถึงถูกจัดเป็นเกม ก่อนจะกดยืนยัน/ยกเลิก ไม่ใช่เห็นแค่ตัวเลขคะแนนลอยๆ ที่ตรวจสอบย้อนกลับไม่ได้
    /// </summary>
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>
    /// true = ครูตัดสินเองด้วยมือ ห้าม classifier เขียนทับเด็ดขาด
    /// เป็นหลักการหลักของทั้งระบบนี้: อัลกอริทึมเสนอ คนตัดสิน และการตัดสินของคนอยู่เหนือเสมอ
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>ใครเป็นคนตัดสิน (null = อัลกอริทึม)</summary>
    public string? DecidedBy { get; set; }

    /// <summary>true = เคยส่งไปถามแหล่งข้อมูลออนไลน์แล้ว (กันยิงซ้ำทุกครั้งที่เจอไฟล์เดิม)</summary>
    public bool OnlineLookupDone { get; set; }

    /// <summary>ชื่อเกมที่ match จากแหล่งออนไลน์ ถ้ามี (ไว้แสดงให้ครูเห็นว่าอ้างอิงจากอะไร)</summary>
    public string? OnlineMatchName { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>เครื่องที่รายงานโปรแกรมนี้เข้ามาล่าสุด (ไว้ให้ครูรู้ว่าต้องไปดูเครื่องไหน)</summary>
    public string? LastSeenOnMachine { get; set; }

    /// <summary>นับว่ามีกี่ครั้งที่เจอโปรแกรมนี้ทั้งห้อง - เรียงคิวตรวจสอบตามความถี่ได้</summary>
    public int SightingCount { get; set; }
}
