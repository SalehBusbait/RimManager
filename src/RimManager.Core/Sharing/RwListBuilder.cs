using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sharing;

/// <summary>The header info a caller supplies when building a list.</summary>
public sealed record RwListInfo(
    string? Name = null,
    string? Author = null,
    string? Description = null,
    string? GameVersion = null,
    IEnumerable<ModId>? RequiredDlc = null)
{
    public DateTimeOffset CreatedUtc { get; init; }
}

/// <summary>Builds a full-fidelity <see cref="RwList"/> from a profile arrangement + metadata.</summary>
public static class RwListBuilder
{
    /// <summary>
    /// Builds from a modlist arrangement, falling back to each entry's own identity when
    /// the mod is not installed.
    /// <para>
    /// The retired ProfileState overload had a real bug this one exists to fix: it read
    /// source and Workshop id off the SCAN, so a list naming a
    /// mod the user has since uninstalled exported as <c>Source = Workshop</c> with a null
    /// <c>PublishedFileId</c> — silently mislabelled, and uninstallable by whoever received
    /// it. A <see cref="ModlistEntry"/> carries that identity itself, so the projection has
    /// something true to fall back on.
    /// </para>
    /// </summary>
    /// <param name="updatedById">
    /// Workshop update times at export, for <see cref="RwEntry.TimeUpdatedUtc"/>. Optional:
    /// they come from an update check, which needs the network and may never have run.
    /// </param>
    public static RwList Build(
        ModlistState state,
        IReadOnlyDictionary<ModId, Mod> byId,
        IReadOnlyDictionary<ModId, ModMetadata> metadataById,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<Category> categories,
        RwListInfo info,
        Sorting.EdgeSuppressions? suppressions = null,
        IReadOnlyDictionary<ModId, DateTimeOffset>? updatedById = null)
    {
        var entries = ImmutableArray.CreateBuilder<RwEntry>();

        foreach (var entry in state.Entries)
        {
            if (entry.Kind == ModlistEntryKind.Separator)
            {
                entries.Add(RwEntry.Separator(
                    entry.Id, entry.DisplayName, entry.PaletteIndex, entry.Collapsed));
                continue;
            }

            var id = ModId.From(entry.Id);
            byId.TryGetValue(id, out var mod);
            var meta = metadataById.GetValueOrDefault(id, ModMetadata.Empty);

            entries.Add(new RwEntry
            {
                Type = RwEntryKind.Mod,
                PackageId = id.Value,
                DisplayName = mod?.Name ?? entry.DisplayName,

                // The installed copy wins where it exists — it is current — but the entry
                // is what keeps an uninstalled mod identifiable.
                Source = MapSource(mod?.Source ?? entry.Source ?? ModSource.Workshop),
                PublishedFileId = mod?.PublishedFileId ?? entry.PublishedFileId,
                GitUrl = entry.GitUrl,
                GitRef = entry.GitRef,
                ModVersion = mod?.ModVersion ?? entry.ModVersion,
                TimeUpdatedUtc = updatedById?.GetValueOrDefault(id) is { } t && t != default
                    ? t
                    : null,

                TagIds = meta.TagIds,
                CategoryId = meta.CategoryId,
                Note = meta.Note,
                Alias = meta.Alias,
                ColorOverride = meta.ColorOverride,
                Favorite = meta.Favorite,
                IgnoreUpdates = meta.IgnoreUpdates,
            });
        }

        return new RwList
        {
            Name = info.Name,
            Author = info.Author,
            Description = info.Description,
            CreatedUtc = info.CreatedUtc,
            GameVersion = info.GameVersion,
            RequiredDlc = info.RequiredDlc is null ? [] : [.. info.RequiredDlc.Select(d => d.Value)],
            Tags = [.. tags.Select(t => new RwTag(t.Id, t.Name, Palette.ReferenceHex(t.PaletteIndex), t.PaletteIndex))],
            Categories = [.. categories.Select(c => new RwCategory(c.Id, c.Name, c.ParentId))],
            Entries = entries.ToImmutable(),
            DroppedEdges = suppressions is null || suppressions.IsEmpty
                ? []
                : [.. suppressions.Edges.Select(e => new RwRule(e.Before.Value, e.After.Value))],
        };
    }


    // Nothing maps to RwSource.Pinned any more: the vault that produced it is gone
    // (O13). The FORMAT keeps the value — a .rwlist is read by other people's tools and
    // by files exported before the removal — so this stops writing it while
    // RwListWorkshopImport.Map goes on accepting it.
    private static RwSource MapSource(ModSource source) => source switch
    {
        ModSource.Local => RwSource.Local,
        ModSource.Git => RwSource.Git,
        ModSource.Core or ModSource.Dlc => RwSource.Dlc,
        _ => RwSource.Workshop,
    };
}
