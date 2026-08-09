using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Sorting;

namespace RimManager.App.ViewModels;

/// <summary>One presented cycle: the loop and the edge the sorter removed to break it.</summary>
public sealed record CycleRow(string Cycle, string Broken, string Source);

/// <summary>
/// Avalonia-free presentation of a sort's cycles: turns <see cref="SortResult.BrokenEdges"/>
/// into display rows (the loop + which edge was dropped, deterministically) and a summary.
/// The sorter already breaks cycles; this surfaces <em>what</em> it broke so the user can
/// understand — and fix the offending rule if they disagree.
/// </summary>
public static class CyclesPresenter
{
    public static ImmutableArray<CycleRow> BuildRows(SortResult result)
    {
        if (result.BrokenEdges.IsDefaultOrEmpty) return [];

        var rows = ImmutableArray.CreateBuilder<CycleRow>(result.BrokenEdges.Length);
        foreach (var broken in result.BrokenEdges)
        {
            var loop = string.Join(" → ", broken.Cycle.Select(m => m.Display));
            if (broken.Cycle.Length > 0) loop += $" → {broken.Cycle[0].Display}";

            rows.Add(new CycleRow(
                loop,
                $"{broken.Edge.Before.Display} → {broken.Edge.After.Display}",
                broken.Edge.Provenance.Source.ToString()));
        }

        return rows.ToImmutable();
    }

    public static string Summarize(SortResult result)
    {
        var cycles = result.Cycles.IsDefaultOrEmpty ? 0 : result.Cycles.Length;
        if (cycles == 0) return "No cycles — the load order is fully consistent.";

        var broke = result.BrokenEdges.IsDefaultOrEmpty ? 0 : result.BrokenEdges.Length;
        return $"{cycles} cycle{(cycles == 1 ? "" : "s")} detected · {broke} edge{(broke == 1 ? "" : "s")} broken.";
    }
}
