namespace OnionProcOparetor.Server.Services;

/// <summary>
/// เก็บว่า Agent แต่ละเครื่อง (keyed ด้วย ClientGuid) เปิด SignalR connection ค้างอยู่ตอนนี้กี่เส้น -
/// in-memory ล้วนๆ ไม่ persist ลง DB (reset ทุกครั้งที่ Server restart เหมือน pattern อื่นๆ ที่เป็น
/// per-process state เช่น AgentBypassState ฝั่ง Agent)
///
/// จุดประสงค์: ให้ ClientMachine.IsOnline มีสัญญาณที่เร็ว/แม่นยำกว่าการดู LastSeenAt (poll timestamp)
/// อย่างเดียว - ถ้า Agent ยัง connect SignalR อยู่จริง ก็แปลว่า online แน่นอนไม่ต้องรอ poll รอบถัดไปหรือ
/// รอ threshold หมดเวลา (ดู LabHub.OnDisconnectedAsync ที่ broadcast ทันทีที่ connection หลุด)
///
/// เก็บเป็น HashSet ต่อ ClientGuid (ไม่ใช่ค่าเดียว) เพราะ reconnect อาจ race กัน (connection เก่ายังไม่
/// ทัน dispose แต่ connection ใหม่ต่อเข้ามาก่อนแล้ว) - "online" หมายถึงยังเหลืออย่างน้อย 1 connection
/// </summary>
public class AgentConnectionRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, HashSet<string>> _connectionIdsByClientGuid = new();
    private readonly Dictionary<string, string> _clientGuidByConnectionId = new();

    /// <summary>
    /// เครื่องที่เคย connect SignalR สำเร็จอย่างน้อย 1 ครั้ง (นับตั้งแต่ Server รอบนี้เริ่มทำงาน) - ไม่เอาออก
    /// แม้ connection จะหลุดไปแล้วก็ตาม (ต่างจาก _connectionIdsByClientGuid ที่ลบออกเมื่อหลุด) ใช้แยกระหว่าง
    /// "เครื่องนี้ไม่เคยใช้ SignalR เลย" (fallback ไป LastSeenAt ตามปกติ - ดู ClientMachine.IsOnline) กับ
    /// "เครื่องนี้เคย connect แล้วแต่ตอนนี้หลุด" (เชื่อสถานะ disconnect นี้ทันที ไม่ fallback อีก) - บั๊กจริงที่
    /// เจอ (2026-08-20): เดิมไม่แยก 2 กรณีนี้ ใช้ OR กับ LastSeenAt staleness เสมอ ทำให้ตอน SignalR หลุดจริง
    /// (เช่น net stop service แบบ graceful) แต่ LastSeenAt ยังสดอยู่ (poll ล่าสุดเพิ่งผ่านไปไม่ถึง 90 วิ)
    /// สัญญาณ disconnect ที่ควรเร็วถูก fallback กลบทันที เท่ากับพฤติกรรมจริงยังช้าเท่าเดิม (รอ LastSeenAt
    /// หมดอายุ) ทั้งที่ตั้งใจให้เร็วกว่านั้นมาก
    /// </summary>
    private readonly HashSet<string> _everConnectedClientGuids = new();

    public void Register(string connectionId, string clientGuid)
    {
        lock (_lock)
        {
            _clientGuidByConnectionId[connectionId] = clientGuid;
            _everConnectedClientGuids.Add(clientGuid);

            if (!_connectionIdsByClientGuid.TryGetValue(clientGuid, out var connectionIds))
            {
                connectionIds = new HashSet<string>();
                _connectionIdsByClientGuid[clientGuid] = connectionIds;
            }

            connectionIds.Add(connectionId);
        }
    }

    /// <summary>เครื่องนี้เคย connect SignalR สำเร็จมาก่อนไหม (นับตั้งแต่ Server รอบนี้เริ่ม) - ดู doc comment ของ field ข้างบน</summary>
    public bool HasEverConnected(string clientGuid)
    {
        lock (_lock)
        {
            return _everConnectedClientGuids.Contains(clientGuid);
        }
    }

    /// <summary>
    /// เรียกตอน connection หลุด (LabHub.OnDisconnectedAsync) - คืน ClientGuid ที่ connection นี้เคยเป็นของ
    /// (null ถ้า connection นี้ไม่เคยเรียก RegisterAgent เลย เช่นเป็นของ Console ไม่ใช่ Agent) และคืนว่า
    /// เครื่องนั้น "ไม่เหลือ connection ใดๆ แล้ว" จริงไหม (ผู้เรียกควร broadcast ก็ต่อเมื่อค่านี้เป็น true
    /// เท่านั้น - ถ้ายังเหลือ connection อื่นของเครื่องเดียวกันอยู่ ก็ยัง online เหมือนเดิม)
    /// </summary>
    public (string? ClientGuid, bool IsFullyDisconnected) Unregister(string connectionId)
    {
        lock (_lock)
        {
            if (!_clientGuidByConnectionId.TryGetValue(connectionId, out var clientGuid))
            {
                return (null, false);
            }

            _clientGuidByConnectionId.Remove(connectionId);

            var isFullyDisconnected = true;
            if (_connectionIdsByClientGuid.TryGetValue(clientGuid, out var connectionIds))
            {
                connectionIds.Remove(connectionId);
                if (connectionIds.Count > 0)
                {
                    isFullyDisconnected = false;
                }
                else
                {
                    _connectionIdsByClientGuid.Remove(clientGuid);
                }
            }

            return (clientGuid, isFullyDisconnected);
        }
    }

    public bool IsConnected(string clientGuid)
    {
        lock (_lock)
        {
            return _connectionIdsByClientGuid.ContainsKey(clientGuid);
        }
    }

    /// <summary>
    /// connection นี้เป็นของเครื่องไหน - null ถ้า connection นี้ไม่เคยเรียก RegisterAgent สำเร็จ
    /// (เช่นเป็น connection ของ Console ที่ใช้ hub เดียวกัน หรือเป็นคนที่ต่อเข้ามาเฉยๆ)
    ///
    /// เพิ่ม 2026-08-31 สำหรับตรวจ ownership ใน LabHub.ReportCommandResult - แผนที่กลับทิศ
    /// (connectionId -> clientGuid) มีอยู่แล้วสำหรับ Unregister เลยแค่เปิดให้อ่านได้ ไม่ได้เพิ่ม state ใหม่
    /// </summary>
    public string? GetClientGuid(string connectionId)
    {
        lock (_lock)
        {
            return _clientGuidByConnectionId.TryGetValue(connectionId, out var guid) ? guid : null;
        }
    }
}
