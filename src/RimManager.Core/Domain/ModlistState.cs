using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>One row of a modlist: a mod, or a separator header.</summary>
public enum ModlistEntryKind
{
    Mod,
    Separator,
}

/// <summary>
/// One entry in a modlist's arrangement.
/// <para>
/// Unlike the <c>ProfileEntry</c> it replaced (retired in N11), a mod entry carries its own <b>identity</b>
/// — source, Workshop id, git origin, version — and not just its packageId. That is what
/// lets a modlist describe a mod the user does not currently have installed, which is the
/// whole of "switch to my friend's list and be told what is missing".
/// </para>
/// <para>
/// It also fixes a live export bug. <c>RwListBuilder</c> reads these fields off the
/// <em>scan</em>, so a list referencing a since-removed mod exported as
/// <c>Source = Workshop</c> with a null <c>PublishedFileId</c> — silently mislabelled, and
/// uninstallable by whoever received it. With identity on the entry the projection has a
/// fallback instead of a default.
/// </para>
/// </summary>
/// <param name="Id">
/// The lowercased packageId for a mod, or a generated id for a separator. Mod ids always
/// route through <see cref="ModId"/>; never compare these raw.
/// </param>
/// <param name="PaletteIndex">
/// A separator's colour bar as an index into <see cref="Palette"/> — never a hex
/// (non-negotiable #6), so it flips with the theme.
/// </param>
public sealed record ModlistEntry(
    ModlistEntryKind Kind,
    string Id,
    string DisplayName,
    bool Enabled = true,
    bool Collapsed = false,
    int? PaletteIndex = null,
    ModSource? Source = null,
    string? PublishedFileId = null,
    string? GitUrl = null,
    string? GitRef = null,
    string? ModVersion = null)
{
    public static ModlistEntry Mod(ModId id, bool enabled = true) =>
        new(ModlistEntryKind.Mod, id.Value, id.Display, Enabled: enabled);

    /// <summary>A mod entry that remembers where it came from, so an export stays correct
    /// after the mod is uninstalled.</summary>
    public static ModlistEntry Mod(Domain.Mod mod, bool enabled = true) =>
        new(ModlistEntryKind.Mod,
            mod.PackageId.Value,
            mod.Name,
            Enabled: enabled,
            Source: mod.Source,
            PublishedFileId: mod.PublishedFileId,
            ModVersion: mod.ModVersion);

    public static ModlistEntry Separator(
        string id, string name, int? paletteIndex = null, bool collapsed = false) =>
        new(ModlistEntryKind.Separator, id, name, Collapsed: collapsed, PaletteIndex: paletteIndex);
}

/// <summary>
/// A modlist's arrangement as an immutable value: ordered entries, mods and separators
/// interleaved, each mod carrying its enabled state. Every user action produces a
/// <em>new</em> state; undo/redo is pointer movement over a history of these, and the
/// snapshot history persists the same shape.
/// </summary>
public sealed record ModlistState(ImmutableList<ModlistEntry> Entries)
{
    public static ModlistState Empty { get; } = new(ImmutableList<ModlistEntry>.Empty);

    public ModlistState WithEntries(IEnumerable<ModlistEntry> entries) =>
        this with { Entries = entries.ToImmutableList() };

    /// <summary>The active mod ids in load order (enabled mods only), skipping separators.</summary>
    public IEnumerable<ModId> ActiveModIds() =>
        Entries.Where(e => e.Kind == ModlistEntryKind.Mod && e.Enabled).Select(e => ModId.From(e.Id));

    /// <summary>All mod ids in order regardless of enabled state.</summary>
    public IEnumerable<ModId> AllModIds() =>
        Entries.Where(e => e.Kind == ModlistEntryKind.Mod).Select(e => ModId.From(e.Id));

    /// <summary>
    /// Mods this list names that are not in <paramref name="installed"/>. The question a
    /// modlist switch has to answer before it changes anything.
    /// </summary>
    public IEnumerable<ModlistEntry> MissingFrom(IReadOnlyDictionary<ModId, Mod> installed) =>
        Entries.Where(e => e.Kind == ModlistEntryKind.Mod && !installed.ContainsKey(ModId.From(e.Id)));
}
