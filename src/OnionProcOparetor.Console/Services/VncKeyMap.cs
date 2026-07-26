namespace OnionProcOparetor.Console.Services;

/// <summary>
/// แปลง Windows virtual-key code (VK_*) -> X11 keysym สำหรับส่งใน RFB KeyEvent (RFC 6143 §7.5.4)
///
/// หลักการสำคัญที่ทำให้ตารางนี้ไม่มี entry แยกสำหรับ "shifted" symbol (เช่น '!' จาก Shift+1, '_' จาก
/// Shift+-): X11 keysym/RFB keyboard model ทำงานเหมือนคีย์บอร์ดจริงทุกประการ - client ส่งแค่ keysym ของ
/// "ปุ่มกายภาพ" ที่ถูกกด (เช่น VK '1' -> keysym '1' เสมอ ไม่ว่าจะกด Shift ค้างอยู่หรือไม่) แยกกับ
/// modifier key (Shift/Ctrl/Alt) ที่ต้องส่งเป็น KeyEvent ของตัวเองต่างหาก (down ก่อน แล้วค่อยส่ง key
/// ที่ต้องการพร้อม Shift ยังกดค้างอยู่ แล้วค่อยปล่อย Shift) เครื่องปลายทาง (OS/keyboard driver จริง) เป็น
/// คนคำนวณเองว่า Shift+'1' ควรออกมาเป็น '!' ไม่ใช่ client - ตรงกับพฤติกรรมคีย์บอร์ดจริงทุกประการ (ปุ่ม "1"
/// บนคีย์บอร์ดไม่มี "โหมด shifted" ของตัวเอง มันแค่เป็นสวิตช์ตัวเดียวที่ถูกกด) ดังนั้นการรองรับ 1/!, -/_
/// ฯลฯ ทั้งคู่ทำได้ครบผ่าน "1 entry ต่อปุ่มกายภาพ + ผู้เรียกส่ง Shift_L/Shift_R เป็น KeyEvent แยกเอง"
/// ไม่ใช่การเพิ่ม entry คู่ในตารางนี้ - ถ้าเพิ่ม entry แยกสำหรับสัญลักษณ์ shifted จะกลายเป็นการเดา/ยัดเยียด
/// mapping ที่ไม่ตรงกับ spec จริง (RFB ไม่มี "shifted keysym" concept แยกต่างหาก)
///
/// Keysym values อ้างจาก X11 core keysymdef.h (ค่าคงที่มาตรฐาน stable มายาวนาน ไม่เปลี่ยนแปลง) ยกเว้น
/// กลุ่ม volume/mute ที่มาจาก XF86keysym.h (X.org vendor-extension แยกต่างหากจาก core - ดู comment ที่
/// XF86AudioMute/... ด้านล่างสำหรับ caveat เรื่อง server support ที่ยังไม่ verify)
/// </summary>
public static class VncKeyMap
{
    // ==================== X11 keysym constants ที่ใช้ ====================
    private const uint XK_BackSpace = 0xFF08;
    private const uint XK_Tab = 0xFF09;
    private const uint XK_Return = 0xFF0D;
    private const uint XK_Escape = 0xFF1B;
    private const uint XK_Delete = 0xFFFF;
    private const uint XK_Insert = 0xFF63;
    private const uint XK_Home = 0xFF50;
    private const uint XK_End = 0xFF57;
    private const uint XK_Page_Up = 0xFF55;
    private const uint XK_Page_Down = 0xFF56;
    private const uint XK_Left = 0xFF51;
    private const uint XK_Up = 0xFF52;
    private const uint XK_Right = 0xFF53;
    private const uint XK_Down = 0xFF54;

    private const uint XK_Shift_L = 0xFFE1;
    private const uint XK_Shift_R = 0xFFE2;
    private const uint XK_Control_L = 0xFFE3;
    private const uint XK_Control_R = 0xFFE4;
    private const uint XK_Caps_Lock = 0xFFE5;
    private const uint XK_Alt_L = 0xFFE9;
    private const uint XK_Alt_R = 0xFFEA;
    private const uint XK_Super_L = 0xFFEB; // Windows key (ซ้าย)
    private const uint XK_Super_R = 0xFFEC; // Windows key (ขวา)
    private const uint XK_Num_Lock = 0xFF7F;
    private const uint XK_Scroll_Lock = 0xFF14;

    private const uint XK_KP_0 = 0xFFB0;
    private const uint XK_KP_Multiply = 0xFFAA;
    private const uint XK_KP_Add = 0xFFAB;
    private const uint XK_KP_Separator = 0xFFAC;
    private const uint XK_KP_Subtract = 0xFFAD;
    private const uint XK_KP_Decimal = 0xFFAE;
    private const uint XK_KP_Divide = 0xFFAF;
    private const uint XK_KP_Enter = 0xFF8D;

    private const uint XK_F1 = 0xFFBE;

    // XF86 vendor-extension keysyms (X.org, ไม่ใช่ core X11 keysymdef.h) - ใส่ไว้เพราะมีค่าคงที่มาตรฐาน
    // ชัดเจนและ VNC client จริงในตลาดหลายตัวส่งแบบนี้กัน แต่ "server ต้องรู้จัก keysym พวกนี้" ไม่ได้เป็น
    // ส่วนหนึ่งของ RFC 6143 - ยังไม่ live-verify ว่า winvnc.exe/UltraVNC แปลงเป็น media-key จริงบนเครื่อง
    // ปลายทางหรือไม่ (อาจแค่เพิกเฉยเงียบๆ ถ้าไม่รู้จัก) ต้องทดสอบจริงก่อนเชื่อว่าใช้ได้
    private const uint XF86XK_AudioMute = 0x1008FF12;
    private const uint XF86XK_AudioLowerVolume = 0x1008FF11;
    private const uint XF86XK_AudioRaiseVolume = 0x1008FF13;

    /// <summary>
    /// Windows VK code -> X11 keysym สำหรับปุ่มที่มี mapping ตรงไปตรงมาชัดเจน - ดู
    /// <see cref="UnmappableVirtualKeys"/> สำหรับรายการที่ตั้งใจ "ข้าม" เพราะไม่มี mapping มาตรฐานจริง
    /// (ไม่ใช่ลืมใส่)
    /// </summary>
    public static readonly IReadOnlyDictionary<int, uint> VirtualKeyToKeysym = BuildMap();

    private static Dictionary<int, uint> BuildMap()
    {
        var map = new Dictionary<int, uint>();

        // ---- A-Z (VK code ตรงกับ ASCII ตัวพิมพ์ใหญ่พอดี 0x41-0x5A - keysym คือตัวพิมพ์เล็กเสมอ ดู
        // comment บนคลาสว่าทำไมไม่มี entry แยกสำหรับตัวพิมพ์ใหญ่/เล็ก) ----
        for (var vk = 0x41; vk <= 0x5A; vk++)
        {
            var lowerAscii = (uint)(vk - 0x41 + 'a');
            map[vk] = lowerAscii;
        }

        // ---- 0-9 แถวบน (VK 0x30-0x39 ตรงกับ ASCII '0'-'9' พอดี) ----
        for (var vk = 0x30; vk <= 0x39; vk++)
        {
            map[vk] = (uint)vk; // ASCII '0'..'9' == 0x30..0x39 อยู่แล้ว
        }

        // ---- Numpad 0-9 (VK_NUMPAD0-9 = 0x60-0x69) - keysym คนละชุดกับแถวบนโดยเจตนา (KP_0-KP_9) ----
        for (var vk = 0x60; vk <= 0x69; vk++)
        {
            map[vk] = XK_KP_0 + (uint)(vk - 0x60);
        }

        // ---- Numpad operators ----
        map[0x6A] = XK_KP_Multiply;  // VK_MULTIPLY
        map[0x6B] = XK_KP_Add;       // VK_ADD
        map[0x6C] = XK_KP_Separator; // VK_SEPARATOR
        map[0x6D] = XK_KP_Subtract;  // VK_SUBTRACT
        map[0x6E] = XK_KP_Decimal;   // VK_DECIMAL
        map[0x6F] = XK_KP_Divide;    // VK_DIVIDE

        // ---- F1-F24 (VK_F1=0x70 ต่อเนื่องกันถึง VK_F24=0x87, keysym ก็ต่อเนื่องกันจาก XK_F1 เหมือนกัน) ----
        for (var i = 0; i < 24; i++)
        {
            map[0x70 + i] = XK_F1 + (uint)i;
        }

        // ---- Editing / whitespace ----
        map[0x08] = XK_BackSpace; // VK_BACK
        map[0x09] = XK_Tab;       // VK_TAB
        map[0x0D] = XK_Return;    // VK_RETURN
        map[0x1B] = XK_Escape;    // VK_ESCAPE
        map[0x20] = 0x0020;       // VK_SPACE (keysym = ASCII space ตรงๆ)

        // ---- Navigation ----
        map[0x2D] = XK_Insert;    // VK_INSERT
        map[0x2E] = XK_Delete;    // VK_DELETE
        map[0x24] = XK_Home;      // VK_HOME
        map[0x23] = XK_End;       // VK_END
        map[0x21] = XK_Page_Up;   // VK_PRIOR
        map[0x22] = XK_Page_Down; // VK_NEXT
        map[0x25] = XK_Left;      // VK_LEFT
        map[0x26] = XK_Up;        // VK_UP
        map[0x27] = XK_Right;     // VK_RIGHT
        map[0x28] = XK_Down;      // VK_DOWN

        // ---- Modifiers (generic + ซ้าย/ขวาแยก - RFB/X11 รองรับทั้งคู่) ----
        map[0x10] = XK_Shift_L;   // VK_SHIFT (generic - ไม่รู้ว่าซ้ายขวา ใช้ซ้ายเป็น default)
        map[0xA0] = XK_Shift_L;   // VK_LSHIFT
        map[0xA1] = XK_Shift_R;   // VK_RSHIFT
        map[0x11] = XK_Control_L; // VK_CONTROL (generic)
        map[0xA2] = XK_Control_L; // VK_LCONTROL
        map[0xA3] = XK_Control_R; // VK_RCONTROL
        map[0x12] = XK_Alt_L;     // VK_MENU (generic Alt)
        map[0xA4] = XK_Alt_L;     // VK_LMENU
        map[0xA5] = XK_Alt_R;     // VK_RMENU (มักเป็น AltGr บาง layout)
        map[0x5B] = XK_Super_L;   // VK_LWIN
        map[0x5C] = XK_Super_R;   // VK_RWIN
        map[0x14] = XK_Caps_Lock; // VK_CAPITAL
        map[0x90] = XK_Num_Lock;  // VK_NUMLOCK
        map[0x91] = XK_Scroll_Lock; // VK_SCROLL

        // ---- Numpad Enter ไม่มี VK code แยกจริงบน Windows (ใช้ VK_RETURN=0x0D ตัวเดียวกันเสมอ - Windows
        // แยกแค่ผ่าน extended-key scan code flag ไม่ใช่ VK code ต่างหาก) - เก็บ XK_KP_Enter ไว้ในคอมเมนต์
        // เฉยๆ ให้รู้ว่ามีอยู่จริงใน X11 แต่ VK_RETURN ธรรมดาก็ map ไป XK_Return ปกติเพียงพอแล้วเพราะ Windows
        // เองก็ไม่มีทางแยกให้ client เห็นต่างจาก Enter ปกติได้อยู่ดีถ้าอ่านแค่ VK code (ไม่ได้ decode scan code)

        // ---- Punctuation/symbol (US layout, keysym = ASCII code point ตรงๆ) ----
        map[0xBA] = ';'; // VK_OEM_1        ;:
        map[0xBB] = '='; // VK_OEM_PLUS     =+
        map[0xBC] = ','; // VK_OEM_COMMA    ,<
        map[0xBD] = '-'; // VK_OEM_MINUS    -_
        map[0xBE] = '.'; // VK_OEM_PERIOD   .>
        map[0xBF] = '/'; // VK_OEM_2        /?
        map[0xC0] = '`'; // VK_OEM_3        `~
        map[0xDB] = '['; // VK_OEM_4        [{
        map[0xDC] = '\\'; // VK_OEM_5       \|
        map[0xDD] = ']'; // VK_OEM_6        ]}
        map[0xDE] = '\''; // VK_OEM_7       '"

        // ---- Media (volume/mute เท่านั้น ตามที่ขอ - ดู caveat ด้านบนเรื่อง XF86 ยังไม่ live-verify) ----
        map[0xAD] = XF86XK_AudioMute;        // VK_VOLUME_MUTE
        map[0xAE] = XF86XK_AudioLowerVolume; // VK_VOLUME_DOWN
        map[0xAF] = XF86XK_AudioRaiseVolume; // VK_VOLUME_UP

        return map;
    }

    /// <summary>
    /// VK code ที่ "ตั้งใจ" ไม่ mapping (ไม่ใช่ลืม) พร้อมเหตุผล - ตามที่ขอให้รายงานปุ่มที่ RFC 6143/X11
    /// keysym มาตรฐานไม่มี mapping ตรงไปตรงมาจริง แทนที่จะเดา/ยัดเยียดค่าที่ไม่ถูกต้อง
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> UnmappableVirtualKeys = new Dictionary<int, string>
    {
        [0xDF] = "VK_OEM_8 - เป็นปุ่ม layout-specific ที่ Windows เองก็ไม่ได้กำหนดตำแหน่ง/สัญลักษณ์ตายตัว (ต่างกันไปตาม keyboard layout แต่ละภาษา) ไม่มี X11 keysym มาตรฐานตัวเดียวที่ตรงกับทุก layout ได้",
        [0xE2] = "VK_OEM_102 (ปุ่ม </> หรือ \\| เพิ่มเติมบน layout ที่ไม่ใช่ US เช่น ISO keyboard) - ตำแหน่ง/สัญลักษณ์ขึ้นกับ layout เช่นเดียวกับ VK_OEM_8 ไม่มี keysym มาตรฐานตัวเดียวที่ถูกต้องเสมอ",
        [0x0C] = "VK_CLEAR (Numpad 5 ตอน NumLock ปิด) - X11 มี XK_Clear (0xFF0B) จริง แต่ RFB client ทั่วไปไม่ค่อยแยกแยะสถานะ NumLock ของ physical key นี้จาก KP_Begin/KP_5 เอง (ต้อง track NumLock state เพิ่มซึ่งอยู่นอก scope 'ส่ง keysym ตรงไปตรงมา' ของรอบนี้) - ข้ามไว้ก่อน ไม่ใช่ keysym ไม่มีจริง แต่ mapping ที่ถูกต้องขึ้นกับ state เพิ่มเติมที่ยังไม่จัดการ",
    };
}
