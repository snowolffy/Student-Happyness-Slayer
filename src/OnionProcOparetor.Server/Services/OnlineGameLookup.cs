using System.Text.Json;

namespace OnionProcOparetor.Server.Services;

public sealed record OnlineLookupOutcome(bool IsGame, string? MatchedName, string Source, int Weight);

/// <summary>
/// ตรวจกับฐานข้อมูลเกมออนไลน์ว่าชื่อโปรแกรมที่เจอเป็นชื่อเกมจริงหรือไม่ (2026-08-31)
///
/// ===================== ทำไม lookup อยู่ที่ Server ไม่ใช่ Agent =====================
/// 1. เครื่องนักเรียน 40 เครื่องไม่ควรยิงออกอินเทอร์เน็ตพร้อมกัน (และบางเครื่องอาจถูกจำกัดเน็ตอยู่)
/// 2. ผลลัพธ์แชร์กันได้ - เครื่องแรกที่เจอเกมหนึ่ง ทำให้อีก 39 เครื่องรู้จักทันทีโดยไม่ต้องถามซ้ำ
/// 3. ควบคุม rate limit / เก็บ cache ที่เดียว
///
/// ===================== แหล่งข้อมูล =====================
/// - Steam (default, ไม่ต้องมี API key): โหลดรายชื่อ appid+name ทั้งหมดจาก ISteamApps/GetAppList
///   มาเก็บเป็น index ในหน่วยความจำ + cache ลงไฟล์ รีเฟรชสัปดาห์ละครั้ง เทียบแบบ normalize ชื่อ
///   (ตัดอักขระที่ไม่ใช่ตัวอักษร/ตัวเลขออก) เพื่อให้ "PLANTSVSZOMBIES.exe" ตรงกับ
///   "Plants vs. Zombies" ได้
/// - IGDB / RAWG (optional, ต้องมี key ใน appsettings.json): ไม่ได้เปิดใช้โดย default เพราะทั้งคู่
///   ต้องสมัคร credential เอง ถ้าไม่ได้ตั้งค่าไว้ก็ข้ามไปเงียบๆ ไม่ถือเป็น error
///
/// ===================== ทำไมต้องระวังเรื่อง false positive =====================
/// ชื่อเกมบน Steam มีคำสามัญเยอะมาก ("Journey", "Limbo", "Superliminal") การ match ชื่อสั้นๆ
/// แบบหลวมๆ จะจับโปรแกรมทั่วไปติดมาด้วยแน่นอน - จึงบังคับความยาวขั้นต่ำหลัง normalize และ
/// ให้น้ำหนักเป็นแค่ "สัญญาณหนึ่งตัว" ไม่ใช่คำตัดสิน (ดู ProgramClassifier: ONLINE_MATCH บวก
/// คะแนนเข้าไปในผลรวม ไม่ได้ override อะไร)
/// </summary>
public sealed class OnlineGameLookup
{
    private const string SteamAppListUrl = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";

    /// <summary>ชื่อที่สั้นกว่านี้หลัง normalize จะไม่ถูกนำไปเทียบเลย - ป้องกัน match มั่วกับคำสามัญ</summary>
    private const int MinimumNameLength = 6;

    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromDays(7);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OnlineGameLookup> _logger;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private HashSet<string>? _steamNameIndex;
    private Dictionary<string, string>? _steamOriginalNames;
    private DateTime _indexLoadedAt = DateTime.MinValue;

    public OnlineGameLookup(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OnlineGameLookup> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsEnabled => _configuration.GetValue("ProgramControl:EnableOnlineLookup", true);

    private string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OnionProcOparetor", "Server", "steam-applist.json");

    /// <summary>
    /// normalize ชื่อให้เทียบกันได้: ตัวเล็กทั้งหมด และเหลือเฉพาะตัวอักษร/ตัวเลข
    /// "Counter-Strike 2" / "counterstrike2.exe" / "CounterStrike2" -> "counterstrike2" เหมือนกันหมด
    /// </summary>
    private static string Normalize(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }
        return new string(buffer[..length]);
    }

    /// <summary>
    /// หาว่าชื่อพวกนี้ (ชื่อไฟล์/ProductName/ชื่อโฟลเดอร์แม่) ตรงกับชื่อเกมในฐานข้อมูลไหม
    /// ส่งหลายชื่อเข้ามาได้ - เกมจำนวนมากตั้งชื่อ .exe เป็นตัวย่อ แต่ชื่อโฟลเดอร์เป็นชื่อเต็ม
    /// </summary>
    public async Task<OnlineLookupOutcome?> LookupAsync(IEnumerable<string?> candidateNames, CancellationToken ct)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            var index = await EnsureSteamIndexAsync(ct);
            if (index is null)
            {
                return null;
            }

            foreach (var candidate in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                // ตัดนามสกุลและ suffix ของ Unreal ออกก่อน normalize - "MyGame-Win64-Shipping.exe"
                // ไม่มีทางตรงกับ "My Game" ในฐานข้อมูลถ้าไม่ตัดส่วนนี้ทิ้ง
                var cleaned = candidate;
                cleaned = Path.GetFileNameWithoutExtension(cleaned);
                cleaned = cleaned.Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace("-Win32-Shipping", "", StringComparison.OrdinalIgnoreCase);

                var normalized = Normalize(cleaned);
                if (normalized.Length < MinimumNameLength) continue;

                if (index.Contains(normalized))
                {
                    var original = _steamOriginalNames?.GetValueOrDefault(normalized) ?? cleaned;
                    _logger.LogInformation("ชื่อ '{Candidate}' ตรงกับเกมบน Steam: '{Match}'", candidate, original);
                    return new OnlineLookupOutcome(true, original, "Steam", 45);
                }
            }

            return new OnlineLookupOutcome(false, null, "Steam", 0);
        }
        catch (Exception ex)
        {
            // ค้นออนไลน์ไม่ได้ = ใช้สัญญาณ local อย่างเดียว ไม่ใช่เหตุให้ทั้งการจำแนกล้มเหลว
            _logger.LogWarning(ex, "ค้นหาข้อมูลเกมออนไลน์ไม่สำเร็จ - จะใช้เฉพาะสัญญาณในเครื่อง");
            return null;
        }
    }

    private async Task<HashSet<string>?> EnsureSteamIndexAsync(CancellationToken ct)
    {
        if (_steamNameIndex is not null && DateTime.UtcNow - _indexLoadedAt < IndexMaxAge)
        {
            return _steamNameIndex;
        }

        await _indexLock.WaitAsync(ct);
        try
        {
            // เช็คซ้ำหลังได้ lock - อีก thread อาจโหลดเสร็จไปแล้วระหว่างที่รอคิว
            if (_steamNameIndex is not null && DateTime.UtcNow - _indexLoadedAt < IndexMaxAge)
            {
                return _steamNameIndex;
            }

            var json = await ReadCachedListAsync(ct) ?? await DownloadListAsync(ct);
            if (json is null)
            {
                return _steamNameIndex; // อาจเป็น index เก่าที่ยังใช้ได้ หรือ null ถ้าไม่เคยโหลดสำเร็จเลย
            }

            BuildIndex(json);
            return _steamNameIndex;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<string?> ReadCachedListAsync(CancellationToken ct)
    {
        try
        {
            var path = CachePath;
            if (!File.Exists(path)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > IndexMaxAge) return null;

            return await File.ReadAllTextAsync(path, ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> DownloadListAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(OnlineGameLookup));
        client.Timeout = TimeSpan.FromSeconds(60);

        _logger.LogInformation("กำลังโหลดรายชื่อเกมจาก Steam (ทำสัปดาห์ละครั้ง)...");
        var json = await client.GetStringAsync(SteamAppListUrl, ct);

        try
        {
            var path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex)
        {
            // เขียน cache ไม่ได้ก็ยังใช้ index ในหน่วยความจำรอบนี้ได้ แค่จะต้องโหลดใหม่ตอน restart
            _logger.LogWarning(ex, "เขียน cache รายชื่อเกมลงดิสก์ไม่สำเร็จ");
        }

        return json;
    }

    private void BuildIndex(string json)
    {
        var index = new HashSet<string>(StringComparer.Ordinal);
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("applist", out var appList) ||
            !appList.TryGetProperty("apps", out var apps))
        {
            _logger.LogWarning("รูปแบบข้อมูลจาก Steam ไม่ตรงกับที่คาดไว้ - ข้ามการสร้าง index");
            return;
        }

        foreach (var app in apps.EnumerateArray())
        {
            if (!app.TryGetProperty("name", out var nameElement)) continue;
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var normalized = Normalize(name);
            if (normalized.Length < MinimumNameLength) continue;

            if (index.Add(normalized))
            {
                originals[normalized] = name;
            }
        }

        _steamNameIndex = index;
        _steamOriginalNames = originals;
        _indexLoadedAt = DateTime.UtcNow;

        _logger.LogInformation("สร้าง index ชื่อเกมจาก Steam เสร็จแล้ว {Count} รายการ", index.Count);
    }
}
