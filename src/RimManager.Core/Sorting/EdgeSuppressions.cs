using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>
/// An ordering edge the user has chosen to drop.
/// <para>
/// When a sort breaks a cycle it picks an edge deterministically — lowest rule
/// precedence, then most-backwards, then by packageId. That is a good default but
/// it is still a guess, and the Warnings detail panel (<c>2a</c>) and cycle graph
/// (<c>3b</c>) both let the user say "drop a different one instead". Recording that
/// choice is what makes the picture stable: the same cycle resolves the same way
/// on every subsequent sort until the user changes their mind.
/// </para>
/// </summary>
/// <param name="Reason">Free text for the UI, e.g. "chosen from the cycle graph".</param>
public sealed record SuppressedEdge(ModId Before, ModId After, string? Reason = null)
{
    public bool Matches(OrderingEdge edge) => edge.Before == Before && edge.After == After;
}

/// <summary>
/// The set of edges the user has suppressed, persisted per profile so a cycle
/// resolution survives a restart.
/// </summary>
public sealed record EdgeSuppressions(ImmutableArray<SuppressedEdge> Edges)
{
    public static readonly EdgeSuppressions Empty = new([]);

    public bool IsEmpty => Edges.IsDefaultOrEmpty;

    public bool Contains(OrderingEdge edge) =>
        !Edges.IsDefaultOrEmpty && Edges.Any(s => s.Matches(edge));

    public bool Contains(ModId before, ModId after) =>
        !Edges.IsDefaultOrEmpty && Edges.Any(s => s.Before == before && s.After == after);

    /// <summary>Adds a suppression; adding one that already exists is a no-op.</summary>
    public EdgeSuppressions With(ModId before, ModId after, string? reason = null) =>
        Contains(before, after)
            ? this
            : new EdgeSuppressions((Edges.IsDefault ? [] : Edges).Add(new SuppressedEdge(before, after, reason)));

    /// <summary>Removes a suppression, restoring the edge to the graph.</summary>
    public EdgeSuppressions Without(ModId before, ModId after) =>
        Edges.IsDefaultOrEmpty
            ? this
            : new EdgeSuppressions([.. Edges.Where(s => !(s.Before == before && s.After == after))]);
}
