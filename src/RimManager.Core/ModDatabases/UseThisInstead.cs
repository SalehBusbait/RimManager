using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;

namespace RimManager.Core.ModDatabases;

/// <summary>
/// One rule from Mlie's UseThisInstead database: an outdated mod and its maintained
/// replacement. Ids are Workshop file ids as strings (they always exist upstream);
/// packageIds are optional and <b>not trustworthy alone</b> — measured on the live
/// database, 374 of 2648 rules keep the same packageId across old and new, because a
/// continuation keeps the id and changes only the Workshop listing.
/// </summary>
public sealed record ModReplacement(
    string OldWorkshopId,
    ModId? OldPackageId,
    string OldName,
    string NewWorkshopId,
    ModId? NewPackageId,
    string NewName,
    string NewAuthor,
    ImmutableArray<string> NewVersions);

/// <summary>A fetched replacements database: the rules, the upstream's own version
/// stamp, and the decompressed JSON so the caller can cache it verbatim.</summary>
public sealed record ReplacementDatabase(
    ImmutableArray<ModReplacement> Replacements,
    DateTimeOffset? PublishedUtc,
    string RawJson)
{
    public static readonly ReplacementDatabase Empty = new([], null, string.Empty);

    public int Count => Replacements.Length;
}

/// <summary>
/// Parses Mlie's <c>replacements.json</c>. Defensive where the live data was measured
/// to need it: the payload carries a UTF-8 BOM inside the gzip, and 4 of 2648 rules
/// hold a null or numeric packageId — a malformed rule degrades, never throws.
/// </summary>
public static class UseThisInsteadParser
{
    /// <summary>Gunzips the fetched payload and strips the BOM the upstream file carries.</summary>
    public static string Decompress(byte[] gzipped)
    {
        ArgumentNullException.ThrowIfNull(gzipped);

        using var input = new MemoryStream(gzipped);
        using var gunzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gunzip.CopyTo(output);

        var bytes = output.ToArray();
        var offset = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    public static ReplacementDatabase Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ReplacementDatabase.Empty;

            var rules = ImmutableArray.CreateBuilder<ModReplacement>();
            if (doc.RootElement.TryGetProperty("rules", out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in array.EnumerateArray())
                {
                    if (ParseRule(rule) is { } parsed) rules.Add(parsed);
                }
            }

            return new ReplacementDatabase(rules.ToImmutable(), ExtractVersion(doc), json);
        }
        catch (JsonException)
        {
            return ReplacementDatabase.Empty;
        }
    }

    private static ModReplacement? ParseRule(JsonElement rule)
    {
        if (rule.ValueKind != JsonValueKind.Object) return null;

        // The Workshop id is the primary key and always present upstream; a rule
        // without one cannot be matched against anything and is dropped.
        var oldId = Str(rule, "oldWorkshopId");
        var newId = Str(rule, "newWorkshopId");
        if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId)) return null;

        var versions = ImmutableArray.CreateBuilder<string>();
        if (rule.TryGetProperty("newVersions", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in array.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                    versions.Add(s);
            }
        }

        return new ModReplacement(
            oldId,
            Id(rule, "oldPackageId"),
            Str(rule, "oldName") ?? string.Empty,
            newId,
            Id(rule, "newPackageId"),
            Str(rule, "newName") ?? string.Empty,
            Str(rule, "newAuthor") ?? string.Empty,
            versions.ToImmutable());
    }

    /// <summary>A string property, or null when absent or (measured upstream) not a string.</summary>
    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ModId? Id(JsonElement obj, string name) =>
        Str(obj, name) is { Length: > 0 } value ? ModId.From(value) : null;

    private static DateTimeOffset? ExtractVersion(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("version", out var v)
        && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), out var stamp)
            ? stamp
            : null;
}

/// <summary>
/// Fetches Mlie's UseThisInstead replacements database — the same shape as
/// <see cref="Rules.CommunityRulesClient"/>: one public raw URL, no key, pure
/// orchestration over the GET seam.
/// </summary>
public sealed class UseThisInsteadClient(IHttpFetcher fetcher)
{
    /// <summary>The upstream: ~180 KB gzipped, ~2,650 rules, updated near-daily.</summary>
    public const string DefaultUrl =
        "https://raw.githubusercontent.com/emipa606/UseThisInstead/main/replacements.json.gz";

    private readonly IHttpFetcher _fetcher = fetcher;

    public async Task<ReplacementDatabase> FetchAsync(string? url = null, CancellationToken ct = default)
    {
        var gzipped = await _fetcher.GetBytesAsync(url ?? DefaultUrl, ct).ConfigureAwait(false);
        return UseThisInsteadParser.Parse(UseThisInsteadParser.Decompress(gzipped));
    }
}

/// <summary>
/// Decides whether a replacement exists for an installed mod — the semantics the
/// intrinsic validation check (N7) and the CLI share.
/// </summary>
public static class ReplacementMatcher
{
    /// <summary>
    /// The replacement for <paramref name="mod"/>, or null.
    /// <para>
    /// The Workshop id leads: a Workshop mod matches only the rule naming its own file
    /// id, which is what stops the replacement itself being flagged — 374 rules keep
    /// one packageId across old and new, so id-based matching alone would tell the
    /// user to replace the very mod the rule points to. A mod with no file id (a local
    /// copy) falls back to packageId, and only when old and new differ, because when
    /// they are equal nothing distinguishes the outdated copy from the continuation.
    /// </para>
    /// <para>
    /// <paramref name="gameMajorMinor"/> gates the offer: a replacement that does not
    /// support the running version is not actionable, and 230 of the live rules point
    /// at replacements that stop short of 1.6. Null (version unknown) gates nothing.
    /// </para>
    /// </summary>
    public static ModReplacement? For(
        Mod mod, ImmutableArray<ModReplacement> replacements, string? gameMajorMinor)
    {
        ArgumentNullException.ThrowIfNull(mod);

        foreach (var rule in replacements)
        {
            if (gameMajorMinor is not null
                && !rule.NewVersions.IsEmpty
                && !rule.NewVersions.Contains(gameMajorMinor))
            {
                continue;
            }

            if (mod.PublishedFileId is { Length: > 0 } fileId)
            {
                if (fileId == rule.OldWorkshopId) return rule;
                continue;
            }

            if (rule.OldPackageId is { } old && old == mod.PackageId
                && rule.NewPackageId is { } replacement && replacement != old)
            {
                return rule;
            }
        }

        return null;
    }
}
