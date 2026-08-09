using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// Per-item status from a Steam Workshop metadata response. Mirrors Steam's
/// <c>EResult</c> for the values RimManager actually distinguishes: an id that
/// resolved (<see cref="Ok"/>) vs one that did not (<see cref="NotFound"/> — the
/// item was deleted, hidden, or never existed). Everything else collapses to
/// <see cref="Other"/> so callers don't have to know Steam's full result table.
/// </summary>
public enum WorkshopItemResult
{
    Other = 0,

    /// <summary>Steam <c>EResult</c> 1 (k_EResultOK): the item resolved.</summary>
    Ok = 1,

    /// <summary>Steam <c>EResult</c> 9 (k_EResultFileNotFound): deleted/hidden/unknown id.</summary>
    NotFound = 9,
}

/// <summary>
/// Steam Workshop metadata for a single published file, as returned by the keyless
/// <c>ISteamRemoteStorage/GetPublishedFileDetails/v1/</c> endpoint. This is the join
/// partner for a scanned <see cref="Mod"/> via <see cref="Mod.PublishedFileId"/>:
/// it carries the remote <see cref="TimeUpdatedUtc"/> that update-checking compares
/// against, plus the human metadata (title, size, tags) and the <see cref="Children"/>
/// list that drives collection/dependency import.
/// </summary>
/// <remarks>
/// Deliberately no I/O and no HTTP knowledge — this is a plain value produced by
/// <c>WorkshopMetadataParser</c> from a response string. Steam ships some numeric
/// fields as strings (<c>file_size</c>) and times as unix seconds; the parser
/// normalizes those so this type stays clean.
/// </remarks>
public sealed record WorkshopItem
{
    /// <summary>The Workshop id (a 64-bit number, kept as a string to match Steam and <see cref="Mod.PublishedFileId"/>).</summary>
    public required string PublishedFileId { get; init; }

    /// <summary>Whether this id resolved. When not <see cref="WorkshopItemResult.Ok"/>, the other fields are unset.</summary>
    public WorkshopItemResult Result { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Owning app; <c>294100</c> for RimWorld. Lets callers reject items from another game.</summary>
    public int ConsumerAppId { get; init; }

    /// <summary>Uploader's 64-bit SteamID as a string, if present.</summary>
    public string? Creator { get; init; }

    /// <summary>Compressed size on the Workshop, in bytes; <c>null</c> if absent or unparsable.</summary>
    public long? FileSize { get; init; }

    public DateTimeOffset? TimeCreatedUtc { get; init; }

    /// <summary>Last time the author updated the item. The signal for update-checking.</summary>
    public DateTimeOffset? TimeUpdatedUtc { get; init; }

    public ImmutableArray<string> Tags { get; init; } = [];

    /// <summary>
    /// Workshop ids this item references (required items / collection members).
    /// For a collection this is the member list; for a mod, its declared required items.
    /// </summary>
    public ImmutableArray<string> Children { get; init; } = [];

    public bool IsOk => Result == WorkshopItemResult.Ok;
}
