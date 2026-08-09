using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Validation;

public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>Stable machine codes for the Tier-1 checks (spec §4.5).</summary>
public static class IssueCodes
{
    public const string MissingDependency = "dep.missing";
    public const string MissingDlc = "dep.missing-dlc";
    public const string DependencyInactive = "dep.inactive";
    public const string IncompatibleActive = "incompatible.active";
    public const string OrderViolated = "order.violated";
    public const string UnsupportedVersion = "version.unsupported";

    /// <summary>
    /// A maintained replacement exists for this mod (N7, Mlie's UseThisInstead).
    /// <b>Intrinsic</b>: a fact about the mod, true whether or not it is loaded, and
    /// exactly what you want to know while deciding whether to load it. Info-toned in
    /// the App — nothing is broken, the mod may run fine — and gated upstream to
    /// replacements that support the running game version.
    /// </summary>
    public const string ReplacementAvailable = "mod.replacement-available";

    /// <summary>
    /// A rule no ordering can satisfy, because honouring it would put a later tier
    /// before an earlier one.
    /// <para>
    /// Reported because the alternative is what shipped: the rule was dropped and
    /// nothing said so anywhere in the app. On a real install ten rules were being
    /// discarded in silence, including four the mods' own authors wrote.
    /// </para>
    /// <para>
    /// It is <b>rare now, and that is the point</b>. Six of those ten were one mod —
    /// <c>imranfish.xmlextensions</c>, whose <c>loadTop</c> we read as "top of the file"
    /// when the database means "top of the mods" (see <see cref="Sorting.Tier.Top"/>).
    /// With Top in its right place, exactly one survives on that install: the community
    /// database marks XML Extensions <c>loadTop</c> AND says another mod must load
    /// before it. That is a contradiction inside the database itself, not an artefact of
    /// tiering, and no tier order can satisfy both.
    /// </para>
    /// <para>
    /// Distinct from <see cref="OrderViolated"/> in <b>tone, not wording</b>: both say
    /// only "A should load before B". This one carries Info because nothing is broken
    /// and sorting cannot clear it, so counting it beside warnings that sorting DOES
    /// clear would make the number mean two different things.
    /// </para>
    /// </summary>
    public const string OrderTierOverride = "order.tier-override";
}

/// <summary>
/// One validation finding. <see cref="Subject"/> is the mod the issue is about;
/// <see cref="Related"/> is the other mod involved (a dependency, an incompatible
/// mod, or the mod an order rule references). Both let the UI "jump to the mod".
/// </summary>
/// <param name="DeclaredBy">
/// The mod whose own rules produced this finding, when that is not the
/// <paramref name="Subject"/>.
/// <para>
/// Only load-order issues need it, and they need it badly: an edge is built from
/// whichever mod wrote the rule, so <c>XmlExtensions</c> declaring
/// <c>loadAfter Ludeon.RimWorld</c> produces the edge <c>rimworld → xmlextensions</c>
/// whose <b>Subject is the base game</b>. Attributing by Subject therefore hung four
/// warnings on RimWorld's row for rules RimWorld never wrote — it declares nothing at
/// all, and a mod is not at fault because someone else's rule points at it.
/// </para>
/// </param>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    ModId? Subject = null,
    ModId? Related = null,
    ModId? DeclaredBy = null)
{
    /// <summary>
    /// The mod this finding belongs to: whoever wrote the rule, else the subject.
    /// <para>
    /// This is what puts a warning glyph on a row. One rule, stated once: <b>a row's
    /// warning means something THAT MOD declared is not satisfied.</b>
    /// </para>
    /// </summary>
    public ModId? Owner => DeclaredBy ?? Subject;
}

/// <summary>The full result of validating a mod list.</summary>
public sealed record ValidationReport(ImmutableArray<ValidationIssue> Issues)
{
    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    public bool IsClean => Issues.IsDefaultOrEmpty;
}
