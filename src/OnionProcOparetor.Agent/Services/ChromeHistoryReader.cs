using Microsoft.Data.Sqlite;

namespace OnionProcOparetor.Agent.Services;

/// <summary>
/// อ่านตาราง urls/visits จากไฟล์ History (SQLite) ของ Chrome/Edge แบบ read-only (WP2-4, 2026-10-31) -
/// เป็น "หัวอ่าน" ล้วนๆ ไม่รู้จัก path จริงของ Chrome/Edge บนเครื่องนี้เลย (รับ path ไฟล์ตรงๆ เป็น
/// พารามิเตอร์ ไม่ค้นหาเอง) เพื่อทดสอบได้ด้วยไฟล์ SQLite ทดลองใน temp โดยไม่ต้องแตะ browser profile จริง
/// ของเครื่อง dev (ดู docs/roadmap.md ขั้น 2 "ข้อจำกัดในการทดสอบ") - เดินตามแนวทางเดียวกับ
/// BrowserHardeningService/WebsiteControlService ที่รับ registry root เป็นพารามิเตอร์ด้วยเหตุผลเดียวกัน
/// (ผู้เรียกจริง - WebHistoryCollectionService - เป็นคนหา path และสำเนาไฟล์มาให้ก่อนเรียกคลาสนี้)
///
/// Chrome เก็บเวลาเป็น "WebKit/Chrome timestamp" = ไมโครวินาทีนับจาก 1601-01-01 UTC (ไม่ใช่ Unix epoch
/// และไม่ใช่ .NET ticks ตรงๆ) ต้องแปลงเองเสมอ
///
/// throw ถ้าเปิดไฟล์ไม่ได้/query ผิด (ไม่กลืน exception ไว้เอง) - ผู้เรียกเป็นคน catch ต่อ browser แยกกัน
/// (ดู WebHistoryCollectionService.CollectNewVisits) กัน browser หนึ่งอ่านพังแล้วลากอีก browser ไม่ให้
/// ได้อ่านไปด้วย
/// </summary>
public static class ChromeHistoryReader
{
    /// <summary>ChromeTimestamp คือค่าดิบจากคอลัมน์ visits.visit_time - เก็บไว้ใช้เป็น cursor รอบถัดไป
    /// (แม่นกว่าแปลงเป็น DateTime แล้วแปลงกลับ เพราะไม่มีปัดเศษ)</summary>
    public sealed record Visit(string Url, string? Title, long ChromeTimestamp, DateTime VisitedAtUtc);

    private static readonly DateTime ChromeEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// อ่าน visit ใหม่ทั้งหมดที่ visit_time > sinceChromeTimestamp จากไฟล์ที่ sqliteFilePath (ต้องเป็น
    /// สำเนาที่ก๊อปมาแล้ว ไม่ใช่ไฟล์ที่ Chrome/Edge เปิดอยู่จริงตรงๆ - ดู comment บนคลาส) เรียงจากเก่าไปใหม่
    /// (visit_time ASC) เพื่อให้ผู้เรียกเอา ChromeTimestamp ของแถวสุดท้ายไปเป็น cursor รอบถัดไปได้ตรงๆ
    /// </summary>
    public static List<Visit> ReadNewVisits(string sqliteFilePath, long sinceChromeTimestamp)
    {
        var results = new List<Visit>();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFilePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT urls.url, urls.title, visits.visit_time " +
            "FROM visits JOIN urls ON visits.url = urls.id " +
            "WHERE visits.visit_time > $since " +
            "ORDER BY visits.visit_time ASC";
        command.Parameters.AddWithValue("$since", sinceChromeTimestamp);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var url = reader.GetString(0);
            var title = reader.IsDBNull(1) ? null : reader.GetString(1);
            var chromeTimestamp = reader.GetInt64(2);

            // ไมโครวินาที (Chrome) -> ticks (.NET, 100ns ต่อ tick) คูณ 10 พอดี
            var visitedAtUtc = ChromeEpoch.AddTicks(chromeTimestamp * 10);

            results.Add(new Visit(url, title, chromeTimestamp, visitedAtUtc));
        }

        return results;
    }
}
