// IconStore.cs - read icons from icons.db (SQLite).
// NOT compiled or run yet (no .NET SDK was available when this was written) - treat as a starting point.
// NuGet: Microsoft.Data.Sqlite.   WPF project (net8.0-windows, UseWPF=true).
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace OnionProcOperator.Icons;

public sealed record IconInfo(string Key, string Name, string? NameTh, string Category);

public sealed class IconStore : IDisposable
{
    private readonly SqliteConnection _con;
    private readonly object _gate = new();
    private readonly Dictionary<(string Key, int Size), BitmapSource?> _images = new();

    /// <param name="dbPath">Path to icons.db (ship it next to the exe; it is opened read-only).</param>
    public IconStore(string dbPath)
    {
        _con = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _con.Open();
    }

    /// <summary>All icons, optionally filtered by category - e.g. for an icon picker in the roster manager.</summary>
    public IReadOnlyList<IconInfo> List(string? category = null)
    {
        lock (_gate)
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT key, name, name_th, category FROM icon " +
                              "WHERE $c IS NULL OR category = $c ORDER BY category, key";
            cmd.Parameters.AddWithValue("$c", (object?)category ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var list = new List<IconInfo>();
            while (r.Read())
                list.Add(new IconInfo(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3)));
            return list;
        }
    }

    /// <summary>PNG as a frozen WPF image. Picks the smallest stored size >= requested, else the largest. Cached.</summary>
    public BitmapSource? GetImage(string key, int size)
    {
        lock (_gate)
        {
            if (_images.TryGetValue((key, size), out var hit)) return hit;

            using var cmd = _con.CreateCommand();
            cmd.CommandText =
                "SELECT png FROM icon_raster WHERE icon_id = (SELECT id FROM icon WHERE key = $k) " +
                "ORDER BY (size < $s), ABS(size - $s) LIMIT 1";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$s", size);
            var bytes = cmd.ExecuteScalar() as byte[];

            BitmapSource? img = null;
            if (bytes != null)
            {
                using var ms = new MemoryStream(bytes);
                var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                img = frame;
            }
            return _images[(key, size)] = img;
        }
    }

    /// <summary>Raw multi-size .ico bytes, e.g. for System.Drawing.Icon / NotifyIcon:
    /// <c>new Icon(new MemoryStream(bytes), new Size(16, 16))</c></summary>
    public byte[]? GetIcoBytes(string key)
    {
        lock (_gate)
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT ico FROM icon WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as byte[];
        }
    }

    /// <summary>sha256 of the icon's SVGs - use it to know when a cached copy is stale.</summary>
    public string? GetHash(string key)
    {
        lock (_gate)
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT sha256 FROM icon WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void Dispose() => _con.Dispose();
}
