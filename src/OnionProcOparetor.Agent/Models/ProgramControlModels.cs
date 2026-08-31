namespace OnionProcOparetor.Agent.Models;

/// <summary>ต้องตรงกับ ProgramAction ฝั่ง Server (Models/ProgramVerdict.cs) - ส่งกันเป็นตัวเลข</summary>
public enum ProgramAction
{
    Allow = 0,
    Flag = 1,
    Block = 2
}

/// <summary>
/// ข้อเท็จจริงของโปรแกรมหนึ่งตัวที่ Agent เก็บได้ แล้วส่งไปให้ Server จำแนก
/// ต้องตรงกับ ProgramFingerprint ฝั่ง Server (คนละ assembly ไม่ได้แชร์ project กัน - ดู comment
/// ที่นั่นสำหรับเหตุผลว่าทำไมการตัดสินอยู่ที่ Server ไม่ใช่ที่นี่)
/// </summary>
public class ProgramFingerprintDto
{
    public string Sha256 { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? FileDescription { get; set; }
    public string? CompanyName { get; set; }
    public string? SignerName { get; set; }
    public bool IsSigned { get; set; }
    public long FileSizeBytes { get; set; }
    public List<string> LoadedModules { get; set; } = new();
    public List<string> SiblingFiles { get; set; } = new();
    public string? MachineName { get; set; }
}

/// <summary>ผลจาก POST /api/classify - map จาก sha256 -> ตัวเลขของ ProgramAction</summary>
public class ClassifyResponse
{
    public Dictionary<string, int> Verdicts { get; set; } = new();
}
