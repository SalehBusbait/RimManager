using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>
/// A directed ordering constraint: <see cref="Before"/> must load before
/// <see cref="After"/>. All About/community/user ordering rules normalize to this.
/// </summary>
public sealed record OrderingEdge(ModId Before, ModId After, RuleProvenance Provenance)
{
    /// <summary>The unordered pair, used to detect conflicting directions during merge.</summary>
    public (ModId, ModId) UnorderedKey =>
        string.CompareOrdinal(Before.Value, After.Value) <= 0 ? (Before, After) : (After, Before);
}
