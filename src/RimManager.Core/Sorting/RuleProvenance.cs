using RimManager.Core.Domain;

namespace RimManager.Core.Sorting;

/// <summary>Where a rule came from. Higher ordinal wins conflicts (spec §4.4: later sources override earlier).</summary>
public enum RuleSource
{
    /// <summary>Declared in a mod's own <c>About.xml</c>.</summary>
    About = 0,

    /// <summary>From the community rules database.</summary>
    Community = 1,

    /// <summary>A user override (highest precedence).</summary>
    User = 2,
}

/// <summary>The specific kind of rule, for display in "explain".</summary>
public enum RuleType
{
    LoadAfter,
    LoadBefore,
    ForceLoadAfter,
    ForceLoadBefore,
    LoadTop,
    LoadBottom,
}

/// <summary>
/// Why an edge or tier hint exists — the data behind "Explain this order" (spec §4.4).
/// </summary>
/// <param name="Source">Which rule source produced it.</param>
/// <param name="Type">The rule kind.</param>
/// <param name="DeclaredBy">The mod whose rules produced this, when applicable.</param>
/// <param name="Comment">Optional human note (community DB rules carry these).</param>
public sealed record RuleProvenance(
    RuleSource Source,
    RuleType Type,
    ModId? DeclaredBy = null,
    string? Comment = null);
