using System.Collections.Immutable;
using System.Globalization;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;

namespace RimManager.Core.Workshop;

/// <summary>
/// Parses Steam's <c>appworkshop_294100.acf</c> (a Valve KeyValues file) into a
/// <see cref="WorkshopInstallState"/>. Pure — it takes the file's text and reuses
/// <see cref="VdfParser"/>, so it's testable without touching a real Steam library.
/// </summary>
/// <remarks>
/// Shape: <c>AppWorkshop → WorkshopItemsInstalled → &lt;publishedfileid&gt; →
/// { size, timeupdated, manifest }</c>. <c>timeupdated</c> is the installed version's
/// Steam publish time (unix seconds) — the value update-checking compares against the
/// live <c>GetPublishedFileDetails.time_updated</c>. Tolerant of a missing file
/// (empty text → empty state) and of absent fields.
/// </remarks>
public static class WorkshopManifestParser
{
    public static WorkshopInstallState Parse(string acfText)
    {
        if (string.IsNullOrWhiteSpace(acfText)) return WorkshopInstallState.Empty;

        var root = VdfParser.Parse(acfText);
        var installed = root["AppWorkshop"]?["WorkshopItemsInstalled"];
        if (installed is null) return WorkshopInstallState.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, InstalledWorkshopItem>(StringComparer.Ordinal);
        foreach (var (id, node) in installed.Children)
        {
            if (node.IsLeaf || string.IsNullOrWhiteSpace(id)) continue;
            builder[id] = new InstalledWorkshopItem
            {
                PublishedFileId = id,
                TimeUpdatedUtc = ReadUnixTime(node["timeupdated"]?.Value),
                SizeOnDisk = ReadLong(node["size"]?.Value),
                ManifestId = node["manifest"]?.Value,
            };
        }

        return new WorkshopInstallState { Items = builder.ToImmutable() };
    }

    private static DateTimeOffset? ReadUnixTime(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static long? ReadLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
}
