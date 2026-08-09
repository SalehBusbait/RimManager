using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Core.Rules;

/// <summary>
/// A load-order rule the user wrote themselves. <c>2i</c>-5: "Yours are
/// accent-marked and always win" — user rules are the highest precedence source in
/// the merge (About → community → user), and that ordering is stated as
/// non-configurable in Settings ▸ Sorting.
/// </summary>
public sealed record UserRule(ModId Before, ModId After, string? Comment = null)
{
    public OrderingEdge ToEdge() =>
        new(Before, After, new RuleProvenance(RuleSource.User, RuleType.LoadAfter, After, Comment));
}

/// <summary>
/// A community rule the user has switched off. Recorded as an identity, never as a
/// deletion: <c>2i</c>-5 is explicit that "disabled community rules render at 55%
/// opacity and are never deleted". Keeping the row visible-but-off is what lets
/// someone undo the decision after a resync, and what stops the next database
/// refresh from silently resurrecting it.
/// </summary>
public sealed record DisabledRule(ModId Before, ModId After, string? Reason = null);

/// <summary>
/// The user's edits to the rule graph, persisted per instance and edited through the
/// rule editor (<c>2i</c>-5). Applied on top of the merged About + community set.
/// </summary>
public sealed record RuleOverrides(
    ImmutableArray<UserRule> UserRules,
    ImmutableArray<DisabledRule> Disabled)
{
    public static readonly RuleOverrides Empty = new([], []);

    private ImmutableArray<UserRule> SafeUser => UserRules.IsDefault ? [] : UserRules;
    private ImmutableArray<DisabledRule> SafeDisabled => Disabled.IsDefault ? [] : Disabled;

    public bool IsEmpty => SafeUser.IsEmpty && SafeDisabled.IsEmpty;

    /// <summary>Counts shown on the Settings ▸ Sorting rules card ("6 local overrides").</summary>
    public int OverrideCount => SafeUser.Length + SafeDisabled.Length;

    public bool IsDisabled(ModId before, ModId after) =>
        SafeDisabled.Any(d => d.Before == before && d.After == after);

    public bool IsDisabled(OrderingEdge edge) => IsDisabled(edge.Before, edge.After);

    /// <summary>Adds or replaces a user rule for a pair — one direction per pair.</summary>
    public RuleOverrides WithUserRule(UserRule rule) =>
        this with
        {
            UserRules = [.. SafeUser.Where(r => !(r.Before == rule.Before && r.After == rule.After)), rule],
        };

    public RuleOverrides WithoutUserRule(ModId before, ModId after) =>
        this with { UserRules = [.. SafeUser.Where(r => !(r.Before == before && r.After == after))] };

    public RuleOverrides Disable(ModId before, ModId after, string? reason = null) =>
        IsDisabled(before, after)
            ? this
            : this with { Disabled = [.. SafeDisabled, new DisabledRule(before, after, reason)] };

    /// <summary>Re-enables a community rule. The row was never removed, only marked.</summary>
    public RuleOverrides Enable(ModId before, ModId after) =>
        this with { Disabled = [.. SafeDisabled.Where(d => !(d.Before == before && d.After == after))] };

    /// <summary>
    /// Applies the overrides to a merged edge set: drops every disabled edge, then
    /// appends the user's own.
    /// <para>
    /// Order matters. Appending after the filter is what makes a user rule survive
    /// even when it names the same pair as a rule they disabled — "yours always win"
    /// has to hold in that case too, or re-adding a rule you had switched off would
    /// silently do nothing.
    /// </para>
    /// </summary>
    public ImmutableArray<OrderingEdge> Apply(ImmutableArray<OrderingEdge> edges)
    {
        var kept = edges.IsDefaultOrEmpty
            ? []
            : edges.Where(e => !IsDisabled(e)).ToList();

        // A user rule replaces any surviving edge for the same pair, rather than
        // sitting alongside it as a duplicate.
        foreach (var rule in SafeUser)
        {
            kept.RemoveAll(e => e.Before == rule.Before && e.After == rule.After);
            kept.Add(rule.ToEdge());
        }

        return [.. kept];
    }
}
