using System.Text.Json;
using Microsoft.Data.Sqlite;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.Storage;

/// <summary>
/// SQLite-backed <see cref="IModCache"/> — a disposable derived cache keyed on the
/// About.xml path + stat. Writes are buffered in a transaction and committed on
/// <see cref="Flush"/> so a full 1,500-mod scan is one commit, not 1,500.
/// </summary>
public sealed class SqliteModCache : IModCache, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new ModIdJsonConverter() },
    };

    /// <summary>
    /// Bump whenever the cached shape or the scan semantics that produce a
    /// <see cref="Mod"/> change (e.g. the LoadFolders content-detection fix). On a
    /// mismatch the cache is rebuilt, so a logic change can never serve stale rows.
    /// <para>
    /// 3 · N1 gave Core and the DLC their names, since Ludeon's About.xml files carry
    /// no <c>&lt;name&gt;</c>. The cache is keyed on that file's mtime and size, and
    /// Ludeon's never change — so without this bump the fix would have been correct in
    /// the scanner and invisible on every install that had ever scanned once. A cache
    /// whose key cannot notice a logic change is why this constant exists.
    /// </para>
    /// <para>
    /// 4 · a local mod holding a <c>.git</c> directory is now <c>ModSource.Git</c>. The
    /// source is part of the cached <c>Mod</c> and the key is still only About.xml, and
    /// cloning a repo does not touch About.xml — so the same trap as 3, one slice later.
    /// </para>
    /// </summary>
    // 5: ContentFlags gained RwList (NF-10) — cached mods scanned before the flag
    //    existed would never show it, and About.xml mtime cannot notice (the lesson
    //    this constant's doc already paid for twice).
    private const long CacheVersion = 5;

    private readonly SqliteConnection _conn;
    private SqliteTransaction? _pending;

    private SqliteModCache(SqliteConnection conn) => _conn = conn;

    /// <summary>Opens (creating if needed) a cache database at <paramref name="dbPath"/>.</summary>
    public static SqliteModCache Open(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            // If the cache was written by an older scanner, discard it wholesale.
            cmd.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (version != CacheVersion)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS mods;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"PRAGMA user_version = {CacheVersion};";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS mods (
                    about_path  TEXT PRIMARY KEY,
                    mtime_ticks INTEGER NOT NULL,
                    size        INTEGER NOT NULL,
                    json        TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        return new SqliteModCache(conn);
    }

    public Mod? TryGet(string aboutXmlPath, FileEntry stat)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT mtime_ticks, size, json FROM mods WHERE about_path = $p";
        cmd.Parameters.AddWithValue("$p", aboutXmlPath);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var mtimeTicks = reader.GetInt64(0);
        var size = reader.GetInt64(1);
        if (mtimeTicks != stat.LastWriteUtc.UtcTicks || size != stat.Size)
        {
            return null; // stale: the About.xml changed since we cached it
        }

        var json = reader.GetString(2);
        try
        {
            return JsonSerializer.Deserialize<Mod>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // corrupt row -> treat as a miss, it'll be overwritten
        }
    }

    public void Put(string aboutXmlPath, FileEntry stat, Mod mod)
    {
        _pending ??= _conn.BeginTransaction();

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _pending;
        cmd.CommandText = """
            INSERT INTO mods (about_path, mtime_ticks, size, json)
            VALUES ($p, $m, $s, $j)
            ON CONFLICT(about_path) DO UPDATE SET
                mtime_ticks = excluded.mtime_ticks,
                size        = excluded.size,
                json        = excluded.json;
            """;
        cmd.Parameters.AddWithValue("$p", aboutXmlPath);
        cmd.Parameters.AddWithValue("$m", stat.LastWriteUtc.UtcTicks);
        cmd.Parameters.AddWithValue("$s", stat.Size);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(mod, JsonOptions));
        cmd.ExecuteNonQuery();
    }

    public void Flush()
    {
        _pending?.Commit();
        _pending?.Dispose();
        _pending = null;
    }

    public void Dispose()
    {
        Flush();
        _conn.Dispose();
    }
}
