using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimManager.Core.Abstractions;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;
using RimManager.Storage;

namespace RimManager.App.Services;

/// <summary>
/// The App's bridge to the community load-order rules: reads the locally synced cache
/// and refreshes it from the upstream database. Thin I/O over the Core-tested
/// <see cref="CommunityRulesClient"/>; the sorter consumes the returned
/// <see cref="LoadOrderRules"/> unchanged.
/// </summary>
public sealed class RulesService(IFileSystem fs, IHttpFetcher fetcher)
{
    private readonly IFileSystem _fs = fs;
    private readonly IHttpFetcher _fetcher = fetcher;

    /// <summary>
    /// When the cache was last written, or null if it never was. Read from the file's
    /// own timestamp rather than a stamp we keep beside it: one fact, one place, and a
    /// cache deleted by hand cannot leave a stamp behind claiming it is current.
    /// </summary>
    public DateTimeOffset? CachedAtUtc()
    {
        var path = AppPaths.CommunityRulesCachePath;
        return _fs.Stat(path) is { } stat ? stat.LastWriteUtc : null;
    }

    /// <summary>Rules from the local cache (written by a previous sync), or empty if none.</summary>
    public LoadOrderRules LoadCached()
    {
        var path = AppPaths.CommunityRulesCachePath;
        if (!_fs.FileExists(path)) return LoadOrderRules.Empty;

        try
        {
            return CommunityRulesClient.Build(_fs.ReadAllText(path)).Rules;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return LoadOrderRules.Empty; // a corrupt cache just means About-only sorting
        }
    }

    /// <summary>Fetches the upstream database, caches it verbatim, and returns the parsed
    /// rules. <paramref name="url"/> overrides the source (N7d); null means the default.</summary>
    public async Task<LoadOrderRules> SyncAsync(string? url = null, CancellationToken ct = default)
    {
        var db = await new CommunityRulesClient(_fetcher).FetchAsync(url, ct).ConfigureAwait(false);
        _fs.CreateDirectory(AppPaths.CacheDir);
        await _fs.AtomicWriteAsync(
            AppPaths.CommunityRulesCachePath, Encoding.UTF8.GetBytes(db.RawJson), backup: false, ct)
            .ConfigureAwait(false);
        return db.Rules;
    }
}
