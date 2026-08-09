using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Domain;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;

namespace RimManager.App.ViewModels;

/// <summary>Which way round a rule reads, from the selected mod's point of view.</summary>
public enum RuleDirection
{
    /// <summary>The selected mod loads AFTER the other one.</summary>
    After,

    /// <summary>The selected mod loads BEFORE the other one.</summary>
    Before,
}

/// <summary>One rule row in the editor (<c>2i</c>-5).</summary>
/// <param name="Other">The mod at the far end of the rule.</param>
/// <param name="Source">Where it came from — this decides what can be done to it.</param>
/// <param name="Comment">The note the community DB attaches, when it has one.</param>
/// <param name="IsDisabled">A community rule the user has switched off. Never removed.</param>
public sealed record RuleRow(
    ModId Other,
    string OtherName,
    RuleDirection Direction,
    RuleSource Source,
    string? Comment = null,
    bool IsDisabled = false)
{
    /// <summary>"loads after" / "loads before", from the selected mod's side.</summary>
    public string DirectionText => Direction == RuleDirection.After ? "loads after" : "loads before";

    /// <summary>The toggle names the action it will TAKE (T6): the command flips a
    /// disabled rule back on, and a button reading "Switch off" over an already-off
    /// rule promised the wrong direction.</summary>
    public string SwitchLabel => IsDisabled ? "Switch on" : "Switch off";

    public string SourceText => Source switch
    {
        RuleSource.About => "About.xml",
        RuleSource.Community => "community",
        _ => "yours",
    };

    /// <summary>
    /// A mod's own declaration is not the user's to edit — changing it would mean editing
    /// somebody else's file, and the next Workshop update would overwrite it anyway. The
    /// row is shown locked (<c>2i</c>-5's 🔒) rather than hidden, because it is still part
    /// of why the order is what it is.
    /// </summary>
    public bool IsLocked => Source == RuleSource.About;

    /// <summary>Yours, and they win the merge. Accent-marked in the list.</summary>
    public bool IsUserRule => Source == RuleSource.User;

    /// <summary>Only community rules can be switched off; About is locked and yours delete.</summary>
    public bool CanDisable => Source == RuleSource.Community;

    /// <summary>Yours can be removed outright — there is no upstream to preserve.</summary>
    public bool CanDelete => Source == RuleSource.User;

    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
}

/// <summary>
/// Builds the rule editor's rows (<c>2i</c>-5) from the merged rule set plus the user's
/// overrides.
/// <para>
/// Pure, and it earns it: the same pair can be declared by About.xml, restated by the
/// community DB and overridden by the user, and what the editor must show is one row per
/// relationship with the source that actually governs — not three rows that look like
/// three rules.
/// </para>
/// </summary>
public static class RuleEditorPresenter
{
    /// <summary>
    /// Every rule touching <paramref name="subject"/>, from its own point of view.
    /// </summary>
    public static ImmutableArray<RuleRow> RowsFor(
        ModId subject,
        LoadOrderRules merged,
        RuleOverrides overrides,
        IReadOnlyDictionary<ModId, Mod> installed,
        RuleSource sourceOfMerged = RuleSource.Community)
    {
        var rows = new Dictionary<(ModId Other, RuleDirection Direction), RuleRow>();

        void Add(ModId other, RuleDirection direction, RuleSource source, string? comment, bool disabled)
        {
            var key = (other, direction);

            // Precedence decides which row survives, exactly as the merge does: About →
            // community → user. Showing all three would make one relationship look like
            // three rules that might disagree.
            if (rows.TryGetValue(key, out var existing) && existing.Source >= source) return;

            var name = installed.TryGetValue(other, out var mod) ? mod.Name : other.Display;
            rows[key] = new RuleRow(other, name, direction, source, comment, disabled);
        }

        if (merged.Rules.TryGetValue(subject, out var declared))
        {
            foreach (var reference in declared.LoadAfter)
            {
                Add(reference.PackageId, RuleDirection.After, sourceOfMerged, reference.Comment,
                    overrides.IsDisabled(reference.PackageId, subject));
            }

            foreach (var reference in declared.LoadBefore)
            {
                Add(reference.PackageId, RuleDirection.Before, sourceOfMerged, reference.Comment,
                    overrides.IsDisabled(subject, reference.PackageId));
            }
        }

        // The other side of the relationship: another mod saying "load me after this one"
        // is a rule about this one too, and the editor is the place you go to understand
        // why something moved.
        foreach (var (other, otherRules) in merged.Rules)
        {
            if (other == subject) continue;

            foreach (var reference in otherRules.LoadAfter.Where(r => r.PackageId == subject))
            {
                Add(other, RuleDirection.Before, sourceOfMerged, reference.Comment,
                    overrides.IsDisabled(subject, other));
            }

            foreach (var reference in otherRules.LoadBefore.Where(r => r.PackageId == subject))
            {
                Add(other, RuleDirection.After, sourceOfMerged, reference.Comment,
                    overrides.IsDisabled(other, subject));
            }
        }

        foreach (var rule in overrides.UserRules)
        {
            if (rule.After == subject) Add(rule.Before, RuleDirection.After, RuleSource.User, rule.Comment, false);
            else if (rule.Before == subject) Add(rule.After, RuleDirection.Before, RuleSource.User, rule.Comment, false);
        }

        return
        [
            .. rows.Values
                // Yours first — they are the ones you came here to change — then the
                // active community rules, then the disabled and locked ones.
                .OrderByDescending(r => r.IsUserRule)
                .ThenBy(r => r.IsDisabled)
                .ThenBy(r => r.IsLocked)
                .ThenBy(r => r.OtherName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// How many rules touch a mod, for the left-hand list's count column. Disabled rules
    /// are counted separately: a mod showing "4" when three are switched off would explain
    /// nothing about the order it ends up in.
    /// </summary>
    public static string CountLabel(int active, int disabled) => disabled == 0
        ? active.ToString()
        : $"{active} · {disabled} off";

    /// <summary>
    /// The line under the list, stating the precedence out loud. Settings ▸ Sorting says
    /// the same thing; both are stated because it is the rule people most often assume
    /// works the other way round.
    /// </summary>
    public const string PrecedenceNote =
        "About.xml declares, the community database adds, and your rules beat both. "
        + "A community rule you switch off is remembered as disabled — never deleted — so a "
        + "database resync cannot quietly bring it back.";
}
