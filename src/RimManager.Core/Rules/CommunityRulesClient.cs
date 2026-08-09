using System.Text.Json;
using RimManager.Core.Abstractions;
using RimManager.Core.Parsing;
using RimManager.Core.Sorting;

namespace RimManager.Core.Rules;

/// <summary>A fetched community-rules database: the parsed rules, the DB's own
/// timestamp, and the raw JSON (so the caller can cache it verbatim for offline use).</summary>
public sealed record CommunityRulesDatabase(LoadOrderRules Rules, DateTimeOffset? PublishedUtc, string RawJson)
{
    public int RuleCount => Rules.Rules.Count;
}

/// <summary>
/// Fetches the live community load-order rules database (RimSort's
/// <c>Community-Rules-Database</c>) over the GET seam and parses it with the existing
/// <see cref="CommunityRulesParser"/>. Pure orchestration — unit-testable with a fake
/// fetcher — so it takes RimManager from snapshot-only rules to live sync without
/// changing the sorter at all (it still consumes a <see cref="LoadOrderRules"/>).
/// </summary>
/// <remarks>
/// The DB is a single ~400 KB JSON file served from a public raw URL, so no API key or
/// pagination is involved. Its top-level <c>timestamp</c> (unix seconds) is surfaced as
/// <see cref="CommunityRulesDatabase.PublishedUtc"/> for staleness reporting.
/// </remarks>
public sealed class CommunityRulesClient(IHttpFetcher fetcher)
{
    /// <summary>The default upstream: RimSort's community rules database (main branch).</summary>
    public const string DefaultUrl =
        "https://raw.githubusercontent.com/RimSort/Community-Rules-Database/main/communityRules.json";

    private readonly IHttpFetcher _fetcher = fetcher;

    public async Task<CommunityRulesDatabase> FetchAsync(string? url = null, CancellationToken ct = default)
    {
        var json = await _fetcher.GetStringAsync(url ?? DefaultUrl, ct).ConfigureAwait(false);
        return Build(json);
    }

    /// <summary>Parses an already-fetched (or cached) DB body. Split out so the cache
    /// read path and the network path share one implementation.</summary>
    public static CommunityRulesDatabase Build(string json)
    {
        var rules = CommunityRulesParser.Parse(json);
        return new CommunityRulesDatabase(rules, ExtractTimestamp(json), json);
    }

    private static DateTimeOffset? ExtractTimestamp(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("timestamp", out var ts)
                && ts.ValueKind == JsonValueKind.Number
                && ts.TryGetInt64(out var seconds) && seconds > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch (JsonException)
        {
            // A malformed body yields no timestamp; the parser above already tolerates it.
        }

        return null;
    }
}
