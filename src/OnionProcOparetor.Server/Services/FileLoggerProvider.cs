using Microsoft.Extensions.DependencyInjection;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;

namespace OnionProcOparetor.Server.Services;

/// <summary>
/// Logger provider เขียนไฟล์ตรงๆ เสริมจาก console/debug/eventlog provider ปกติของ host
/// จำเป็นเพราะตอนรันเป็น Windows Service (ไม่มี console attach) log ทาง console จะหายไปเงียบๆ
/// ถ้าไม่มี provider อื่นมารับ - ไฟล์นี้เปิดดูได้เสมอไม่ว่าจะรันแบบ console ตรงๆ หรือเป็น service จริง
///
/// เป็นจุดเดียวที่ ILogger ทุกตัวในระบบไหลผ่านตอนรันเป็น Windows Service เลยใช้จุดนี้เป็นทางเดียวกัน
/// ในการดัก Warning ขึ้นไปแล้วบันทึกลง Activity Log ด้วย (แทนที่จะเพิ่ม ILoggerProvider อีกตัว
/// ที่ต้อง format ข้อความซ้ำสองรอบ) - ไม่ดัก Info/Debug เพราะ agent poll ทุก 30 วิ/เครื่องจะทำให้
/// ท่วมตาราง (ตามที่ผู้ใช้ระบุ)
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private IServiceScopeFactory? _scopeFactory;

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// ต้องเรียกหลัง app.Build() เท่านั้น (ตัว provider นี้ถูกสร้างก่อน builder.Build() ตั้งแต่ต้น
    /// Program.cs เพื่อดัก log ให้เร็วที่สุด เลย inject ActivityLogDbContext ผ่าน constructor ตรงๆ
    /// ไม่ได้ - ต้องรับ scope factory มาทีหลังแทน) ก่อนเรียกจุดนี้ Warning+ จะเขียนไฟล์ได้ปกติ
    /// แค่ยังไม่ถูกบันทึกลง Activity Log เท่านั้น
    /// </summary>
    public void AttachServiceScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _filePath, _lock, () => _scopeFactory);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly object _lock;
        private readonly Func<IServiceScopeFactory?> _getScopeFactory;

        public FileLogger(string categoryName, string filePath, object @lock, Func<IServiceScopeFactory?> getScopeFactory)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _lock = @lock;
            _getScopeFactory = getScopeFactory;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_filePath, line + Environment.NewLine);
                }
                catch
                {
                    // เขียน log ไฟล์เองไม่ได้ก็ปล่อยผ่าน ไม่ทำให้ service ทำงานหลักพัง
                }
            }

            if (logLevel >= LogLevel.Warning)
            {
                TryPersistToActivityLog(logLevel, message, exception);
            }
        }

        // reentrancy guard: db.SaveChanges() ข้างล่างอาจทำให้ EF Core log ข้อความของตัวเองกลับเข้า
        // ILogger pipeline นี้อีกรอบ (เช่น "Executing DbCommand"/"Failed executing DbCommand" ที่มา
        // จากการเขียนลง ActivityLogEntries เอง) ถ้าปล่อยให้เรียก TryPersistToActivityLog ซ้ำแบบไม่
        // มีเบรก จะวนเขียนไม่จบ (ยิ่งเขียนไม่สำเร็จยิ่งวน error ซ้ำเข้าไปอีก) - บั๊กจริงที่เจอตอนทดสอบ
        // ครั้งแรก (สร้างแถวซ้ำหลายร้อยแถวรัวๆ จนต้อง kill process) [ThreadStatic] ใช้ได้เพราะ
        // SaveChanges() (ไม่ใช่ Async) รันบน thread เดียวกันตลอดทั้ง call stack ไม่มี context switch
        [ThreadStatic]
        private static bool _isPersistingToActivityLog;

        /// <summary>
        /// เขียนตรงลง ActivityLogDbContext (ข้าม IActivityLogService โดยตั้งใจ - ลดความเสี่ยง
        /// reentrancy เพราะ Log() นี้ถูกเรียกจากที่ไหนก็ได้ในระบบ รวมถึงจาก DbContext เองตอน
        /// query/save) synchronous-blocked เพราะ ILogger.Log เป็น synchronous - พังแล้วปล่อยผ่าน
        /// เหมือนการเขียนไฟล์ข้างบน ไม่ทำให้ request/log หลักพัง
        /// </summary>
        private void TryPersistToActivityLog(LogLevel logLevel, string message, Exception? exception)
        {
            if (_isPersistingToActivityLog)
            {
                return; // กันวนซ้ำ - ดู comment ของ _isPersistingToActivityLog ข้างบน
            }

            var scopeFactory = _getScopeFactory();
            if (scopeFactory is null)
            {
                return; // ยังไม่ผ่าน AttachServiceScopeFactory (เช่น log ที่เกิดระหว่าง startup ก่อน app.Build())
            }

            try
            {
                _isPersistingToActivityLog = true;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ActivityLogDbContext>();

                db.ActivityLogEntries.Add(new ActivityLogEntry
                {
                    Category = ActivityLogCategory.SystemEvent,
                    Severity = logLevel switch
                    {
                        LogLevel.Warning => ActivityLogSeverity.Warning,
                        LogLevel.Error => ActivityLogSeverity.Error,
                        _ => ActivityLogSeverity.Critical // Critical ขึ้นไป
                    },
                    EventType = _categoryName,
                    Description = message,
                    DetailJson = exception?.ToString()
                });

                db.SaveChanges();
            }
            catch
            {
                // เขียนลง Activity Log ไม่ได้ก็ปล่อยผ่าน (เช่น DB ยัง migrate ไม่เสร็จตอน startup) -
                // ไม่ทำให้ logging หลัก/request ที่เรียกมาพัง
            }
            finally
            {
                _isPersistingToActivityLog = false;
            }
        }
    }
}
