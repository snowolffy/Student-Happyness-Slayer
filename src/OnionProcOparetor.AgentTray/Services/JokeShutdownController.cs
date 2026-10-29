using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;

namespace OnionProcOparetor.AgentTray.Services;

/// <summary>
/// Easter egg มุกจอ shutdown - เล่นเสียงนับถอยหลังจรวด (ICBMStrike.mp3, ยาว AudioDurationSeconds วิ)
/// แล้วสลับเป็นจอฟ้าปลอมทันทีที่วินาทีที่ 3.5 ของเสียง ปิด default (JokeShutdown:Enabled=false ใน
/// appsettings.json) และทำงานเฉพาะตอนนับถอยหลังทั้งหมด &gt;= AudioDurationSeconds วิเท่านั้น (สั้นกว่า
/// นี้ไม่มีทางเล่นเสียงจบก่อนเครื่องดับจริง)
///
/// เสียงต้อง "จบพอดีตอนเครื่องดับ" เสมอไม่ว่า DelaySeconds ทั้งหมดจะยาวแค่ไหน (10, 15, 60 วิ ก็ตาม) -
/// จึงต้องรอจนกว่าเวลาที่เหลือจะเท่ากับ AudioDurationSeconds พอดีก่อนค่อยเริ่มเล่น ไม่ใช่เริ่มเล่นทันที
/// ตอน DelaySeconds เต็มที่ (ถ้า DelaySeconds ยาวกว่า 10 วิ แล้วเริ่มเล่นทันที เสียงจะจบและเข้าจอฟ้าปลอม
/// เร็วเกินไป ทิ้งช่วงเงียบๆ รอ shutdown จริงอีกนาน)
///
/// ไม่แตะ flow shutdown จริงเลย - เป็นแค่ overlay คู่ขนานกับ ShutdownNotifyWindow ปกติ (ดู
/// App.xaml.cs OnShutdownNotifyRequested) ฝั่ง Agent ยิง ExitWindowsEx ของตัวเองแยกต่างหากอยู่แล้ว
/// ไม่ว่าหน้าต่างนี้จะโหลดสำเร็จหรือพังก็ตาม - ทุกจุดที่แตะไฟล์ asset ครอบ try-catch ไว้หมด แล้ว
/// เงียบๆ ปล่อยผ่านถ้าพัง (ไม่ใช่ฟีเจอร์หลัก ห้ามทำให้ Tray ล่มตอน shutdown จริงเด็ดขาด)
/// </summary>
public class JokeShutdownController
{
    private const int AudioDurationSeconds = 10;
    private const double ExplodeAtSeconds = 4.2;

    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Joke");
    private static readonly string AudioPath = Path.Combine(AssetsDir, "ICBMStrike.mp3");
    private static readonly string ImagePath = Path.Combine(AssetsDir, "windows-modern-10-bsod.png");

    private DispatcherTimer? _waitTimer;
    private MediaPlayer? _player;
    private DispatcherTimer? _explodeTimer;
    private BsodWindow? _bsodWindow;

    /// <summary>เช็ค config + เงื่อนไขเวลาก่อนเรียก Start() เสมอ - แยกเป็น static เพราะไม่ต้องมี state ของ instance มาเกี่ยวข้อง</summary>
    public static bool ShouldRun(int delaySeconds)
    {
        if (delaySeconds < AudioDurationSeconds)
        {
            return false;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        return config.GetValue<bool>("JokeShutdown:Enabled");
    }

    /// <summary>
    /// เริ่มมุก - เรียกคู่กับตอนเปิด ShutdownNotifyWindow ปกติ (ไม่แทนที่) ผู้เรียกต้อง subscribe
    /// ShutdownNotifyWindow.Closed มาเรียก Cancel() เองด้วยเสมอ เพื่อให้จอฟ้าปลอมปิดพร้อมกับจังหวะ
    /// shutdown จริง (ดู doc comment ของ BsodWindow) - delaySeconds ต้องเป็นค่าเดียวกับที่ส่งให้
    /// ShutdownNotifyWindow เป๊ะๆ เพื่อคำนวณว่าต้องรอกี่วิก่อนเริ่มเล่นเสียง
    /// </summary>
    public void Start(int delaySeconds)
    {
        var waitSeconds = delaySeconds - AudioDurationSeconds;

        if (waitSeconds <= 0)
        {
            StartAudioSequence();
            return;
        }

        _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(waitSeconds) };
        _waitTimer.Tick += (_, _) =>
        {
            _waitTimer?.Stop();
            _waitTimer = null;
            StartAudioSequence();
        };
        _waitTimer.Start();
    }

    private void StartAudioSequence()
    {
        try
        {
            if (!File.Exists(AudioPath))
            {
                TrayDiagnostics.Log($"[joke] ไม่พบไฟล์เสียง {AudioPath} - ข้ามมุกนี้รอบนี้");
                return;
            }

            _player = new MediaPlayer();
            _player.Open(new Uri(AudioPath, UriKind.Absolute));
            _player.Play();

            _explodeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ExplodeAtSeconds) };
            _explodeTimer.Tick += (_, _) => Explode();
            _explodeTimer.Start();
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[joke] เริ่มมุกไม่สำเร็จ: {ex.Message} - ข้าม ShutdownNotifyWindow ปกติทำงานต่อได้เหมือนเดิม");
            Cancel();
        }
    }

    private void Explode()
    {
        _explodeTimer?.Stop();
        _explodeTimer = null;

        try
        {
            _bsodWindow = new BsodWindow(ImagePath);
            _bsodWindow.Show();
        }
        catch (Exception ex)
        {
            TrayDiagnostics.Log($"[joke] เปิดจอฟ้าปลอมไม่สำเร็จ: {ex.Message}");
        }
    }

    /// <summary>
    /// เลิกมุกกลางคัน - idempotent เรียกซ้ำได้เสมอไม่มีผลข้างเคียง (ดู App.xaml.cs
    /// OnShutdownNotifyRequested ที่เรียกทุกครั้งก่อนเริ่มมุกใหม่ กันเคสมีคำสั่งเก่าค้างอยู่)
    /// </summary>
    public void Cancel()
    {
        _waitTimer?.Stop();
        _waitTimer = null;

        _explodeTimer?.Stop();
        _explodeTimer = null;

        _player?.Stop();
        _player?.Close();
        _player = null;

        _bsodWindow?.ForceClose();
        _bsodWindow = null;
    }
}
