namespace OnionProcOparetor.Console.Models;

/// <summary>แจก/เก็บไฟล์ (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน) - DTO ทั้งหมดของหน้า tc-files
/// รวมไว้ไฟล์เดียว (ตรงข้ามกับ LaunchableProgramDto ที่แยกไฟล์ เพราะ 4 DTO นี้เล็กและใช้ด้วยกันเสมอ
/// ในหน้าเดียว) ดู Server/Models/DistributedFile.cs, FileCollectionJob.cs, CollectedFile.cs</summary>
public class FileTransferSettingsDto
{
    public int MaxFileSizeMb { get; set; } = 100;
}

public class DistributedFileResultDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class FileCollectionJobDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FileNamePattern { get; set; }
    public int? RoomId { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsClosedManually { get; set; }
    public bool IsOpen { get; set; }
}

/// <summary>หนึ่งแถวของ GET /api/files/collection-jobs/{id}/status - หนึ่งเครื่องเป้าหมาย</summary>
public class CollectionStatusRowDto
{
    public string ClientGuid { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? StudentName { get; set; }
    public bool HasFile { get; set; }
    public string? FileName { get; set; }
    public DateTime? CollectedAt { get; set; }

    public string StudentDisplayText => string.IsNullOrEmpty(StudentName) ? "—" : StudentName;
    public string StatusText => HasFile ? "ได้แล้ว" : "ยังไม่มีไฟล์";
}

public class CollectionJobStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FileNamePattern { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsOpen { get; set; }
    public int TotalCount { get; set; }
    public int CollectedCount { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public List<CollectionStatusRowDto> Rows { get; set; } = new();
}
