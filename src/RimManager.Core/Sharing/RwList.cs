using System.Collections.Immutable;

namespace RimManager.Core.Sharing;

public enum RwSource { Workshop, Local, Git, Dlc, Pinned }

public enum RwEntryKind { Mod, Separator }

/// <summary>
/// A tag definition (carries its colour so export isn't lossy).
/// <para>
/// <see cref="PaletteIndex"/> is authoritative for RimManager — colours persist as a
/// palette index so they flip with the theme (non-negotiable #6). <see cref="Color"/>
/// is an <em>advisory</em> hex written alongside it, because a <c>.rwlist</c> is read
/// by other people's tools and a bare index means nothing to them. On import the
/// index wins; a file that carries only a hex is mapped to the nearest hue.
/// </para>
/// </summary>
public sealed record RwTag(string Id, string Name, string? Color = null, int? PaletteIndex = null);

/// <summary>A category-tree node.</summary>
public sealed record RwCategory(string Id, string Name, string? ParentId = null);

/// <summary>A user load-order edge needed to reproduce the list's order (<c>Before</c> loads first).</summary>
public sealed record RwRule(string Before, string After);

/// <summary>
/// One ordered entry: a mod or a separator (grouping is positional — a mod belongs
/// to the nearest separator above it; there is no back-reference).
/// </summary>
public sealed record RwEntry
{
    public required RwEntryKind Type { get; init; }

    // --- separator ---
    public string? Id { get; init; }
    public string? Name { get; init; }

    /// <summary>Advisory interchange hex; <see cref="PaletteIndex"/> is authoritative.</summary>
    public string? Color { get; init; }

    /// <summary>Index into <see cref="RimManager.Core.Domain.Palette"/>.</summary>
    public int? PaletteIndex { get; init; }

    public bool Collapsed { get; init; }

    // --- mod ---
    public string? PackageId { get; init; }
    public string? DisplayName { get; init; }
    public RwSource Source { get; init; }
    public string? PublishedFileId { get; init; }
    public string? GitUrl { get; init; }
    public string? GitRef { get; init; }
    public string? ModVersion { get; init; }

    /// <summary>
    /// When the Workshop last updated this mod, at the moment the list was exported —
    /// "validated against these exact versions".
    /// <para>
    /// Steam publishes an update <em>time</em> and never a version number, so this is the
    /// only fact that lets a recipient tell whether a mod has moved since the list was
    /// proven to work. It is why the Updates tab's LATEST column shows a dash.
    /// </para>
    /// </summary>
    public DateTimeOffset? TimeUpdatedUtc { get; init; }

    public bool Pinned { get; init; }
    public ImmutableArray<string> TagIds { get; init; } = [];
    public string? CategoryId { get; init; }
    public string? Note { get; init; }
    public string? Alias { get; init; }
    public string? ColorOverride { get; init; }
    public bool Favorite { get; init; }
    public bool IgnoreUpdates { get; init; }

    public static RwEntry Separator(
        string id, string name, int? paletteIndex = null, bool collapsed = false) =>
        new()
        {
            Type = RwEntryKind.Separator,
            Id = id,
            Name = name,
            PaletteIndex = paletteIndex,
            Color = paletteIndex is { } i ? RimManager.Core.Domain.Palette.ReferenceHex(i) : null,
            Collapsed = collapsed,
        };

    public static RwEntry Mod(string packageId, string displayName, RwSource source) =>
        new() { Type = RwEntryKind.Mod, PackageId = packageId, DisplayName = displayName, Source = source };
}

/// <summary>A parsed/constructed <c>.rwlist</c> manifest (schema v1). See docs/rwlist-v1.md.</summary>
public sealed record RwList
{
    public int SchemaVersion { get; init; } = 1;
    public string? Name { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public string? GameVersion { get; init; }
    public ImmutableArray<string> RequiredDlc { get; init; } = [];
    public ImmutableArray<RwTag> Tags { get; init; } = [];
    public ImmutableArray<RwCategory> Categories { get; init; } = [];
    public ImmutableArray<RwEntry> Entries { get; init; } = [];
    public ImmutableArray<RwRule> UserRules { get; init; } = [];

    /// <summary>
    /// Ordering edges the curator chose to drop when resolving a cycle.
    /// <para>
    /// Narrower value than it first appears, and worth stating honestly: on import the
    /// <see cref="Entries"/> array <em>is</em> the order, reproduced verbatim, so these
    /// change nothing about how the list first loads. They matter only when the recipient
    /// later re-sorts — after adding mods of their own — and stop that sort re-litigating
    /// cycles the curator already settled.
    /// </para>
    /// </summary>
    public ImmutableArray<RwRule> DroppedEdges { get; init; } = [];

    /// <summary>The Workshop collection this list was built from, when it came from one.</summary>
    public string? SourceCollectionUrl { get; init; }

    /// <summary>
    /// What the curator knows is wrong with it — "CE and Rimefeller conflict; load CE
    /// after". Turns a warning the recipient would hit anyway into curation.
    /// </summary>
    public ImmutableArray<string> KnownIssues { get; init; } = [];

    public string? Checksum { get; init; }

    public IEnumerable<RwEntry> Mods => Entries.Where(e => e.Type == RwEntryKind.Mod);
}
