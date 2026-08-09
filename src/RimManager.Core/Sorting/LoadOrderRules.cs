using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>A referenced mod in a rule, with the optional comment the DB attaches.</summary>
public sealed record RuleRef(ModId PackageId, string? Comment = null);

/// <summary>The rules the database declares for one mod.</summary>
public sealed record ModRules
{
    public ImmutableArray<RuleRef> LoadAfter { get; init; } = [];
    public ImmutableArray<RuleRef> LoadBefore { get; init; } = [];
    public bool LoadTop { get; init; }
    public bool LoadBottom { get; init; }
    public string? LoadTopComment { get; init; }
    public string? LoadBottomComment { get; init; }
}

/// <summary>
/// A parsed rules database keyed by mod. Source-agnostic: the same shape holds the
/// community rules DB and the user's overrides (they merge with different
/// <see cref="RuleSource"/> precedence). Mirrors RimSort's <c>communityRules.json</c>.
/// </summary>
public sealed record LoadOrderRules(ImmutableDictionary<ModId, ModRules> Rules)
{
    public static readonly LoadOrderRules Empty = new(ImmutableDictionary<ModId, ModRules>.Empty);
}
