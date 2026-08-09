using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>
/// The second sort algorithm offered in Settings ▸ Sorting &amp; rules and the
/// Sort ▾ flyout (<c>2g</c>, <c>3f</c>): <b>Alphabetical within separators</b> —
/// "Ignores rules entirely. Useful for library-style lists where you group by hand."
/// <para>
/// It operates on the <see cref="ModlistState"/> rather than on a flat mod list,
/// because unlike <see cref="ModSorter"/> it needs to see the separators: each
/// separator owns the contiguous run of mods below it until the next one, and only
/// the contents of a run are reordered. Separators never move, so a hand-built
/// grouping survives the sort — which is the entire point of the mode.
/// </para>
/// <para>
/// Deterministic and idempotent, same contract as <see cref="ModSorter"/>: ties
/// break on packageId, so <c>sort(sort(x)) == sort(x)</c>.
/// </para>
/// </summary>
public static class AlphabeticalSorter
{
    /// <summary>
    /// Sorts the mods inside each separator-owned run by display name.
    /// </summary>
    /// <param name="displayNames">
    /// packageId → display name. A mod with no entry falls back to its
    /// <see cref="ModlistEntry.DisplayName"/>, so an unscanned or missing mod still
    /// sorts somewhere sensible instead of being dropped.
    /// </param>
    public static ModlistState SortWithinSeparators(
        ModlistState state, IReadOnlyDictionary<ModId, string>? displayNames = null)
    {
        var result = ImmutableList.CreateBuilder<ModlistEntry>();
        var run = new List<ModlistEntry>();

        foreach (var entry in state.Entries)
        {
            if (entry.Kind == ModlistEntryKind.Separator)
            {
                // The run ends at the next separator; flush it, then keep the
                // separator exactly where it was.
                Flush(result, run, displayNames);
                result.Add(entry);
                continue;
            }

            run.Add(entry);
        }

        Flush(result, run, displayNames);
        return state with { Entries = result.ToImmutable() };
    }

    /// <summary>
    /// Emits one run in name order. Mods above the first separator form their own
    /// run, so an ungrouped list still sorts.
    /// </summary>
    private static void Flush(
        ImmutableList<ModlistEntry>.Builder result,
        List<ModlistEntry> run,
        IReadOnlyDictionary<ModId, string>? displayNames)
    {
        if (run.Count == 0) return;

        var ordered = run
            .OrderBy(e => NameOf(e, displayNames), StringComparer.OrdinalIgnoreCase)
            // packageId breaks ties, which is what makes the result idempotent —
            // two mods sharing a display name would otherwise swap on every sort.
            .ThenBy(e => e.Id, StringComparer.Ordinal);

        foreach (var entry in ordered) result.Add(entry);
        run.Clear();
    }

    private static string NameOf(ModlistEntry entry, IReadOnlyDictionary<ModId, string>? displayNames)
    {
        if (displayNames is not null
            && ModId.TryFrom(entry.Id, out var id)
            && displayNames.TryGetValue(id, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return entry.DisplayName;
    }
}
