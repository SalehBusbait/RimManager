using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>One rule connecting a mod to another, for "explain" output.</summary>
public sealed record RuleLink(ModId Other, RuleProvenance Provenance);

/// <summary>An edge dropped because it contradicted hard tiering.</summary>
public sealed record DroppedEdge(OrderingEdge Edge, string Reason);

/// <summary>An edge removed to break a cycle, with the cycle it belonged to.</summary>
public sealed record BrokenEdge(OrderingEdge Edge, ImmutableArray<ModId> Cycle);

/// <summary>A detected dependency cycle (node path, first == last conceptually).</summary>
public sealed record CycleReport(ImmutableArray<ModId> Nodes);

/// <summary>Why a single mod landed where it did (spec §4.4 "Explain this order").</summary>
public sealed record ModExplanation(
    ModId Id,
    Tier Tier,
    int Position,
    ImmutableArray<RuleLink> LoadsAfter,
    ImmutableArray<RuleLink> LoadsBefore,
    ImmutableArray<OrderingEdge> IgnoredForTier);

/// <summary>The full output of a sort: the new order plus everything needed to explain it.</summary>
public sealed class SortResult
{
    public ImmutableArray<ModId> Order { get; }
    public ImmutableDictionary<ModId, Tier> Tiers { get; }
    public ImmutableArray<OrderingEdge> AppliedEdges { get; }
    public ImmutableArray<DroppedEdge> DroppedForTier { get; }
    public ImmutableArray<CycleReport> Cycles { get; }
    public ImmutableArray<BrokenEdge> BrokenEdges { get; }

    /// <summary>
    /// Edges the user explicitly chose to drop ("Drop a different edge…" in the
    /// Warnings panel or the cycle graph). Reported separately from
    /// <see cref="DroppedForTier"/> because the UI must be able to say "you dropped
    /// this" rather than "the sorter dropped this", and to offer to restore it.
    /// </summary>
    public ImmutableArray<DroppedEdge> SuppressedByUser { get; }

    private readonly ImmutableDictionary<ModId, int> _positions;

    public SortResult(
        ImmutableArray<ModId> order,
        ImmutableDictionary<ModId, Tier> tiers,
        ImmutableArray<OrderingEdge> appliedEdges,
        ImmutableArray<DroppedEdge> droppedForTier,
        ImmutableArray<CycleReport> cycles,
        ImmutableArray<BrokenEdge> brokenEdges,
        ImmutableArray<DroppedEdge> suppressedByUser = default)
    {
        Order = order;
        Tiers = tiers;
        AppliedEdges = appliedEdges;
        DroppedForTier = droppedForTier;
        Cycles = cycles;
        BrokenEdges = brokenEdges;
        SuppressedByUser = suppressedByUser.IsDefault ? [] : suppressedByUser;

        var positions = ImmutableDictionary.CreateBuilder<ModId, int>();
        for (int i = 0; i < order.Length; i++) positions[order[i]] = i;
        _positions = positions.ToImmutable();
    }

    public bool HasCycles => !Cycles.IsDefaultOrEmpty;

    public int PositionOf(ModId id) => _positions.TryGetValue(id, out var p) ? p : -1;

    /// <summary>Builds the "why is this mod here" explanation for one mod.</summary>
    public ModExplanation Explain(ModId id)
    {
        var loadsAfter = AppliedEdges
            .Where(e => e.After == id)
            .Select(e => new RuleLink(e.Before, e.Provenance))
            .ToImmutableArray();

        var loadsBefore = AppliedEdges
            .Where(e => e.Before == id)
            .Select(e => new RuleLink(e.After, e.Provenance))
            .ToImmutableArray();

        var ignored = DroppedForTier
            .Where(d => d.Edge.Before == id || d.Edge.After == id)
            .Select(d => d.Edge)
            .ToImmutableArray();

        return new ModExplanation(
            id,
            Tiers.GetValueOrDefault(id, Tier.Normal),
            PositionOf(id),
            loadsAfter,
            loadsBefore,
            ignored);
    }
}
