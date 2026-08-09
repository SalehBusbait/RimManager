using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>
/// The deterministic topological sorter (spec §4.4). Priority-Kahn: among nodes
/// that are free to place, pick by <c>(tier, current position, packageId)</c>.
/// Tiering dominates (edges violating it are dropped); cycles are detected and
/// broken deterministically so a complete order is always produced.
/// </summary>
/// <remarks>
/// Determinism &amp; idempotence (constraint #6) come from the total-order
/// comparator: sorting an already-sorted list reproduces it, and sorting twice is
/// a no-op. Both are covered by property tests.
/// </remarks>
public sealed class ModSorter
{
    /// <param name="suppressions">
    /// Edges the user has chosen to drop, from the Warnings detail panel or the cycle
    /// graph ("Drop a different edge…"). They are removed before the graph is built,
    /// so the sorter then breaks whatever cycle remains — which is exactly what makes
    /// "break this one instead" work. Suppressing an edge that is not in a cycle is
    /// harmless; it just relaxes a constraint.
    /// </param>
    public SortResult Sort(
        IReadOnlyList<Mod> activeMods, RuleSet rules, EdgeSuppressions? suppressions = null)
    {
        var ids = activeMods.Select(m => m.PackageId).ToImmutableArray();
        var position = new Dictionary<ModId, int>(ids.Length);
        for (int i = 0; i < ids.Length; i++) position[ids[i]] = i;

        var tiers = ImmutableDictionary.CreateRange(activeMods.Select(m =>
            new KeyValuePair<ModId, Tier>(m.PackageId, TierAssigner.Assign(m, rules))));

        // Split rule edges three ways: suppressed by the user, dropped because they
        // violate hard tiering, and applied. User suppression is checked first — an
        // explicit choice outranks both the tier rule and the automatic cycle break.
        var applied = ImmutableArray.CreateBuilder<OrderingEdge>();
        var dropped = ImmutableArray.CreateBuilder<DroppedEdge>();
        var suppressed = ImmutableArray.CreateBuilder<DroppedEdge>();

        foreach (var edge in rules.Edges)
        {
            if (!tiers.ContainsKey(edge.Before) || !tiers.ContainsKey(edge.After)) continue;

            if (suppressions is { } s && s.Contains(edge))
            {
                suppressed.Add(new DroppedEdge(edge,
                    $"suppressed by you: {edge.Before.Display} → {edge.After.Display}"));
            }
            else if ((int)tiers[edge.Before] > (int)tiers[edge.After])
            {
                dropped.Add(new DroppedEdge(edge,
                    $"rule would load {edge.Before.Display} (tier {tiers[edge.Before]}) before " +
                    $"{edge.After.Display} (tier {tiers[edge.After]}), violating hard tiering"));
            }
            else
            {
                applied.Add(edge);
            }
        }

        var (order, cycles, broken) = TopologicalSort(ids, applied.ToImmutable(), tiers, position);

        return new SortResult(
            order, tiers, applied.ToImmutable(), dropped.ToImmutable(), cycles, broken,
            suppressed.ToImmutable());
    }

    private static (ImmutableArray<ModId> order, ImmutableArray<CycleReport> cycles, ImmutableArray<BrokenEdge> broken)
        TopologicalSort(
            ImmutableArray<ModId> ids,
            ImmutableArray<OrderingEdge> edges,
            ImmutableDictionary<ModId, Tier> tiers,
            Dictionary<ModId, int> position)
    {
        // Mutable adjacency + in-degree. Successors kept as a list; removed edges (cycle-breaking) are honoured.
        var successors = ids.ToDictionary(id => id, _ => new List<ModId>());
        var indegree = ids.ToDictionary(id => id, _ => 0);
        var live = new HashSet<(ModId, ModId)>();
        foreach (var e in edges)
        {
            if (live.Add((e.Before, e.After)))
            {
                successors[e.Before].Add(e.After);
                indegree[e.After]++;
            }
        }

        var comparer = Comparer<ModId>.Create((a, b) =>
        {
            int t = ((int)tiers[a]).CompareTo((int)tiers[b]);
            if (t != 0) return t;
            int p = position[a].CompareTo(position[b]);
            if (p != 0) return p;
            return string.CompareOrdinal(a.Value, b.Value);
        });

        var ready = new SortedSet<ModId>(comparer);
        foreach (var id in ids)
        {
            if (indegree[id] == 0) ready.Add(id);
        }

        var order = ImmutableArray.CreateBuilder<ModId>(ids.Length);
        var emitted = new HashSet<ModId>();
        var cycles = ImmutableArray.CreateBuilder<CycleReport>();
        var broken = ImmutableArray.CreateBuilder<BrokenEdge>();

        while (emitted.Count < ids.Length)
        {
            while (ready.Count > 0)
            {
                var n = ready.Min;
                ready.Remove(n);
                order.Add(n);
                emitted.Add(n);

                foreach (var succ in successors[n])
                {
                    if (!live.Contains((n, succ))) continue;
                    if (--indegree[succ] == 0 && !emitted.Contains(succ)) ready.Add(succ);
                }
            }

            if (emitted.Count == ids.Length) break;

            // Stall → a cycle remains among the un-emitted nodes. Break it deterministically.
            var cyclePath = FindCycle(ids, emitted, successors, live);
            var edge = ChooseEdgeToBreak(cyclePath, edges, position);

            cycles.Add(new CycleReport([.. cyclePath]));
            broken.Add(new BrokenEdge(edge, [.. cyclePath]));

            live.Remove((edge.Before, edge.After));
            if (--indegree[edge.After] == 0 && !emitted.Contains(edge.After)) ready.Add(edge.After);
        }

        return (order.ToImmutable(), cycles.ToImmutable(), broken.ToImmutable());
    }

    /// <summary>Deterministic DFS over the remaining subgraph; returns the node path of the first cycle found.</summary>
    private static List<ModId> FindCycle(
        ImmutableArray<ModId> ids, HashSet<ModId> emitted,
        Dictionary<ModId, List<ModId>> successors, HashSet<(ModId, ModId)> live)
    {
        var remaining = ids.Where(id => !emitted.Contains(id)).OrderBy(id => id.Value, StringComparer.Ordinal).ToList();
        var state = new Dictionary<ModId, int>(); // 0=unvisited,1=on-stack,2=done
        var stack = new List<ModId>();

        foreach (var start in remaining)
        {
            var cycle = Dfs(start);
            if (cycle is not null) return cycle;
        }

        // Should be unreachable when a stall occurred, but stay total.
        return [remaining[0]];

        List<ModId>? Dfs(ModId node)
        {
            state[node] = 1;
            stack.Add(node);

            foreach (var succ in successors[node]
                         .Where(s => !emitted.Contains(s) && live.Contains((node, s)))
                         .OrderBy(s => s.Value, StringComparer.Ordinal))
            {
                var s = state.GetValueOrDefault(succ);
                if (s == 1)
                {
                    // Back-edge: extract the cycle from the stack.
                    int idx = stack.IndexOf(succ);
                    return stack.Skip(idx).ToList();
                }

                if (s == 0)
                {
                    var found = Dfs(succ);
                    if (found is not null) return found;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
            return null;
        }
    }

    /// <summary>
    /// Chooses which edge of a cycle to remove: lowest rule precedence first (break
    /// About before community before user), then the most "backwards" edge relative
    /// to current order, then by packageId — all deterministic.
    /// </summary>
    private static OrderingEdge ChooseEdgeToBreak(
        List<ModId> cyclePath, ImmutableArray<OrderingEdge> edges, Dictionary<ModId, int> position)
    {
        var byPair = edges
            .GroupBy(e => (e.Before, e.After))
            .ToDictionary(g => g.Key, g => g.First());

        var cycleEdges = new List<OrderingEdge>();
        for (int i = 0; i < cyclePath.Count; i++)
        {
            var before = cyclePath[i];
            var after = cyclePath[(i + 1) % cyclePath.Count];
            if (byPair.TryGetValue((before, after), out var e)) cycleEdges.Add(e);
        }

        return cycleEdges
            .OrderBy(e => (int)e.Provenance.Source)
            .ThenByDescending(e => position[e.Before] - position[e.After])
            .ThenBy(e => e.Before.Value, StringComparer.Ordinal)
            .ThenBy(e => e.After.Value, StringComparer.Ordinal)
            .First();
    }
}
