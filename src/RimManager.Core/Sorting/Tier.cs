namespace RimManager.Core.Sorting;

/// <summary>
/// Hard load-order tiers (domain spec §4.4). Lower ordinal loads first. Tiers
/// dominate rule edges: an About/community rule that would order a later tier
/// before an earlier one is dropped rather than honoured, which also prevents a
/// whole class of tier-vs-rule cycles.
/// <para>
/// The ordinals were renumbered once <see cref="Top"/> was understood — see its own
/// note. Ordinals rather than declaration order decide the sort, so the members below
/// stay in their original textual order and the numbers carry the meaning.
/// </para>
/// </summary>
public enum Tier
{
    /// <summary>Forced to the very top by a <c>loadTop</c> rule.</summary>
    Top = 3,

    /// <summary>
    /// Loads before the base game: Harmony and the other pre-patchers.
    /// <para>
    /// This was <c>Harmony</c>, and the rename is the fix rather than tidying. Loading
    /// before Core was treated as an <b>identity</b> — one hardcoded packageId — when
    /// it is a property a mod <b>declares about itself</b>, with
    /// <c>&lt;loadBefore&gt;Ludeon.RimWorld&lt;/loadBefore&gt;</c>. Every other
    /// pre-patcher therefore landed in <see cref="Normal"/>, which made its own rule a
    /// tier violation: the sorter dropped it and the validator, which reports only
    /// applied edges, never mentioned it. Measured on a real install, that silently
    /// discarded Prepatcher's, Loading Progress's and Better Stacktraces' declarations.
    /// </para>
    /// <para>
    /// They share one tier rather than getting a band each, so their mutual ordering is
    /// decided by their own rules — Prepatcher before Harmony, Loading Progress after
    /// it — which is what those rules are for.
    /// </para>
    /// </summary>
    PreCore = 0,

    /// <summary>The base game (<c>ludeon.rimworld</c>).</summary>
    Core = 1,

    /// <summary>Official expansions.</summary>
    Dlc = 2,

    /// <summary>Everything else.</summary>
    Normal = 4,

    /// <summary>Forced to the very bottom by a <c>loadBottom</c> rule.</summary>
    Bottom = 5,
}
