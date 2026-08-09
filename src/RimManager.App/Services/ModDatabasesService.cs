using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimManager.Core.Abstractions;
using RimManager.Core.ModDatabases;
using RimManager.Storage;

namespace RimManager.App.Services;

/// <summary>
/// The App's bridge to Mlie's two mod databases (N7): reads the locally synced caches
/// and refreshes them from upstream. The same shape as <see cref="RulesService"/>,
/// because they are the same kind of thing — a remote database feeding local mod
/// metadata, cached under <c>cache/</c>, consumed by the validator unchanged.
/// </summary>
public sealed class ModDatabasesService(IFileSystem fs, IHttpFetcher fetcher)
{
    private readonly IFileSystem _fs = fs;
    private readonly IHttpFetcher _fetcher = fetcher;

    /// <summary>When each cache was last written, from the file's own timestamp —
    /// one fact, one place, the <see cref="RulesService.CachedAtUtc"/> rule.</summary>
    public DateTimeOffset? ReplacementsCachedAtUtc() =>
        _fs.Stat(AppPaths.ReplacementsCachePath) is { } stat ? stat.LastWriteUtc : null;

    public DateTimeOffset? KnownGoodCachedAtUtc(string? gameMajorMinor) =>
        gameMajorMinor is not null
        && _fs.Stat(AppPaths.KnownGoodCachePath(gameMajorMinor)) is { } stat
            ? stat.LastWriteUtc
            : null;

    /// <summary>Replacements from the local cache, or empty if never synced.</summary>
    public ReplacementDatabase LoadCachedReplacements()
    {
        var path = AppPaths.ReplacementsCachePath;
        if (!_fs.FileExists(path)) return ReplacementDatabase.Empty;

        try
        {
            return UseThisInsteadParser.Parse(_fs.ReadAllText(path));
        }
        catch (IOException)
        {
            return ReplacementDatabase.Empty; // a corrupt cache just means no suggestions
        }
    }

    /// <summary>The known-good list for one game version from the local cache, or empty.</summary>
    public KnownGoodDatabase LoadCachedKnownGood(string? gameMajorMinor)
    {
        if (gameMajorMinor is null) return KnownGoodDatabase.Empty;

        var path = AppPaths.KnownGoodCachePath(gameMajorMinor);
        if (!_fs.FileExists(path)) return KnownGoodDatabase.Empty;

        try
        {
            return NoVersionWarningParser.Parse(_fs.ReadAllText(path));
        }
        catch (IOException)
        {
            return KnownGoodDatabase.Empty;
        }
    }

    /// <summary>Fetches the replacements database, caches it decompressed, returns it.
    /// <paramref name="url"/> overrides the source (N7d); null means the default.</summary>
    public async Task<ReplacementDatabase> SyncReplacementsAsync(
        string? url = null, CancellationToken ct = default)
    {
        var db = await new UseThisInsteadClient(_fetcher).FetchAsync(url, ct).ConfigureAwait(false);
        _fs.CreateDirectory(AppPaths.CacheDir);
        await _fs.AtomicWriteAsync(
                AppPaths.ReplacementsCachePath, Encoding.UTF8.GetBytes(db.RawJson), backup: false, ct)
            .ConfigureAwait(false);
        return db;
    }

    /// <summary>
    /// Fetches the known-good list for a game version and caches it. An upstream 404 —
    /// no list for this version yet — returns empty and leaves any previous cache
    /// alone: absence upstream must not delete a list that existed yesterday.
    /// </summary>
    public async Task<KnownGoodDatabase> SyncKnownGoodAsync(
        string gameMajorMinor, string? baseUrl = null, CancellationToken ct = default)
    {
        var db = await new NoVersionWarningClient(_fetcher)
            .FetchAsync(gameMajorMinor, baseUrl, ct).ConfigureAwait(false);
        if (db.Count == 0) return LoadCachedKnownGood(gameMajorMinor);

        _fs.CreateDirectory(AppPaths.CacheDir);
        await _fs.AtomicWriteAsync(
                AppPaths.KnownGoodCachePath(gameMajorMinor), Encoding.UTF8.GetBytes(db.RawXml),
                backup: false, ct)
            .ConfigureAwait(false);
        return db;
    }
}
