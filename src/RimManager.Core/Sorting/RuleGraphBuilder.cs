using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Rules;

namespace RimManager.Core.Sorting;

/// <summary>
/// Merges About.xml rules, the community DB, and user overrides into a single
/// <see cref="RuleSet"/> scoped to a set of active mods. Later sources override
/// earlier ones per unordered pair; a same-precedence contradiction is preserved
/// (it surfaces as a cycle downstream rather than being silently dropped).
/// </summary>
public static class RuleGraphBuilder
{
    /// <param name="overrides">
    /// The rule editor's output: rules the user wrote, and community rules they
    /// switched off. Applied AFTER the merge, so a disabled pair drops whichever
    /// representative won the merge and a user rule replaces it outright — "yours
    /// always win" composed at the one point every consumer passes through. This is
    /// deliberately a parameter here rather than a wrapper at the call sites: the
    /// validator builds its own graph internally, and two compositions drift.
    /// </param>
    public static RuleSet Build(
        IReadOnlyList<Mod> activeMods,
        LoadOrderRules? community = null,
        LoadOrderRules? user = null,
        RuleOverrides? overrides = null)
    {
        community ??= LoadOrderRules.Empty;
        user ??= LoadOrderRules.Empty;

        var activeIds = activeMods.Select(m => m.PackageId).ToHashSet();
        var raw = new List<OrderingEdge>();

        AddAboutEdges(activeMods, activeIds, raw);
        AddDbEdges(community.Rules, RuleSource.Community, activeIds, raw);
        AddDbEdges(user.Rules, RuleSource.User, activeIds, raw);

        var edges = MergeEdges(raw);

        if (overrides is { IsEmpty: false })
        {
            // Scope the user's edges the way every other source is scoped: a rule
            // naming a mod that is not in this list must not order phantoms.
            edges = [.. overrides.Apply(edges).Where(
                e => activeIds.Contains(e.Before) && activeIds.Contains(e.After))];
        }

        var (loadTop, loadBottom) = MergeTierHints(community, user, activeIds);

        return new RuleSet(edges, loadTop, loadBottom);
    }

    // --- edge collection ----------------------------------------------------

    private static void AddAboutEdges(IReadOnlyList<Mod> mods, HashSet<ModId> active, List<OrderingEdge> raw)
    {
        foreach (var mod in mods)
        {
            var id = mod.PackageId;
            foreach (var other in mod.LoadAfter)
                Add(raw, active, other, id, new RuleProvenance(RuleSource.About, RuleType.LoadAfter, id));
            foreach (var other in mod.LoadBefore)
                Add(raw, active, id, other, new RuleProvenance(RuleSource.About, RuleType.LoadBefore, id));
            foreach (var other in mod.ForceLoadAfter)
                Add(raw, active, other, id, new RuleProvenance(RuleSource.About, RuleType.ForceLoadAfter, id));
            foreach (var other in mod.ForceLoadBefore)
                Add(raw, active, id, other, new RuleProvenance(RuleSource.About, RuleType.ForceLoadBefore, id));
        }
    }

    private static void AddDbEdges(
        ImmutableDictionary<ModId, ModRules> rules, RuleSource source, HashSet<ModId> active, List<OrderingEdge> raw)
    {
        foreach (var (id, modRules) in rules)
        {
            if (!active.Contains(id)) continue;
            foreach (var r in modRules.LoadAfter)
                Add(raw, active, r.PackageId, id, new RuleProvenance(source, RuleType.LoadAfter, id, r.Comment));
            foreach (var r in modRules.LoadBefore)
                Add(raw, active, id, r.PackageId, new RuleProvenance(source, RuleType.LoadBefore, id, r.Comment));
        }
    }

    private static void Add(List<OrderingEdge> raw, HashSet<ModId> active, ModId before, ModId after, RuleProvenance p)
    {
        if (before == after) return;                    // self-edge: meaningless
        if (!active.Contains(before) || !active.Contains(after)) return; // ignore rules about absent mods
        raw.Add(new OrderingEdge(before, after, p));
    }

    // --- merge --------------------------------------------------------------

    /// <summary>
    /// Unions every source, keeping one representative edge per <b>direction</b>.
    /// <para>
    /// About, community and user rules <b>add to each other</b>. They used to be merged
    /// per unordered pair with the highest source winning outright, so a community rule
    /// touching {A,B} deleted the mod author's rule about the same pair even when the
    /// two agreed — and when they disagreed, one simply vanished with nothing said.
    /// </para>
    /// <para>
    /// Measured on a real 548-mod install against 629 community rules before changing
    /// it: <b>no pair anywhere has sources disagreeing on direction</b>. Twenty pairs
    /// carry several raw edges and all twenty agree — Core's <c>forceLoadBefore
    /// Ideology</c> and Ideology's <c>forceLoadAfter Core</c> both produce
    /// <c>Core → Ideology</c>. So overriding was never discarding anything here; it was
    /// simply the wrong rule, waiting for the day two sources really differ.
    /// </para>
    /// <para>
    /// On that day both directions now survive, the graph has a two-node cycle, and it
    /// surfaces in the Cycles category — which exists precisely to show a contradiction
    /// and say which edge was dropped to resolve it. That is strictly better than one
    /// source silently deleting another.
    /// </para>
    /// </summary>
    private static ImmutableArray<OrderingEdge> MergeEdges(List<OrderingEdge> raw)
    {
        var result = ImmutableArray.CreateBuilder<OrderingEdge>();

        foreach (var directionGroup in raw.GroupBy(e => (e.Before, e.After)))
        {
            // One representative per direction, chosen deterministically. The highest
            // source names it, so an edge both a mod and the database assert is
            // attributed to the database — the stronger claim, and the one a user
            // editing rules would go looking for.
            var representative = directionGroup
                .OrderByDescending(e => e.Provenance.Source)
                .ThenBy(e => e.Provenance.Type)
                .ThenBy(e => e.Provenance.DeclaredBy?.Value ?? string.Empty, StringComparer.Ordinal)
                .First();
            result.Add(representative);
        }

        return result.ToImmutable();
    }

    private static (ImmutableDictionary<ModId, RuleProvenance> top, ImmutableDictionary<ModId, RuleProvenance> bottom)
        MergeTierHints(LoadOrderRules community, LoadOrderRules user, HashSet<ModId> active)
    {
        // Per mod, the highest-precedence hint wins; loadTop and loadBottom are mutually exclusive.
        var winning = new Dictionary<ModId, (RuleSource source, bool isTop, RuleProvenance prov)>();

        void Consider(LoadOrderRules db, RuleSource source)
        {
            foreach (var (id, r) in db.Rules)
            {
                if (!active.Contains(id)) continue;
                if (r.LoadTop) Offer(id, source, true, new RuleProvenance(source, RuleType.LoadTop, id, r.LoadTopComment));
                if (r.LoadBottom) Offer(id, source, false, new RuleProvenance(source, RuleType.LoadBottom, id, r.LoadBottomComment));
            }
        }

        void Offer(ModId id, RuleSource source, bool isTop, RuleProvenance prov)
        {
            // Higher source wins; within the same source, Top wins over Bottom (deterministic).
            if (winning.TryGetValue(id, out var cur) && (cur.source > source || (cur.source == source && cur.isTop)))
                return;
            winning[id] = (source, isTop, prov);
        }

        Consider(community, RuleSource.Community);
        Consider(user, RuleSource.User);

        var top = ImmutableDictionary.CreateBuilder<ModId, RuleProvenance>();
        var bottom = ImmutableDictionary.CreateBuilder<ModId, RuleProvenance>();
        foreach (var (id, w) in winning)
        {
            (w.isTop ? top : bottom)[id] = w.prov;
        }

        return (top.ToImmutable(), bottom.ToImmutable());
    }
}
