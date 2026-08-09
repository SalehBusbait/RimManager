using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// A mod as discovered on disk: parsed <c>About.xml</c> metadata + detected
/// content + provenance. This is the scanner's output and the unit the sorter,
/// validators, and UI all consume. Distinct from <see cref="ModlistEntry"/>,
/// which is about list <em>organization</em>.
/// </summary>
public sealed record Mod
{
    public required ModId PackageId { get; init; }

    public required string Name { get; init; }

    public ImmutableArray<string> Authors { get; init; } = [];

    public string? Description { get; init; }

    /// <summary>Game versions the mod declares support for, e.g. <c>["1.5","1.6"]</c>.</summary>
    public ImmutableArray<string> SupportedVersions { get; init; } = [];

    public string? ModVersion { get; init; }

    public ImmutableArray<ModDependency> Dependencies { get; init; } = [];

    public ImmutableArray<ModId> LoadAfter { get; init; } = [];
    public ImmutableArray<ModId> LoadBefore { get; init; } = [];
    public ImmutableArray<ModId> ForceLoadAfter { get; init; } = [];
    public ImmutableArray<ModId> ForceLoadBefore { get; init; } = [];
    public ImmutableArray<ModId> IncompatibleWith { get; init; } = [];

    public required ModSource Source { get; init; }

    /// <summary>Absolute path of the mod's root folder.</summary>
    public required string RootPath { get; init; }

    /// <summary>Steam Workshop id from <c>PublishedFileId.txt</c>, if present.</summary>
    public string? PublishedFileId { get; init; }

    public ContentFlags Content { get; init; }

    public ImmutableArray<ModWarning> Warnings { get; init; } = [];

    // --- cache key: stat of the About.xml this mod was parsed from -----------
    public DateTimeOffset AboutLastWriteUtc { get; init; }
    public long AboutSize { get; init; }

    // --- derived classifications (spec §4.3 content flags) -------------------

    public bool HasAssemblies => Content.HasFlag(ContentFlags.Assemblies);

    public bool IsXmlOnly =>
        !HasAssemblies && (Content.HasFlag(ContentFlags.Defs) || Content.HasFlag(ContentFlags.Patches));

    public bool IsTextureOnly =>
        Content.HasFlag(ContentFlags.Textures)
        && !HasAssemblies
        && !Content.HasFlag(ContentFlags.Defs)
        && !Content.HasFlag(ContentFlags.Patches);

    public bool IsTranslationOnly =>
        Content.HasFlag(ContentFlags.Languages)
        && (Content & ~(ContentFlags.Languages | ContentFlags.Sources)) == ContentFlags.None;

    /// <summary>
    /// A Workshop item whose payload is a mod list, not a mod (NF-10): carries a
    /// <c>.rwlist</c> and none of the five content-bearing folders. <b>Content wins
    /// over payload</b> — a folder with Defs AND a .rwlist is a mod that happens to
    /// bundle a list, and treating it as a list would hide real content from the
    /// order. Languages/Sources do not disqualify; neither loads into the game.
    /// </summary>
    public bool IsRwListItem =>
        Content.HasFlag(ContentFlags.RwList)
        && (Content & (ContentFlags.Defs | ContentFlags.Patches | ContentFlags.Assemblies
                       | ContentFlags.Textures | ContentFlags.Sounds)) == ContentFlags.None;

    public bool HasErrors => !Warnings.IsDefaultOrEmpty && Warnings.Any(w => w.Severity == WarningSeverity.Error);
}
