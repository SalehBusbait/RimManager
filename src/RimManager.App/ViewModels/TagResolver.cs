using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// Avalonia-free tag helpers: resolve a mod's tag ids to <see cref="Tag"/>s (order-
/// preserving, tolerant of ids whose definition was deleted) and pick a colour for a
/// new tag. Kept out of the view-model so it's unit-tested.
/// </summary>
public static class TagResolver
{
    /// <summary>
    /// The palette index for the next new tag, cycling the six hues so a fresh list
    /// does not come back all one colour.
    /// <para>
    /// This returns an <em>index</em>, never a hex: colours persist as a palette index
    /// so they resolve through the active theme dictionary and stay legible in both
    /// light and dark (design non-negotiable #6).
    /// </para>
    /// </summary>
    public static int NextPaletteIndex(int existingCount) => Palette.Normalize(existingCount);

    /// <summary>The tags assigned to a mod, in assignment order; ids with no definition are skipped.</summary>
    public static ImmutableArray<Tag> Resolve(TagSet tags, ImmutableArray<string> tagIds)
    {
        if (tagIds.IsDefaultOrEmpty) return [];

        var byId = tags.Tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<Tag>();
        foreach (var id in tagIds)
        {
            if (byId.TryGetValue(id, out var tag)) result.Add(tag);
        }

        return result.ToImmutable();
    }

    /// <summary>The defined tags a mod does <em>not</em> have, for the "assign" affordance.</summary>
    public static ImmutableArray<Tag> Unassigned(TagSet tags, ImmutableArray<string> assignedIds)
    {
        var assigned = assignedIds.IsDefaultOrEmpty
            ? new HashSet<string>(StringComparer.Ordinal)
            : assignedIds.ToHashSet(StringComparer.Ordinal);
        return [.. tags.Tags.Where(t => !assigned.Contains(t.Id))];
    }

    /// <summary>
    /// The row's tag pills (v2 §4A.1), in manage-list order — EVERY assigned tag is
    /// represented, which is the clause this design overturned: the old 3px stripe
    /// showed exactly one tag chosen by declaration order, disambiguated only by a
    /// tooltip whose hover target was the 3px sliver itself. The per-tag
    /// <c>ShowAsStripe</c> flag keeps its meaning — "show this tag on rows" — and
    /// now gates a pill rather than the stripe contest.
    /// </summary>
    public static ImmutableArray<TagPill> PillsFor(TagSet tags, ImmutableArray<string> assignedIds)
    {
        if (assignedIds.IsDefaultOrEmpty) return [];

        var assigned = assignedIds.ToHashSet(StringComparer.Ordinal);
        return [.. tags.Tags
            .Where(t => t.ShowAsStripe && assigned.Contains(t.Id))
            .Select(t => new TagPill(t.Name, t.PaletteIndex))];
    }
}

/// <summary>One pill on a row: the tag's name and its palette identity.</summary>
public readonly record struct TagPill(string Name, int PaletteIndex);
