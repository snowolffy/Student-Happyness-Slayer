using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ----
// WP4 (2026-10-30): DisplayName/Role เป็น optional - Console/OnlineConsole เวอร์ชันเก่าที่ยังไม่รู้จัก
// field นี้ส่งไม่มาก็ยังสร้าง user ได้ตามเดิม (DisplayName fallback เป็น Username, Role fallback เป็น
// Admin=0 - ดู UserEndpoints.cs จุดใช้งาน)
record CreateUserRequest(string Username, string Password, string? DisplayName = null, int Role = 0);
record ChangePasswordRequest(string NewPassword);

/// <summary>PUT /api/admin/users/{id}/display-name - เจ้าของบัญชีแก้เองได้ Admin แก้ให้คนอื่นได้ (WP4, 2026-10-30)</summary>
record UpdateDisplayNameRequest(string DisplayName);
record RuleOverrideRequest(bool IsEnabled);

/// <summary>ตั้งค่า enforcement ทั้งห้องแบบระบุค่าตรงๆ (POST /api/global/set)</summary>
record GlobalStateRequest(bool Enabled);

/// <summary>BatchId เป็น optional - Console เวอร์ชันเก่าที่ไม่ส่งมาก็ยังใช้งานได้ตามปกติ (เป็น null)</summary>
record CommandRequest(string CommandType, string? Payload, string? BatchId = null);

/// <summary>ผลจริงที่ Agent รายงานกลับมาหลัง process command เสร็จ</summary>
record CommandResultRequest(bool Success, string? Message);
record AgentUninstallAuthorizedRequest(string ClientGuid, string MachineName);

/// <summary>Agent ส่ง fingerprint ของโปรแกรมที่ยังไม่รู้จักมาให้จำแนกเป็นชุด</summary>
record ClassifyRequest(List<ProgramFingerprint>? Programs);

/// <summary>POST /api/website-rules/bulk - รายชื่อโดเมนที่ครูวางมาทีเดียวหลายบรรทัด</summary>
record AddWebsiteRulesBulkRequest(List<string>? UrlPatterns);

/// <summary>ครูตัดสินโปรแกรมหนึ่งด้วยมือ - Action ตรงกับ enum ProgramAction (0=Allow, 1=Flag, 2=Block)</summary>
record ProgramDecisionRequest(int Action);

/// <summary>
/// ครูเพิ่ม/แก้คำตัดสินของโปรแกรมหนึ่งด้วยตัวเอง โดยอ้างอิงจาก SHA-256 ที่ Console เห็นจากรายการ
/// โปรแกรมที่กำลังรันอยู่ - ไม่ต้องมี publisher และไม่ต้องเคยผ่าน classifier มาก่อน
/// </summary>
record ManualVerdictRequest(
    string? Sha256,
    string? FileName,
    string? ProductName,
    string? Publisher,
    bool IsSignedPublisher,
    string? MachineName,
    int Action);

/// <summary>
/// รายงานจาก agent-watchdog.ps1 ก่อนที่มันจะสั่ง reboot เครื่องตัวเอง
/// DetectedAtUtc มาจากฝั่ง client (นาฬิกาเครื่องนักเรียน) จึงไม่ถูกใช้เป็น Timestamp จริงของ
/// LogEntry - Server ประทับเวลาของตัวเองเสมอเหมือนทุก endpoint อื่น รับมาเพื่อดู clock skew เท่านั้น
/// </summary>
record TamperReportRequest(
    string? ClientGuid,
    string? MachineName,
    string? ReasonCode,
    string? Detail,
    DateTime? DetectedAtUtc);
