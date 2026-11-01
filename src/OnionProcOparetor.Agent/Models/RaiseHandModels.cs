namespace OnionProcOparetor.Agent.Models;

/// <summary>ร้องขอจาก AgentTray มาที่ loopback POST /raisehand/send (WP4-2, 2026-11-01)</summary>
public sealed record RaiseHandSendLoopbackRequest(string Reason, string? Note);

/// <summary>
/// ผลลัพธ์จาก POST /api/raisehand ฝั่ง Server (ดู RaiseHandQueueEntryResponse/RaiseHandEndpoints.cs) -
/// Agent ส่งต่อให้ AgentTray ตรงๆ ไม่ตีความเอง (Success=false มีได้ทั้งเพราะ cooldown หรือมีรายการค้างอยู่แล้ว)
/// </summary>
public sealed record RaiseHandResult(bool Success, string? Error, int? CooldownSecondsRemaining, int? QueuePosition);

/// <summary>ผลลัพธ์จาก POST /api/raisehand/cancel ฝั่ง Server</summary>
public sealed record RaiseHandCancelResponse(bool Success);
