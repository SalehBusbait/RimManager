using System.Collections.Immutable;
using System.Xml.Linq;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.Core.ModDatabases;

/// <summary>
/// Mlie's NoVersionWarning list for one game version: packageIds reported working on
/// that version despite not declaring support for it. Feeds the unsupported-version
/// check as a suppression — the warning stays for everyone else.
/// </summary>
public sealed record KnownGoodDatabase(ImmutableHashSet<ModId> ModIds, string RawXml)
{
    public static readonly KnownGoodDatabase Empty = new([], string.Empty);

    public int Count => ModIds.Count;

    public bool Contains(ModId id) => ModIds.Contains(id);
}

/// <summary>Parses one <c>ModIdsToFix.xml</c>: a flat <c>&lt;li&gt;</c> list of packageIds.</summary>
public static class NoVersionWarningParser
{
    public static KnownGoodDatabase Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (string.IsNullOrWhiteSpace(xml)) return KnownGoodDatabase.Empty;

        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null || root.Name.LocalName != "ModIdsToFix") return KnownGoodDatabase.Empty;

            var ids = root.Elements("li")
                .Select(li => li.Value.Trim())
                .Where(v => v.Length > 0)
                .Select(ModId.From)
                .ToImmutableHashSet();

            return new KnownGoodDatabase(ids, xml);
        }
        catch (System.Xml.XmlException)
        {
            return KnownGoodDatabase.Empty;
        }
    }
}

/// <summary>
/// Fetches Mlie's NoVersionWarning list for a game version. The list is a file per
/// version (<c>1.6/ModIdsToFix.xml</c>), so the URL is parameterised — and a version
/// with no file yet returns <see cref="KnownGoodDatabase.Empty"/> rather than
/// throwing: on the day a new RimWorld version ships, an absent list is the truthful
/// answer, not a failure.
/// </summary>
public sealed class NoVersionWarningClient(IHttpFetcher fetcher)
{
    /// <summary>The upstream repository's raw root. A custom source overrides the BASE,
    /// not the whole URL, because the path under it is per game version.</summary>
    public const string DefaultBaseUrl =
        "https://raw.githubusercontent.com/emipa606/NoVersionWarning/main";

    private readonly IHttpFetcher _fetcher = fetcher;

    public static string UrlFor(string gameMajorMinor, string? baseUrl = null) =>
        $"{(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/'))}"
        + $"/{gameMajorMinor}/ModIdsToFix.xml";

    public async Task<KnownGoodDatabase> FetchAsync(
        string gameMajorMinor, string? baseUrl = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameMajorMinor);

        try
        {
            var xml = await _fetcher.GetStringAsync(UrlFor(gameMajorMinor, baseUrl), ct)
                .ConfigureAwait(false);
            return NoVersionWarningParser.Parse(xml);
        }
        catch (HttpFetchException ex) when (ex.StatusCode == 404)
        {
            return KnownGoodDatabase.Empty;
        }
    }
}
