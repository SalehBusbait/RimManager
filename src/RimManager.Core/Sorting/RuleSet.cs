using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>
/// The merged, precedence-resolved rules for a specific set of active mods:
/// ordering edges plus <c>loadTop</c>/<c>loadBottom</c> tier hints, each carrying
/// provenance for "explain".
/// </summary>
public sealed class RuleSet
{
    public ImmutableArray<OrderingEdge> Edges { get; }
    public ImmutableDictionary<ModId, RuleProvenance> LoadTop { get; }
    public ImmutableDictionary<ModId, RuleProvenance> LoadBottom { get; }

    public RuleSet(
        ImmutableArray<OrderingEdge> edges,
        ImmutableDictionary<ModId, RuleProvenance> loadTop,
        ImmutableDictionary<ModId, RuleProvenance> loadBottom)
    {
        Edges = edges;
        LoadTop = loadTop;
        LoadBottom = loadBottom;
    }
}
