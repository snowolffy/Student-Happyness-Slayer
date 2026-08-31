namespace OnionProcOparetor.Server.Models;

/// <summary>
/// สิ่งที่ Agent เก็บได้จากโปรแกรมหนึ่งตัวบนเครื่องนักเรียน แล้วส่งมาให้ Server จำแนก
///
/// หลักการที่ยึด: Agent "เก็บข้อเท็จจริง" อย่างเดียว ไม่ตัดสิน - การตัดสินอยู่ที่ Server ที่เดียว
/// เพราะ (ก) เกณฑ์เปลี่ยนได้โดยไม่ต้อง deploy Agent ใหม่ทั้ง 40 เครื่อง (ข) ผลที่คำนวณแล้วแชร์
/// ให้ทุกเครื่องใช้ต่อได้ (ค) การเรียกแหล่งข้อมูลออนไลน์ควรออกจากจุดเดียว ไม่ใช่ 40 จุดพร้อมกัน
/// ข้อยกเว้นเดียวคือสัญญาณที่ชัดเจนมากจน Agent ตัดสินเองได้ทันทีโดยไม่ต้องรอ (ดู
/// ProgramFingerprint.LocalConfidence และ ProgramControlService ฝั่ง Agent)
///
/// ต้องตรงกับ ProgramFingerprintDto ฝั่ง Agent (คนละ assembly กัน ไม่ได้แชร์ project)
/// </summary>
public class ProgramFingerprint
{
    /// <summary>SHA-256 ของไฟล์ (hex ตัวเล็ก)</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>path เต็ม - ใช้หา marker ของโฟลเดอร์ (steamapps, Epic Games ฯลฯ)</summary>
    public string FullPath { get; set; } = string.Empty;

    public string? ProductName { get; set; }
    public string? FileDescription { get; set; }
    public string? CompanyName { get; set; }

    /// <summary>subject ของ Authenticode certificate ถ้า verify ผ่าน</summary>
    public string? SignerName { get; set; }

    public bool IsSigned { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>
    /// ชื่อ DLL ที่ process นี้โหลดอยู่จริงตอนที่ตรวจ (ไม่ใช่ import table แบบ static)
    ///
    /// เลือกอ่าน loaded modules แทนการ parse PE import table โดยตั้งใจ: เกมสมัยใหม่เกือบทั้งหมด
    /// โหลด d3d11/d3d12/xinput ผ่าน LoadLibrary ตอน runtime หรือผ่าน engine DLL อีกชั้น
    /// (UnityPlayer.dll เป็นตัวที่ import จริง ไม่ใช่ .exe) - import table ของ .exe จึงมักไม่มี
    /// สัญญาณเหล่านั้นเลย ในขณะที่รายการ module ที่โหลดจริงมีครบ และได้มาฟรีจาก Process.Modules
    /// ที่ Agent มีอยู่ในมืออยู่แล้วตอนที่เห็น process นั้นรันอยู่
    /// </summary>
    public List<string> LoadedModules { get; set; } = new();

    /// <summary>ชื่อไฟล์อื่นๆ ในโฟลเดอร์เดียวกัน (จำกัดจำนวน) - จับ steam_appid.txt, EOSSDK, .pak ฯลฯ</summary>
    public List<string> SiblingFiles { get; set; } = new();

    /// <summary>ชื่อเครื่องที่เจอ - ไว้บอกครูว่าต้องไปดูเครื่องไหน</summary>
    public string? MachineName { get; set; }
}
