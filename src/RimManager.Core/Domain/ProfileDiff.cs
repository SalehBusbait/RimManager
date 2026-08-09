using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>How one mod changed between two profile states.</summary>
public sealed record ModMove(ModId Id, int FromIndex, int ToIndex);

public sealed record EnableChange(ModId Id, bool NowEnabled);

/// <summary>
/// The difference between two arrangements (spec §4.2, History's diff:
/// added / removed / reordered / enable-changed). Version-changed is a scan-time
/// concern (profile state does not store mod versions) and is layered on elsewhere.
/// </summary>
public sealed record ProfileDiff(
    ImmutableArray<ModId> Added,
    ImmutableArray<ModId> Removed,
    ImmutableArray<ModMove> Moved,
    ImmutableArray<EnableChange> EnableChanged)
{
    public bool IsIdentical =>
        Added.IsDefaultOrEmpty && Removed.IsDefaultOrEmpty
        && Moved.IsDefaultOrEmpty && EnableChanged.IsDefaultOrEmpty;

    /// <summary>
    /// The same diff between two modlist arrangements — what History compares now that
    /// snapshots belong to a modlist rather than to an instance's profile.
    /// </summary>
    public static ProfileDiff Between(ModlistState from, ModlistState to) =>
        Between(
            from.Entries
                .Where(e => e.Kind == ModlistEntryKind.Mod)
                .Select(e => (e.Id, e.Enabled)),
            to.Entries
                .Where(e => e.Kind == ModlistEntryKind.Mod)
                .Select(e => (e.Id, e.Enabled)));

    /// <summary>
    /// The comparison itself, over the only two things it has ever needed: an ordered
    /// sequence of mod ids and their enabled state. Kept shared rather than duplicated
    /// per state type — two copies of this would be two places for "moved" to mean
    /// something slightly different.
    /// </summary>
    private static ProfileDiff Between(
        IEnumerable<(string Id, bool Enabled)> fromEntries,
        IEnumerable<(string Id, bool Enabled)> toEntries)
    {
        var fromIndex = fromEntries.Select((e, i) => (e, i))
            .ToDictionary(x => ModId.From(x.e.Id), x => (x.i, x.e.Enabled));
        var toIndex = toEntries.Select((e, i) => (e, i))
            .ToDictionary(x => ModId.From(x.e.Id), x => (x.i, x.e.Enabled));

        var added = ImmutableArray.CreateBuilder<ModId>();
        var removed = ImmutableArray.CreateBuilder<ModId>();
        var moved = ImmutableArray.CreateBuilder<ModMove>();
        var enableChanged = ImmutableArray.CreateBuilder<EnableChange>();

        foreach (var (id, (toPos, toEnabled)) in toIndex.OrderBy(kv => kv.Value.Item1))
        {
            if (!fromIndex.TryGetValue(id, out var fromState))
            {
                added.Add(id);
                continue;
            }

            if (fromState.Item1 != toPos) moved.Add(new ModMove(id, fromState.Item1, toPos));
            if (fromState.Item2 != toEnabled) enableChanged.Add(new EnableChange(id, toEnabled));
        }

        foreach (var (id, _) in fromIndex.OrderBy(kv => kv.Value.Item1))
        {
            if (!toIndex.ContainsKey(id)) removed.Add(id);
        }

        return new ProfileDiff(added.ToImmutable(), removed.ToImmutable(), moved.ToImmutable(), enableChanged.ToImmutable());
    }
}
