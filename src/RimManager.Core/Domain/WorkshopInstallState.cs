using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>One installed Workshop item as Steam records it in <c>appworkshop_294100.acf</c>.</summary>
public sealed record InstalledWorkshopItem
{
    public required string PublishedFileId { get; init; }

    /// <summary>Steam's publish time of the <em>installed</em> version — the apples-to-apples
    /// counterpart to <see cref="WorkshopItem.TimeUpdatedUtc"/> for update detection.</summary>
    public DateTimeOffset? TimeUpdatedUtc { get; init; }

    public long? SizeOnDisk { get; init; }

    /// <summary>Steam content manifest id of the installed version, if present.</summary>
    public string? ManifestId { get; init; }
}

/// <summary>
/// The locally-installed Workshop state parsed from Steam's <c>appworkshop_294100.acf</c>
/// manifest — the source of truth for "which version of each item is on disk right now".
/// Update-checking compares each entry's <see cref="InstalledWorkshopItem.TimeUpdatedUtc"/>
/// against the live Web API value; an item absent here is simply not Steam-tracked.
/// </summary>
public sealed record WorkshopInstallState
{
    public static readonly WorkshopInstallState Empty = new() { Items = ImmutableDictionary<string, InstalledWorkshopItem>.Empty };

    /// <summary>Installed items keyed by published-file id (ordinal string key, matching Steam).</summary>
    public required ImmutableDictionary<string, InstalledWorkshopItem> Items { get; init; }

    public InstalledWorkshopItem? TryGet(string publishedFileId) =>
        Items.TryGetValue(publishedFileId, out var item) ? item : null;
}
