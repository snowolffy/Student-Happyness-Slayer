namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// เก็บค่า LoopbackSecret ล่าสุดที่ได้จาก OnionProcOparetor.Agent ผ่าน named pipe (WP2b, 2026-10-30) -
/// BroadcastPipeListener อัปเดตค่านี้ทุกครั้งที่ได้ message ใดๆ จาก Agent (ดู HandleMessageLine) ส่วน
/// ClientApiClient อ่านค่านี้ไปแนบกับ POST /vnc/session-result และ /processes/list-result เพื่อให้ Agent
/// ตรวจได้ว่าเป็น AgentTray จริง (ดู LoopbackSecret ฝั่ง Agent)
///
/// เป็น static เพราะ BroadcastPipeListener กับ ClientApiClient ถูกสร้างแยกกันเป็น field คนละตัวใน
/// App.xaml.cs (ไม่มี DI container ในฝั่ง AgentTray) การแชร์ค่าข้ามสอง instance ที่ไม่รู้จักกันโดยตรง
/// ต้องผ่านตัวกลาง static แบบนี้ - ไม่ต้องแก้ App.xaml.cs เพิ่มเพื่อ wiring ใหม่
///
/// ค่าว่าง (null) ตอนเริ่มโปรแกรมก่อนได้ message แรกจาก Agent ไม่เป็นปัญหาจริง: /vnc/session-result และ
/// /processes/list-result ถูกเรียกเป็นผลสืบเนื่องจากที่ Agent สั่ง StartVncSession/GetRunningProcesses
/// มาก่อนเสมอ (ดู App.xaml.cs OnStartVncSessionRequested/OnGetRunningProcessesRequested) ซึ่ง message
/// นั้นเองก็แนบ secret ล่าสุดมาด้วยอยู่แล้ว - ถึงเวลาต้อง callback กลับไปจึงมี secret ที่ถูกต้องพร้อมใช้เสมอ
/// ยกเว้นกรณีผิดปกติที่เรียก callback โดยไม่มี request นำมาก่อน ซึ่งควรถูก Agent ปฏิเสธด้วย 401 อยู่แล้ว
/// </summary>
public static class LoopbackSecretStore
{
    private static readonly object Lock = new();
    private static string? _current;

    /// <summary>เรียกจาก BroadcastPipeListener ทุกครั้งที่รับ message จาก Agent (ไม่ว่า Type ไหน) - ไม่เขียนทับด้วยค่าว่าง/null เพื่อไม่ให้ secret ที่ใช้งานได้อยู่หายไปเปล่าๆ ถ้า message บางตัวบังเอิญไม่มีค่านี้ติดมา</summary>
    public static void Update(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        lock (Lock)
        {
            _current = secret;
        }
    }

    /// <summary>ค่า secret ล่าสุดที่รู้จัก - null ถ้ายังไม่เคยได้ message จาก Agent เลยตั้งแต่ AgentTray เริ่มทำงาน</summary>
    public static string? Current
    {
        get
        {
            lock (Lock)
            {
                return _current;
            }
        }
    }
}
