using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>สัญญาณหนึ่งตัวที่เจอ พร้อมน้ำหนักและคำอธิบายที่ครูอ่านออก</summary>
public sealed record ClassificationSignal(string Code, int Weight, string Description);

public sealed class ClassificationResult
{
    public int Score { get; init; }
    public ProgramCategory Category { get; init; }
    public ProgramAction Action { get; init; }
    public List<ClassificationSignal> Signals { get; init; } = new();
}

/// <summary>
/// เครื่องมือจำแนกว่าโปรแกรมหนึ่งเป็นเกม/โปรแกรมที่ไม่เหมาะสมหรือไม่ (2026-08-31)
///
/// ===================== ทำไมต้องมีตัวนี้ =====================
/// ระบบกฎเดิมจับด้วย publisher name อย่างเดียว และ Worker.CheckAndActOnProcess ก็ return ทันที
/// ถ้าหา publisher ไม่เจอ - แปลว่าโปรแกรมที่ "ไม่ได้เซ็นและไม่มี CompanyName ใน metadata"
/// มองไม่เห็นเลยแม้แต่ใน log ซึ่งครอบคลุมเกม indie/portable/โหลดเถื่อนแทบทั้งหมด และการเลี่ยงกฎ
/// ก็แค่แก้ CompanyName ด้วย resource editor (ไม่ต้อง re-sign อะไรเลยถ้าไฟล์ไม่ได้เซ็นมาแต่แรก)
/// พูดอีกอย่าง: ระบบเดิมจับได้เฉพาะเกมที่ "ยอมบอกชื่อตัวเอง"
///
/// ===================== แนวคิดของวิธีนี้ =====================
/// ไม่พึ่งสัญญาณเดียว แต่รวมหลักฐานหลายทางที่ปลอมพร้อมกันทั้งหมดได้ยาก แล้วให้คะแนน:
///
///   1. โครงสร้างโฟลเดอร์ที่ตัว launcher เป็นคนสร้าง (\steamapps\common\, \Epic Games\)
///      - นักเรียนย้ายเกมออกมาได้ แต่ Steam/Epic จะเลิกอัปเดต/รันให้ ซึ่งทำให้เสียประโยชน์
///   2. ไฟล์คู่ที่ engine/SDK ต้องมี (steam_api64.dll, EOSSDK, UnityPlayer.dll, GameAssembly.dll)
///      - ลบทิ้งไม่ได้ เกมจะรันไม่ขึ้น เป็นสัญญาณที่ "แพงที่จะปลอม" ที่สุดในชุดนี้
///   3. DLL ที่ process โหลดจริงตอนรัน (d3d11/d3d12 + xinput + fmod/wwise)
///      - โปรแกรมเรียนทั่วไปไม่โหลด XInput (API สำหรับจอย) เกือบ 100%
///   4. publisher ที่รู้จัก (Valve, Riot, miHoYo, Roblox, Garena ฯลฯ)
///   5. คำในชื่อไฟล์/ProductName ทั้งไทยและอังกฤษ
///   6. ข้อมูลออนไลน์ (ดู OnlineGameLookup) - ชื่อโปรแกรมตรงกับชื่อเกมในฐานข้อมูล Steam ไหม
///   7. ร่องรอยของ crack (CrackMarkers) - ตัวจำลอง Steamworks ที่เกมโหลดต้องมีเสมอ
///
/// <b>"แล้วถ้าเกมไม่ได้อยู่บน Steam ล่ะ"</b> - ข้อ 6 เป็นสัญญาณเดียวที่ผูกกับ Steam และให้
/// แค่ 45 คะแนนจากทั้งหมด ข้ออื่นไม่เกี่ยวกับ Steam เลย: ข้อ 1 ครอบคลุม Epic/Riot/Battle.net/
/// GOG/EA/Ubisoft/Roblox/Garena/itch.io/Xbox/Rockstar, ข้อ 2-3 จับที่ตัวเอนจินและ DLL ที่โหลดจริง
/// (Unity/Unreal/Godot/GameMaker/RPG Maker/Ren'Py/LOVE2D) ซึ่งไม่สนใจว่าซื้อมาจากไหน
/// และข้อ 7 จับเกมโหลดโดยเฉพาะ - เกมที่ไม่ผ่านร้านไหนเลยจึงยังถูกจับได้จากหลายทาง
///
/// ===================== สิ่งที่จงใจ "ไม่" ทำ =====================
/// ไม่บล็อกอัตโนมัติทุกอย่างที่คะแนนถึงเกณฑ์แบบไม่มีคนดู - เกณฑ์แบ่ง 3 ระดับ (Allow/Flag/Block)
/// และคะแนนกลางๆ ตกไปที่ Flag ซึ่งแค่บันทึกไว้ให้ครูตัดสิน เหตุผล: ต้นทุนของ false-positive ที่นี่
/// คือ "ปิดโปรแกรมที่นักเรียนกำลังใช้ทำงานส่งกลางคาบ" ซึ่งแพงกว่าการปล่อยเกมหลุดหนึ่งรอบมาก
/// และเมื่อครูตัดสินครั้งเดียว ผลจะผูกกับ hash ตลอดไปทั้ง 40 เครื่อง (IsManual ชนะอัลกอริทึมเสมอ)
///
/// ===================== ข้อจำกัดที่รู้ตัว =====================
/// - เกมเว็บ (ผ่าน browser) ตรวจไม่ได้เลยด้วยวิธีนี้ ต้องคุมที่ระดับ DNS/เว็บ ซึ่งเป็นคนละงาน
/// - เกม portable ที่เขียนเองด้วย engine ที่ไม่ทิ้งร่องรอย จะได้คะแนนต่ำและตกไปที่ Flag
///   (ซึ่งถูกต้องแล้ว - ให้คนดู ดีกว่าเดาผิดแล้วปิดของที่ไม่ควรปิด)
/// - ตัวนี้ไม่ได้แทนที่ระบบ rule เดิม แต่ทำงานคู่กัน: rule คือสิ่งที่ครูรู้แน่ๆ ว่าจะบล็อก
///   ส่วนตัวนี้คือตาข่ายรับของที่ครูยังไม่รู้จัก
/// </summary>
public sealed class ProgramClassifier
{
    // ---- เกณฑ์ตัดสิน (ปรับได้จาก appsettings.json) ----
    private readonly int _blockThreshold;
    private readonly int _flagThreshold;
    private readonly ILogger<ProgramClassifier> _logger;

    public ProgramClassifier(IConfiguration configuration, ILogger<ProgramClassifier> logger)
    {
        _logger = logger;
        _blockThreshold = configuration.GetValue("ProgramControl:BlockThreshold", 70);
        _flagThreshold = configuration.GetValue("ProgramControl:FlagThreshold", 40);
    }

    // ================= ตารางสัญญาณ =================
    // น้ำหนักตั้งจากหลักการเดียว: สัญญาณที่ "ปลอมยาก/ลบแล้วโปรแกรมพัง" ได้น้ำหนักสูงกว่าสัญญาณที่
    // "แก้ได้ด้วย text editor" เสมอ - ชื่อไฟล์กับ metadata จึงมีน้ำหนักต่ำที่สุดในชุดนี้โดยตั้งใจ

    /// <summary>marker ในเส้นทางไฟล์ - key ต้องเป็นตัวเล็กทั้งหมด</summary>
    private static readonly (string Marker, int Weight, ProgramCategory Category, string Description)[] PathMarkers =
    {
        (@"\steamapps\common\", 45, ProgramCategory.Game, "อยู่ในโฟลเดอร์เกมของ Steam"),
        (@"\steamapps\",        35, ProgramCategory.Game, "อยู่ใต้โฟลเดอร์ steamapps"),
        (@"\epic games\",       40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ของ Epic Games"),
        (@"\riot games\",       45, ProgramCategory.Game, "อยู่ในโฟลเดอร์ของ Riot Games"),
        (@"\battle.net\",       40, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ Battle.net"),
        (@"\gog galaxy\",       35, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ GOG Galaxy"),
        (@"\gog games\",        40, ProgramCategory.Game, "อยู่ในโฟลเดอร์เกมของ GOG"),
        (@"\ea games\",         40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ EA Games"),
        (@"\origin games\",     40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Origin Games"),
        (@"\ubisoft\",          38, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Ubisoft"),
        (@"\roblox\",           50, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Roblox"),
        (@"\garena\",           45, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Garena"),
        (@"\wargaming.net\",    40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Wargaming"),
        (@"\minecraft",         45, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Minecraft"),
        (@"\.minecraft",        45, ProgramCategory.Game, "อยู่ในโฟลเดอร์ .minecraft"),
        (@"\mihoyo\",           45, ProgramCategory.Game, "อยู่ในโฟลเดอร์ miHoYo"),
        (@"\hoyoplay\",         45, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ HoYoPlay"),

        // ---- เพิ่ม 2026-08-31: ร้าน/launcher ที่ไม่ใช่ Steam ----
        // เดิมครอบคลุมแค่ร้านใหญ่ไม่กี่เจ้า เกมที่ซื้อ/โหลดจากที่อื่นจึงไม่ได้คะแนนจากกลุ่มนี้เลย
        (@"\itch\apps\",        42, ProgramCategory.Game, "อยู่ในโฟลเดอร์เกมของ itch.io"),
        (@"\xboxgames\",        45, ProgramCategory.Game, "อยู่ในโฟลเดอร์เกมของ Xbox บน PC"),
        (@"\amazon games\",     40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Amazon Games"),
        (@"\rockstar games\",   42, ProgramCategory.Game, "อยู่ในโฟลเดอร์ Rockstar Games"),
        (@"\bethesda.net launcher\", 40, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ Bethesda Launcher"),
        (@"\ea desktop\",       38, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ EA Desktop"),
        (@"\blizzard\",         38, ProgramCategory.GameLauncher, "อยู่ในโฟลเดอร์ Blizzard"),
        (@"\vrchat\",           40, ProgramCategory.Game, "อยู่ในโฟลเดอร์ VRChat"),
    };

    /// <summary>
    /// ร่องรอยของ "เกมที่ถูก crack ให้รันโดยไม่ต้องมี Steam" (2026-08-31)
    ///
    /// กลุ่มนี้ตอบโจทย์ "แล้วถ้าไม่ได้อยู่บน Steam ล่ะ" ตรงที่สุด เพราะเกมโหลดส่วนใหญ่คือเกม Steam
    /// ที่ถูกถอดตัวตรวจลิขสิทธิ์ออกแล้วแทนด้วย emulator ของ Steamworks - ไฟล์พวกนี้ไม่มีทาง
    /// ปรากฏในโปรแกรมที่ถูกลิขสิทธิ์เลยแม้แต่ตัวเดียว จึงเป็นสัญญาณที่ทั้งแรงและแทบไม่มี false positive
    ///
    /// ให้คะแนนสูงและ <b>ไม่โดนหักตามลำดับ</b> เหมือนกลุ่ม BinaryMarkers เพราะแต่ละตัวเป็นหลักฐาน
    /// อิสระจากกันจริงๆ (คนละ crack คนละสำนัก) ไม่ใช่หลักฐานชิ้นเดียวที่นับซ้ำหลายรอบ
    /// </summary>
    private static readonly (string Name, int Weight, string Description)[] CrackMarkers =
    {
        ("steam_emu.ini",          55, "มี steam_emu.ini (ตัวจำลอง Steam ของเกมที่ถูก crack)"),
        ("smartsteamemu.ini",      55, "มี SmartSteamEmu (ตัวจำลอง Steam)"),
        ("coldclientloader.ini",   55, "มี ColdClientLoader (Goldberg Steam emulator)"),
        ("steamclient_loader.exe", 55, "มี steamclient_loader.exe (Goldberg Steam emulator)"),
        ("onlinefix64.dll",        55, "มี OnlineFix (ตัวแพตช์ให้เล่นออนไลน์โดยไม่ต้องมี Steam)"),
        ("onlinefix.ini",          55, "มี OnlineFix"),
        ("steam_settings",         50, "มีโฟลเดอร์ steam_settings (Goldberg Steam emulator)"),
        ("valve.ini",              40, "มี valve.ini (พบในเกมที่ถูก crack)"),
        ("codex.ini",              50, "มี codex.ini (กลุ่มที่ปล่อยเกม crack)"),
        ("hoodlum.ini",            50, "มี hoodlum.ini (กลุ่มที่ปล่อยเกม crack)"),
    };

    /// <summary>
    /// ชื่อโฟลเดอร์ข้างเคียงที่บอกประเภทได้ชัดกว่าไฟล์ .exe เอง - เพิ่ม 2026-08-31 พร้อมกับที่ Agent
    /// เริ่มเก็บชื่อโฟลเดอร์มาให้ (เดิมเก็บแต่ไฟล์ สัญญาณกลุ่มนี้จึงหลุดไปทั้งหมด)
    /// </summary>
    private static readonly (string Name, int Weight, ProgramCategory Category, string Description)[] DirectoryMarkers =
    {
        ("roms",    45, ProgramCategory.Emulator, "มีโฟลเดอร์ roms อยู่ข้างๆ"),
        ("bios",    35, ProgramCategory.Emulator, "มีโฟลเดอร์ bios อยู่ข้างๆ"),
        ("nand",    40, ProgramCategory.Emulator, "มีโฟลเดอร์ nand (อีมูเลเตอร์เครื่องเกม)"),
        ("sdmc",    40, ProgramCategory.Emulator, "มีโฟลเดอร์ sdmc (อีมูเลเตอร์เครื่องเกม)"),
        ("keys",    30, ProgramCategory.Emulator, "มีโฟลเดอร์ keys (กุญแจของอีมูเลเตอร์)"),
        ("firmware", 32, ProgramCategory.Emulator, "มีโฟลเดอร์ firmware"),
        ("states",  30, ProgramCategory.Emulator, "มีโฟลเดอร์ states (เซฟสถานะแบบอีมูเลเตอร์)"),
        ("savestates", 35, ProgramCategory.Emulator, "มีโฟลเดอร์ savestates"),
        ("dumps",   25, ProgramCategory.Emulator, "มีโฟลเดอร์ dumps"),
        ("cheats",  22, ProgramCategory.Emulator, "มีโฟลเดอร์ cheats"),
        ("shaders", 18, ProgramCategory.Emulator, "มีโฟลเดอร์ shaders"),
        ("cores",   30, ProgramCategory.Emulator, "มีโฟลเดอร์ cores (คอร์ของ RetroArch)"),
        ("renpy",   40, ProgramCategory.Game, "สร้างด้วย Ren'Py (เอนจินวิชวลโนเวล)"),
        ("www",     25, ProgramCategory.Game, "มีโฟลเดอร์ www (โครงสร้างของ RPG Maker MV/MZ)"),
        ("gamedata", 25, ProgramCategory.Game, "มีโฟลเดอร์ gamedata"),
        ("savedata", 22, ProgramCategory.Game, "มีโฟลเดอร์ savedata"),
        ("mods",    20, ProgramCategory.Game, "มีโฟลเดอร์ mods"),
    };

    /// <summary>
    /// นามสกุลของไฟล์ข้างเคียง - บางเอนจินไม่ทิ้ง DLL ชื่อตายตัวไว้ให้จับ แต่ทิ้งไฟล์ข้อมูล
    /// รูปแบบเฉพาะของตัวเองไว้เสมอ (Godot = .pck, GameMaker = data.win, Unreal = .pak)
    /// </summary>
    private static readonly (string Extension, int Weight, ProgramCategory Category, string Description)[] ExtensionMarkers =
    {
        (".pck",  35, ProgramCategory.Game, "มีไฟล์ .pck อยู่ข้างๆ (เอนจิน Godot)"),
        (".pak",  20, ProgramCategory.Game, "มีไฟล์ .pak อยู่ข้างๆ (แพ็กข้อมูลเกม)"),
        (".nsp",  50, ProgramCategory.Emulator, "มีไฟล์ .nsp อยู่ข้างๆ (ตลับเกม Nintendo Switch)"),
        (".xci",  50, ProgramCategory.Emulator, "มีไฟล์ .xci อยู่ข้างๆ (ตลับเกม Nintendo Switch)"),
        (".rvz",  45, ProgramCategory.Emulator, "มีไฟล์ .rvz อยู่ข้างๆ (แผ่นเกม GameCube/Wii)"),
        (".wbfs", 45, ProgramCategory.Emulator, "มีไฟล์ .wbfs อยู่ข้างๆ (แผ่นเกม Wii)"),
        (".cso",  40, ProgramCategory.Emulator, "มีไฟล์ .cso อยู่ข้างๆ (แผ่นเกม PSP)"),
        (".gba",  40, ProgramCategory.Emulator, "มีไฟล์ .gba อยู่ข้างๆ (ตลับเกม Game Boy Advance)"),
        (".nds",  40, ProgramCategory.Emulator, "มีไฟล์ .nds อยู่ข้างๆ (ตลับเกม Nintendo DS)"),
        (".3ds",  45, ProgramCategory.Emulator, "มีไฟล์ .3ds อยู่ข้างๆ (ตลับเกม Nintendo 3DS)"),
        (".cia",  45, ProgramCategory.Emulator, "มีไฟล์ .cia อยู่ข้างๆ (เกม Nintendo 3DS)"),
        (".z64",  45, ProgramCategory.Emulator, "มีไฟล์ .z64 อยู่ข้างๆ (ตลับเกม Nintendo 64)"),
        (".n64",  45, ProgramCategory.Emulator, "มีไฟล์ .n64 อยู่ข้างๆ (ตลับเกม Nintendo 64)"),
        (".smc",  42, ProgramCategory.Emulator, "มีไฟล์ .smc อยู่ข้างๆ (ตลับเกม Super Nintendo)"),
        (".sfc",  42, ProgramCategory.Emulator, "มีไฟล์ .sfc อยู่ข้างๆ (ตลับเกม Super Nintendo)"),
        (".nes",  42, ProgramCategory.Emulator, "มีไฟล์ .nes อยู่ข้างๆ (ตลับเกม Famicom/NES)"),
        (".gbc",  40, ProgramCategory.Emulator, "มีไฟล์ .gbc อยู่ข้างๆ (ตลับเกม Game Boy Color)"),
        (".gcm",  42, ProgramCategory.Emulator, "มีไฟล์ .gcm อยู่ข้างๆ (แผ่นเกม GameCube)"),
        (".wud",  42, ProgramCategory.Emulator, "มีไฟล์ .wud อยู่ข้างๆ (แผ่นเกม Wii U)"),
        (".wux",  42, ProgramCategory.Emulator, "มีไฟล์ .wux อยู่ข้างๆ (แผ่นเกม Wii U)"),
        (".chd",  38, ProgramCategory.Emulator, "มีไฟล์ .chd อยู่ข้างๆ (แผ่นเกมแบบบีบอัดของ MAME)"),
        (".pbp",  40, ProgramCategory.Emulator, "มีไฟล์ .pbp อยู่ข้างๆ (เกม PSP)"),
        (".gdi",  38, ProgramCategory.Emulator, "มีไฟล์ .gdi อยู่ข้างๆ (แผ่นเกม Dreamcast)"),
    };

    /// <summary>
    /// ไฟล์ที่อยู่ในโฟลเดอร์เดียวกัน หรือ DLL ที่ process โหลดอยู่ - สัญญาณกลุ่มที่แข็งที่สุด
    /// เพราะลบทิ้งแล้วโปรแกรมรันไม่ได้ (ต่างจาก path ที่ย้ายได้ และ metadata ที่แก้ได้)
    /// </summary>
    private static readonly (string Name, int Weight, ProgramCategory Category, string Description)[] BinaryMarkers =
    {
        ("steam_api64.dll",  40, ProgramCategory.Game, "ใช้ Steamworks SDK (steam_api64.dll)"),
        ("steam_api.dll",    40, ProgramCategory.Game, "ใช้ Steamworks SDK (steam_api.dll)"),
        ("steam_appid.txt",  45, ProgramCategory.Game, "มี steam_appid.txt (ไฟล์ที่มีเฉพาะในเกม Steam)"),
        ("gameoverlayrenderer64.dll", 35, ProgramCategory.Game, "มี Steam overlay hook"),
        ("eossdk-win64-shipping.dll", 35, ProgramCategory.Game, "ใช้ Epic Online Services SDK"),
        ("unityplayer.dll",  28, ProgramCategory.Game, "สร้างด้วย Unity engine"),
        ("gameassembly.dll", 25, ProgramCategory.Game, "สร้างด้วย Unity (IL2CPP)"),
        ("mono-2.0-bdwgc.dll", 12, ProgramCategory.Game, "Mono runtime แบบที่ Unity ใช้"),
        ("fmod.dll",         18, ProgramCategory.Game, "ใช้ FMOD (middleware เสียงของเกม)"),
        ("fmodstudio.dll",   18, ProgramCategory.Game, "ใช้ FMOD Studio"),
        ("wwise.dll",        18, ProgramCategory.Game, "ใช้ Wwise (middleware เสียงของเกม)"),
        ("xinput1_3.dll",    22, ProgramCategory.Game, "โหลด XInput (API สำหรับจอยเกม)"),
        ("xinput1_4.dll",    22, ProgramCategory.Game, "โหลด XInput (API สำหรับจอยเกม)"),
        ("xinput9_1_0.dll",  22, ProgramCategory.Game, "โหลด XInput (API สำหรับจอยเกม)"),
        ("d3d12.dll",        15, ProgramCategory.Game, "ใช้ Direct3D 12"),
        ("d3d11.dll",         8, ProgramCategory.Game, "ใช้ Direct3D 11"),
        ("openvr_api.dll",   25, ProgramCategory.Game, "ใช้ OpenVR"),
        ("discord_game_sdk.dll", 25, ProgramCategory.Game, "ใช้ Discord Game SDK"),

        // ---- เพิ่ม 2026-08-31: เอนจิน/รันไทม์อื่นที่ไม่ใช่ Unity/Unreal ----
        // เกม indie และเกมที่ทำด้วยเครื่องมือฟรีส่วนใหญ่ไม่ได้ใช้ 2 เอนจินนั้น จึงไม่เคยได้คะแนน
        // จากกลุ่มนี้เลยทั้งที่เป็นเกมชัดๆ
        ("data.win",             35, ProgramCategory.Game, "สร้างด้วย GameMaker (data.win)"),
        ("assembly-csharp.dll",  25, ProgramCategory.Game, "สคริปต์เกมของ Unity (Assembly-CSharp.dll)"),
        ("nw.dll",               22, ProgramCategory.Game, "รันบน NW.js (RPG Maker MV/MZ และเกม HTML)"),
        ("love.dll",             30, ProgramCategory.Game, "สร้างด้วย LOVE2D"),
        ("sdl2.dll",             12, ProgramCategory.Game, "ใช้ SDL2 (ไลบรารีที่เกมใช้เป็นหลัก)"),
        ("openal32.dll",         12, ProgramCategory.Game, "ใช้ OpenAL"),
        ("bink2w64.dll",         28, ProgramCategory.Game, "ใช้ Bink Video (มิดเดิลแวร์วิดีโอของเกม)"),
        ("prod.keys",            55, ProgramCategory.Emulator, "มี prod.keys (กุญแจถอดรหัสเกม Nintendo Switch)"),
        ("title.keys",           55, ProgramCategory.Emulator, "มี title.keys (กุญแจถอดรหัสเกม Nintendo Switch)"),
        ("aes_keys.txt",         50, ProgramCategory.Emulator, "มี aes_keys.txt (กุญแจของอีมูเลเตอร์ 3DS)"),
        ("boot9.bin",            50, ProgramCategory.Emulator, "มี boot9.bin (เฟิร์มแวร์ Nintendo 3DS)"),
        ("bios7.bin",            50, ProgramCategory.Emulator, "มี bios7.bin (เฟิร์มแวร์ Nintendo DS)"),
        ("bios9.bin",            50, ProgramCategory.Emulator, "มี bios9.bin (เฟิร์มแวร์ Nintendo DS)"),
        ("firmware.bin",         35, ProgramCategory.Emulator, "มี firmware.bin (เฟิร์มแวร์เครื่องเกม)"),
        ("gba_bios.bin",         50, ProgramCategory.Emulator, "มี gba_bios.bin (เฟิร์มแวร์ Game Boy Advance)"),
        ("scph5501.bin",         50, ProgramCategory.Emulator, "มี BIOS ของ PlayStation 1"),
        ("scph1001.bin",         50, ProgramCategory.Emulator, "มี BIOS ของ PlayStation 1"),
        ("ps3updat.pup",         50, ProgramCategory.Emulator, "มีเฟิร์มแวร์ PlayStation 3"),
        ("dsp_rom.bin",          45, ProgramCategory.Emulator, "มี dsp_rom.bin (เฟิร์มแวร์ GameCube/Wii)"),
        ("keys.bin",             30, ProgramCategory.Emulator, "มี keys.bin"),
        ("dxgi.dll",             15, ProgramCategory.Game, "มี dxgi.dll วางทับในโฟลเดอร์โปรแกรม (มักเป็นตัว hook กราฟิก)"),
    };

    /// <summary>publisher ที่รู้จักว่าเป็นค่ายเกม/แพลตฟอร์มเกม</summary>
    private static readonly (string Fragment, int Weight, ProgramCategory Category)[] PublisherMarkers =
    {
        ("valve", 45, ProgramCategory.GameLauncher),
        ("riot games", 50, ProgramCategory.Game),
        ("epic games", 45, ProgramCategory.Game),
        ("roblox", 50, ProgramCategory.Game),
        ("mihoyo", 50, ProgramCategory.Game),
        ("cognosphere", 50, ProgramCategory.Game),
        ("garena", 50, ProgramCategory.Game),
        ("electronic arts", 45, ProgramCategory.Game),
        ("ubisoft", 45, ProgramCategory.Game),
        ("activision", 45, ProgramCategory.Game),
        ("blizzard", 45, ProgramCategory.Game),
        ("bandai namco", 45, ProgramCategory.Game),
        ("square enix", 45, ProgramCategory.Game),
        ("wargaming", 45, ProgramCategory.Game),
        ("nexon", 45, ProgramCategory.Game),
        ("krafton", 45, ProgramCategory.Game),
        ("netease", 40, ProgramCategory.Game),
        ("tencent", 40, ProgramCategory.Game),
        ("mojang", 50, ProgramCategory.Game),
        ("gameloft", 45, ProgramCategory.Game),
        ("2k games", 45, ProgramCategory.Game),
        ("rockstar", 45, ProgramCategory.Game),
        ("bethesda", 45, ProgramCategory.Game),
    };

    /// <summary>คำในชื่อไฟล์/ProductName - น้ำหนักต่ำโดยตั้งใจ เพราะเป็นชั้นที่ปลอมง่ายที่สุด</summary>
    private static readonly (string Keyword, int Weight, ProgramCategory Category)[] NameKeywords =
    {
        ("-win64-shipping", 35, ProgramCategory.Game),   // ชื่อไฟล์มาตรฐานของ Unreal Engine
        ("-win32-shipping", 35, ProgramCategory.Game),
        ("launcher", 10, ProgramCategory.GameLauncher),
        ("emulator", 30, ProgramCategory.Emulator),

        // ---- อีมูเลเตอร์เครื่องเกม (ขยายชุด 2026-08-31) ----
        // ชื่อเป็นสัญญาณที่อ่อนที่สุด (rename ทีเดียวหาย) แต่ยังคุ้มที่จะมีให้ครบ เพราะส่วนใหญ่
        // นักเรียนไม่ได้ rename และการมีชื่ออยู่ในตารางทำให้คะแนนถึงเกณฑ์เร็วขึ้นเมื่อรวมกับ
        // สัญญาณกลุ่มไฟล์/โฟลเดอร์ที่ rename หนีไม่ได้
        ("ppsspp", 40, ProgramCategory.Emulator),
        ("dolphin", 30, ProgramCategory.Emulator),
        ("citra", 35, ProgramCategory.Emulator),
        ("yuzu", 40, ProgramCategory.Emulator),
        ("ryujinx", 40, ProgramCategory.Emulator),
        ("sudachi", 40, ProgramCategory.Emulator),
        ("suyu", 42, ProgramCategory.Emulator),
        ("eden", 20, ProgramCategory.Emulator),
        ("pcsx2", 40, ProgramCategory.Emulator),
        ("rpcs3", 45, ProgramCategory.Emulator),
        ("vita3k", 45, ProgramCategory.Emulator),
        ("duckstation", 45, ProgramCategory.Emulator),
        ("epsxe", 40, ProgramCategory.Emulator),
        ("retroarch", 40, ProgramCategory.Emulator),
        ("cemu", 42, ProgramCategory.Emulator),
        ("xenia", 42, ProgramCategory.Emulator),
        ("xemu", 42, ProgramCategory.Emulator),
        ("cxbx", 40, ProgramCategory.Emulator),
        ("project64", 45, ProgramCategory.Emulator),
        ("mupen64", 45, ProgramCategory.Emulator),
        ("melonds", 42, ProgramCategory.Emulator),
        ("desmume", 42, ProgramCategory.Emulator),
        ("snes9x", 42, ProgramCategory.Emulator),
        ("zsnes", 42, ProgramCategory.Emulator),
        ("visualboyadvance", 42, ProgramCategory.Emulator),
        ("mgba", 40, ProgramCategory.Emulator),
        ("fceux", 40, ProgramCategory.Emulator),
        ("nestopia", 40, ProgramCategory.Emulator),
        ("flycast", 40, ProgramCategory.Emulator),
        ("redream", 40, ProgramCategory.Emulator),
        ("demul", 42, ProgramCategory.Emulator),
        ("primehack", 40, ProgramCategory.Emulator),
        ("mame", 30, ProgramCategory.Emulator),
        ("dosbox", 25, ProgramCategory.Emulator),
        ("scummvm", 30, ProgramCategory.Emulator),

        // ---- อีมูเลเตอร์ Android (เอาไว้เล่นเกมมือถือบน PC) ----
        ("nox", 25, ProgramCategory.Emulator),
        ("bluestacks", 40, ProgramCategory.Emulator),
        ("ldplayer", 40, ProgramCategory.Emulator),
        ("memu", 30, ProgramCategory.Emulator),
        ("gameloop", 45, ProgramCategory.Emulator),
        ("mumu", 42, ProgramCategory.Emulator),
        ("koplayer", 42, ProgramCategory.Emulator),
        ("smartgaga", 40, ProgramCategory.Emulator),
        ("aow_exe", 40, ProgramCategory.Emulator),
        ("obs", 15, ProgramCategory.Streaming),
        ("discord", 20, ProgramCategory.Chat),
        ("teamviewer", 35, ProgramCategory.RemoteAccess),
        ("anydesk", 35, ProgramCategory.RemoteAccess),
        ("ultraviewer", 35, ProgramCategory.RemoteAccess),
        ("เกม", 20, ProgramCategory.Game),
    };

    /// <summary>
    /// สัญญาณ "ไม่ใช่เกม" - หักคะแนน กันไม่ให้โปรแกรมเรียน/ระบบโดนจับเพราะบังเอิญโหลด d3d11
    /// (โปรแกรม CAD, ตัดต่อวิดีโอ, และแม้แต่ Chrome ก็โหลด Direct3D กันหมด)
    /// </summary>
    private static readonly (string Fragment, int Weight, string Description)[] TrustedPublisherMarkers =
    {
        ("microsoft corporation", -35, "เซ็นโดย Microsoft"),
        ("adobe", -30, "เซ็นโดย Adobe"),
        ("autodesk", -30, "เซ็นโดย Autodesk"),
        ("jetbrains", -30, "เซ็นโดย JetBrains"),
        ("google llc", -25, "เซ็นโดย Google"),
        ("mozilla", -25, "เซ็นโดย Mozilla"),
        ("oracle", -25, "เซ็นโดย Oracle"),
        ("python software foundation", -30, "เซ็นโดย Python Software Foundation"),
        ("the document foundation", -30, "เซ็นโดย The Document Foundation (LibreOffice)"),
        ("dell ", -25, "เซ็นโดย Dell"),
        ("hp inc", -25, "เซ็นโดย HP"),
        ("intel corporation", -30, "เซ็นโดย Intel"),
        ("nvidia corporation", -25, "เซ็นโดย NVIDIA"),
        ("advanced micro devices", -25, "เซ็นโดย AMD"),
        ("onionprocoparetor", -100, "เป็นส่วนหนึ่งของระบบนี้เอง"),
    };

    /// <summary>
    /// เส้นทางที่ต้อง "หักคะแนน" เพราะเป็นเครื่องมือสำหรับเรียน/พัฒนา ที่บังเอิญมีลักษณะเหมือนเกม
    ///
    /// เพิ่มพร้อมกับที่ขยายรายชื่ออีมูเลเตอร์ (2026-08-31): Android SDK มี emulator.exe อยู่จริงๆ
    /// และเป็นของที่นักเรียนสายพัฒนาแอปต้องใช้ - การไปบล็อกมันคือความผิดพลาดที่แพงที่สุดแบบหนึ่ง
    /// ของระบบนี้ (นักเรียนทำงานส่งไม่ได้) หักคะแนนตามเส้นทางไม่ใช่ตามชื่อ เพราะชื่อ emulator.exe
    /// เหมือนกันเป๊ะกับของที่อยากบล็อกจริงๆ - เส้นทางต่างหากที่แยกสองอย่างนี้ออกจากกันได้
    /// </summary>
    private static readonly (string Fragment, int Weight, string Description)[] DeveloperToolPathMarkers =
    {
        (@"\android\sdk\",       -60, "อยู่ใน Android SDK (เครื่องมือพัฒนาแอป)"),
        (@"\android\avd",         -60, "เป็นส่วนหนึ่งของ Android Virtual Device"),
        (@"\androidstudio",       -50, "อยู่ในโฟลเดอร์ Android Studio"),
        (@"\microsoft visual studio\", -50, "อยู่ในโฟลเดอร์ Visual Studio"),
        (@"\jetbrains\",          -50, "อยู่ในโฟลเดอร์ JetBrains"),
        (@"\dotnet\",             -40, "อยู่ในโฟลเดอร์ .NET SDK"),
        (@"\nodejs\",             -40, "อยู่ในโฟลเดอร์ Node.js"),
        (@"\unity\hub\",         -40, "เป็นส่วนหนึ่งของ Unity Hub (เครื่องมือทำเกมสำหรับเรียน)"),
        (@"\unity\editor\",      -40, "เป็นตัว Unity Editor เอง ไม่ใช่เกมที่สร้างจากมัน"),
        (@"\godot",               -25, "เป็นตัว Godot Editor เอง"),
    };

    public ClassificationResult Classify(ProgramFingerprint fingerprint, OnlineLookupOutcome? onlineOutcome = null)
    {
        var signals = new List<ClassificationSignal>();
        var categoryVotes = new Dictionary<ProgramCategory, int>();

        var path = (fingerprint.FullPath ?? string.Empty).ToLowerInvariant().Replace('/', '\\');
        var fileName = (fingerprint.FileName ?? string.Empty).ToLowerInvariant();
        var productName = (fingerprint.ProductName ?? string.Empty).ToLowerInvariant();
        var description = (fingerprint.FileDescription ?? string.Empty).ToLowerInvariant();
        var publisher = (fingerprint.SignerName ?? fingerprint.CompanyName ?? string.Empty).ToLowerInvariant();

        // ================= กฎเหล็ก: ห้ามแตะไฟล์ระบบเด็ดขาด =================
        // ต้องอยู่บนสุดและ return ทันที ไม่ใช่แค่หักคะแนน - การ kill process ใน %SystemRoot%
        // อาจทำให้ Windows พังทั้งเครื่องหรือ bluescreen ได้ ไม่มีคะแนนจากสัญญาณไหนที่ควรชนะข้อนี้
        // (เช่น dwm.exe/explorer.exe โหลด d3d11 + มี xinput อยู่แล้วตามปกติ)
        if (path.StartsWith(@"c:\windows\", StringComparison.Ordinal) ||
            path.Contains(@"\windows\system32\", StringComparison.Ordinal) ||
            path.Contains(@"\windows\syswow64\", StringComparison.Ordinal))
        {
            return new ClassificationResult
            {
                Score = 0,
                Category = ProgramCategory.SystemOrProductivity,
                Action = ProgramAction.Allow,
                Signals = { new ClassificationSignal("SYSTEM_PATH", 0, "อยู่ในโฟลเดอร์ระบบของ Windows - ยกเว้นเสมอ") }
            };
        }

        var score = 0;

        void Add(string code, int weight, string description, ProgramCategory? category = null)
        {
            score += weight;
            signals.Add(new ClassificationSignal(code, weight, description));
            if (category is { } c && c != ProgramCategory.Unknown && weight > 0)
            {
                categoryVotes[c] = categoryVotes.GetValueOrDefault(c) + weight;
            }
        }

        // ---- 1) โครงสร้างโฟลเดอร์ ----
        // นับแค่ marker ที่หนักที่สุดอันเดียว ไม่บวกสะสม - "\steamapps\common\" กับ "\steamapps\"
        // match พร้อมกันเสมอโดยธรรมชาติ ถ้าบวกทั้งคู่จะเป็นการนับหลักฐานชิ้นเดียวซ้ำสองรอบ
        var bestPathMarker = PathMarkers
            .Where(m => path.Contains(m.Marker, StringComparison.Ordinal))
            .OrderByDescending(m => m.Weight)
            .FirstOrDefault();
        if (bestPathMarker.Marker is not null)
        {
            Add("PATH_MARKER", bestPathMarker.Weight, bestPathMarker.Description, bestPathMarker.Category);
        }

        // ---- 1ข) หักคะแนนถ้าเป็นเครื่องมือ dev ที่หน้าตาเหมือนเกม ----
        // ทำก่อนกลุ่มอื่นเพื่อให้เห็นในรายการเหตุผลว่า "ถูกหักเพราะอะไร" ตั้งแต่ต้น เวลาครูเปิดดู
        foreach (var marker in DeveloperToolPathMarkers)
        {
            if (!path.Contains(marker.Fragment, StringComparison.Ordinal)) continue;
            Add("DEV_TOOL_PATH", marker.Weight, marker.Description);
            break;
        }

        // ---- 2) ไฟล์คู่ + DLL ที่โหลดจริง ----
        var binaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in fingerprint.LoadedModules) binaryNames.Add(Path.GetFileName(m));
        foreach (var f in fingerprint.SiblingFiles) binaryNames.Add(Path.GetFileName(f));

        var directoryNames = new HashSet<string>(fingerprint.SiblingDirectories, StringComparer.OrdinalIgnoreCase);

        // ---- 2ก) ร่องรอย crack - ตรวจก่อนกลุ่มอื่นและไม่หักคะแนนตามลำดับ ----
        // เป็นคำตอบตรงๆ ของ "เกมที่ไม่ได้อยู่บน Steam": ส่วนใหญ่คือเกม Steam ที่ถูกถอดตัวตรวจ
        // ลิขสิทธิ์ออก ไฟล์กลุ่มนี้จึงชี้ชัดกว่าอะไรทั้งหมด และไม่มีทางอยู่ในโปรแกรมถูกลิขสิทธิ์
        foreach (var marker in CrackMarkers)
        {
            if (!binaryNames.Contains(marker.Name) && !directoryNames.Contains(marker.Name)) continue;
            Add($"CRACK_{marker.Name}", marker.Weight, marker.Description, ProgramCategory.Game);
        }

        // ---- 2ข) โฟลเดอร์ข้างเคียง ----
        foreach (var marker in DirectoryMarkers)
        {
            if (!directoryNames.Contains(marker.Name)) continue;
            Add($"DIR_{marker.Name}", marker.Weight, marker.Description, marker.Category);
        }

        // ---- 2ค) นามสกุลไฟล์ข้างเคียง - นับ marker ละครั้งเดียวแม้จะมีไฟล์นามสกุลนั้นหลายไฟล์ ----
        foreach (var marker in ExtensionMarkers)
        {
            var hit = fingerprint.SiblingFiles.Any(f => f.EndsWith(marker.Extension, StringComparison.OrdinalIgnoreCase));
            if (!hit) continue;
            Add($"EXT_{marker.Extension}", marker.Weight, marker.Description, marker.Category);
        }

        var binaryHits = 0;
        foreach (var marker in BinaryMarkers)
        {
            if (!binaryNames.Contains(marker.Name)) continue;

            // ลดน้ำหนักของ marker ตัวที่ 3 เป็นต้นไปลงครึ่งหนึ่ง - เกม Unity ตัวเดียวจะ match
            // unityplayer + gameassembly + mono + d3d11 + xinput ครบชุดพร้อมกัน ซึ่งทั้งหมดคือ
            // "หลักฐานว่าเป็น Unity" ชิ้นเดียวกัน ไม่ใช่หลักฐานอิสระ 5 ชิ้น ถ้าบวกเต็มทุกตัวคะแนนจะ
            // ทะลุเกณฑ์ block จากสัญญาณกลุ่มเดียวโดยไม่มีอะไรมายืนยัน
            binaryHits++;
            var weight = binaryHits <= 2 ? marker.Weight : marker.Weight / 2;
            Add($"BINARY_{marker.Name}", weight, marker.Description, marker.Category);
        }

        // ---- 3) publisher ----
        if (!string.IsNullOrWhiteSpace(publisher))
        {
            foreach (var marker in PublisherMarkers)
            {
                if (!publisher.Contains(marker.Fragment, StringComparison.Ordinal)) continue;

                // publisher ที่มาจากลายเซ็นดิจิทัลเชื่อได้มากกว่า metadata ที่แก้ได้ด้วย resource
                // editor - ให้ metadata ครึ่งเดียว ตรงกับหลักการ RequireSignedMatch ของระบบ rule เดิม
                var weight = fingerprint.IsSigned ? marker.Weight : marker.Weight / 2;
                Add("PUBLISHER", weight,
                    $"publisher '{publisher}' เป็นค่ายเกม/แพลตฟอร์มเกม" + (fingerprint.IsSigned ? " (จากลายเซ็นดิจิทัล)" : " (จาก metadata เท่านั้น - เชื่อถือได้น้อยกว่า)"),
                    marker.Category);
                break;
            }

            foreach (var trusted in TrustedPublisherMarkers)
            {
                if (!publisher.Contains(trusted.Fragment, StringComparison.Ordinal)) continue;

                // หักคะแนนเฉพาะเมื่อ "เซ็นจริง" เท่านั้น - ไม่งั้นการเลี่ยงระบบนี้จะง่ายมาก
                // (แค่พิมพ์ "Microsoft Corporation" ลงช่อง CompanyName ของ game.exe ก็ได้ -35 ฟรี)
                if (!fingerprint.IsSigned) continue;

                Add("TRUSTED_PUBLISHER", trusted.Weight, trusted.Description);
                break;
            }
        }

        // ---- 4) คำในชื่อ ----
        var nameHaystack = $"{fileName} {productName} {description}";
        foreach (var keyword in NameKeywords)
        {
            if (!nameHaystack.Contains(keyword.Keyword, StringComparison.OrdinalIgnoreCase)) continue;
            Add("NAME_KEYWORD", keyword.Weight, $"ชื่อ/คำอธิบายมีคำว่า '{keyword.Keyword}'", keyword.Category);
        }

        // ---- 5) ผลจากแหล่งข้อมูลออนไลน์ ----
        if (onlineOutcome is { IsGame: true })
        {
            Add("ONLINE_MATCH", onlineOutcome.Weight,
                $"ตรงกับเกมชื่อ '{onlineOutcome.MatchedName}' ในฐานข้อมูล {onlineOutcome.Source}",
                ProgramCategory.Game);
        }

        // ---- 6) ไม่ได้เซ็นเลย + อยู่นอก Program Files ----
        // ไม่ใช่หลักฐานว่าเป็นเกมในตัวมันเอง (สคริปต์ของนักเรียนเองก็เข้าข่าย) แต่เป็นบริบทที่ทำให้
        // สัญญาณอื่นน่าเชื่อขึ้น - ให้น้ำหนักน้อยและให้ต่อเมื่อมีสัญญาณอื่นอยู่แล้วเท่านั้น
        var inUserWritableLocation =
            path.Contains(@"\downloads\", StringComparison.Ordinal) ||
            path.Contains(@"\desktop\", StringComparison.Ordinal) ||
            path.Contains(@"\appdata\", StringComparison.Ordinal) ||
            path.Contains(@"\temp\", StringComparison.Ordinal) ||
            path.StartsWith(@"d:\", StringComparison.Ordinal);

        if (!fingerprint.IsSigned && inUserWritableLocation && score > 0)
        {
            Add("UNSIGNED_IN_USER_LOCATION", 10,
                "ไม่มีลายเซ็นดิจิทัล และอยู่ในที่ที่ผู้ใช้เขียนไฟล์เองได้ (Downloads/Desktop/AppData)");
        }

        score = Math.Clamp(score, 0, 100);

        var category = categoryVotes.Count > 0
            ? categoryVotes.MaxBy(kv => kv.Value).Key
            : ProgramCategory.Unknown;

        var action = score >= _blockThreshold ? ProgramAction.Block
            : score >= _flagThreshold ? ProgramAction.Flag
            : ProgramAction.Allow;

        _logger.LogDebug("จำแนก {FileName} ได้ {Score} คะแนน -> {Action} ({SignalCount} สัญญาณ)",
            fingerprint.FileName, score, action, signals.Count);

        return new ClassificationResult
        {
            Score = score,
            Category = category,
            Action = action,
            Signals = signals
        };
    }
}
