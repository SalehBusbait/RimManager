using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>The property an auto-assign condition tests (`2g` Tags &amp; metadata).</summary>
public enum TagConditionKind
{
    AuthorContains,
    NameContains,
    PackageIdContains,
    SourceIs,

    /// <summary>Folder size in megabytes, e.g. <c>size &gt; 100 MB</c>.</summary>
    SizeOverMb,
}

/// <summary>
/// One auto-assign condition, applied to <em>newly scanned</em> mods only — never
/// retroactively, so it can never silently re-tag a list the user has curated.
/// </summary>
public sealed record TagCondition(TagConditionKind Kind, string Value);

/// <summary>
/// A user-defined tag (spec §4.3), many-to-many with mods.
/// <para>
/// <see cref="PaletteIndex"/> is an index into <see cref="Palette"/>, never a hex
/// string (design non-negotiable #6) — that is what makes a user's colours flip
/// correctly between light and dark.
/// </para>
/// </summary>
public sealed record Tag
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Index into <see cref="Palette"/>. Persisted; resolved to a brush at paint time.</summary>
    public int PaletteIndex { get; init; }

    /// <summary>Whether members show this tag's 3px stripe in the mod lists (`1e` §4).</summary>
    public bool ShowAsStripe { get; init; } = true;

    /// <summary>Whether auto-layout may create a separator for this tag's members.</summary>
    public bool CreatesSeparator { get; init; }


    /// <summary>Whether the tag travels with an exported <c>.rwlist</c>.</summary>
    public bool IncludeInExport { get; init; }

    /// <summary>Conditions that auto-apply this tag to newly scanned mods.</summary>
    public ImmutableArray<TagCondition> AutoAssign { get; init; } = [];
}

/// <summary>
/// A node in the single-assignment category tree (spec §4.3). <see cref="ParentId"/>
/// null means a root category.
/// </summary>
public sealed record Category(string Id, string Name, string? ParentId = null);

/// <summary>
/// Per-mod, identity-scoped metadata (spec §4.3): notes, favorite, ignore-updates,
/// a color override, a display alias, tag assignments, and a category. Keyed by
/// packageId at the instance level, so tagging "Harmony" applies wherever it
/// appears — independent of any single profile's arrangement.
/// </summary>
public sealed record ModMetadata
{
    /// <summary>Custom display name — many RimWorld mods have unhelpful names (spec §4.3).</summary>
    public string? Alias { get; init; }

    public string? Note { get; init; }
    public bool Favorite { get; init; }
    public bool IgnoreUpdates { get; init; }
    public string? ColorOverride { get; init; }

    public ImmutableArray<string> TagIds { get; init; } = [];
    public string? CategoryId { get; init; }

    public static readonly ModMetadata Empty = new();

    public bool IsEmpty =>
        Alias is null && Note is null && !Favorite && !IgnoreUpdates
        && ColorOverride is null && TagIds.IsDefaultOrEmpty && CategoryId is null;
}

// --- persisted aggregates (one file each) -----------------------------------

public sealed record TagSet(ImmutableArray<Tag> Tags)
{
    public static readonly TagSet Empty = new([]);
}

public sealed record CategorySet(ImmutableArray<Category> Categories)
{
    public static readonly CategorySet Empty = new([]);
}

public sealed record ModMetadataSet(ImmutableDictionary<string, ModMetadata> Entries)
{
    public static readonly ModMetadataSet Empty = new(ImmutableDictionary<string, ModMetadata>.Empty);
}
