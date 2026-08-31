namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// Logger provider เขียนไฟล์ตรงๆ เสริมจาก console/debug/eventlog provider ปกติของ host
/// จำเป็นเพราะตอนรันเป็น Windows Service (ไม่มี console attach) log ทาง console จะหายไปเงียบๆ
/// ถ้าไม่มี provider อื่นมารับ - ไฟล์นี้เปิดดูได้เสมอไม่ว่าจะรันแบบ console ตรงๆ หรือเป็น service จริง
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _filePath, _lock);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly object _lock;

        public FileLogger(string categoryName, string filePath, object @lock)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _lock = @lock;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_lock)
            {
                try
                {
                    RotateIfNeeded(_filePath);
                    File.AppendAllText(_filePath, line + Environment.NewLine);
                }
                catch
                {
                    // เขียน log ไฟล์เองไม่ได้ก็ปล่อยผ่าน ไม่ทำให้ agent ทำงานหลักพัง
                }
            }
        }

        /// <summary>
        /// ตัดไฟล์ log เมื่อโตเกินเพดาน แล้วเก็บไฟล์ก่อนหน้าไว้ 1 รุ่น (.1) (2026-08-31)
        ///
        /// เดิมไฟล์นี้โตขึ้นเรื่อยๆ ไม่มีที่สิ้นสุด: LogLevel เป็น Information และ Agent เขียนทุกครั้ง
        /// ที่ poll สำเร็จ (ทุก 30 วิ = ~2,900 บรรทัด/วัน) ส่วน Server เขียนทุก poll ของทั้ง 40 เครื่อง
        /// เครื่องที่เปิดค้างทั้งเทอมโดยไม่มีใครไปลบไฟล์เองจึงมี log หลายร้อย MB - บนเครื่อง lab ที่
        /// พื้นที่ดิสก์ถูกจัดสรรไว้พอดีๆ และมี Reboot Restore Rx กินพื้นที่ snapshot อยู่แล้ว
        ///
        /// เก็บแค่ 1 รุ่นย้อนหลังพอ: log พวกนี้ใช้ debug ปัญหาที่เพิ่งเกิด ไม่ใช่ audit trail
        /// (audit trail จริงอยู่ที่ Activity Log ใน DB ซึ่งมี retention ของตัวเองอยู่แล้ว)
        /// เรียกใต้ lock เดียวกับที่เขียนไฟล์เสมอ - ห้ามให้ thread อื่นเขียนคั่นระหว่างที่ move อยู่
        /// </summary>
        private static void RotateIfNeeded(string filePath)
        {
            const long MaxBytes = 8 * 1024 * 1024;

            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length < MaxBytes)
                {
                    return;
                }

                var previous = filePath + ".1";
                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
                File.Move(filePath, previous);
            }
            catch
            {
                // rotate ไม่ได้ (ไฟล์ถูก lock โดยโปรแกรมอื่นที่เปิดดูอยู่) - เขียนต่อลงไฟล์เดิม
                // ดีกว่าทิ้ง log ทั้งรอบ จะลองใหม่ในบรรทัดถัดไป
            }
        }
    }
}
