using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.ModDatabases;
using RimManager.Core.Rules;
using RimManager.Core.Sorting;

namespace RimManager.Core.Validation;

/// <summary>
/// The Tier-1 validators (spec §4.5): missing dependency, missing DLC, declared
/// incompatibility active, load-order rule violated, unsupported game version.
/// Pure: it inspects an ordered active list against what's installed.
/// <para>
/// The checks fall into two kinds, and the distinction decides which mods each one may
/// be asked about:
/// </para>
/// <list type="bullet">
///   <item><b>Relational</b> — about the <i>list</i>. Dependencies, incompatibilities
///   and load order are all statements about what else is loaded and in what sequence.
///   They are meaningless for a mod that is not loaded: a mod sitting in the inactive
///   pane has no position, breaks nothing, and its dependency is not missing because
///   nothing is asking for it.</item>
///   <item><b>Intrinsic</b> — about the <i>mod</i>. Whether its About.xml lists your
///   game version is true whether or not you load it, and it is exactly what you want
///   to know while deciding whether to.</item>
/// </list>
/// </summary>
public sealed class ModListValidator
{
    /// <param name="activeInOrder">Active mods in their current load order.</param>
    /// <param name="knownExpansions">Owned DLCs (from ModsConfig).</param>
    /// <param name="gameMajorMinor">Active game version, e.g. <c>1.6</c>.</param>
    /// <param name="community">Optional community rules to include in the order check.</param>
    /// <param name="inactive">
    /// Installed mods that are NOT in the load order. Only the intrinsic checks run over
    /// them — never the relational ones, which would report a missing dependency for a
    /// mod nothing is loading and a load-order violation for a mod that has no position.
    /// </param>
    /// <param name="knownGood">
    /// Mlie's NoVersionWarning list for <paramref name="gameMajorMinor"/> (N7): packageIds
    /// reported working despite not declaring support. Suppresses the unsupported-version
    /// warning for exactly those — the warning stays for everyone else.
    /// </param>
    /// <param name="replacements">
    /// Mlie's UseThisInstead rules (N7). Adds the intrinsic
    /// <see cref="IssueCodes.ReplacementAvailable"/> finding, matched by
    /// <see cref="ModDatabases.ReplacementMatcher"/>'s rules and gated to replacements
    /// that support the running version.
    /// </param>
    /// <param name="overrides">
    /// The rule editor's user rules and disabled community rules. The validator must
    /// see the SAME effective rule set the sorter sees: without this, a rule the user
    /// switched off keeps warning, and a rule they wrote never does.
    /// </param>
    public ValidationReport Validate(
        IReadOnlyList<Mod> activeInOrder,
        IReadOnlyCollection<ModId> knownExpansions,
        string? gameMajorMinor,
        LoadOrderRules? community = null,
        IReadOnlyList<Mod>? inactive = null,
        KnownGoodDatabase? knownGood = null,
        ImmutableArray<ModReplacement> replacements = default,
        RuleOverrides? overrides = null)
    {
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        var activeSet = activeInOrder.Select(m => m.PackageId).ToHashSet();
        var known = knownExpansions.ToHashSet();

        // A LOOKUP, not a subject: the dependency check stays relational and runs over
        // the active list only, but "installed and one click away" and "not on this
        // machine" are different problems with different fixes, and only the installed
        // set can tell them apart.
        var installedInactive = inactive?.Select(m => m.PackageId).ToHashSet() ?? [];

        // Relational: the active list only.
        CheckDependenciesAndDlc(activeInOrder, activeSet, known, installedInactive, issues);
        CheckIncompatibilities(activeInOrder, activeSet, issues);
        CheckOrder(activeInOrder, community, overrides, issues);

        // Intrinsic: every installed mod, loaded or not.
        CheckVersions(activeInOrder, gameMajorMinor, knownGood, issues);
        CheckReplacements(activeInOrder, replacements, gameMajorMinor, issues);
        if (inactive is not null)
        {
            CheckVersions(inactive, gameMajorMinor, knownGood, issues);
            CheckReplacements(inactive, replacements, gameMajorMinor, issues);
        }

        return new ValidationReport(issues.ToImmutable());
    }

    private static void CheckDependenciesAndDlc(
        IReadOnlyList<Mod> active, HashSet<ModId> activeSet, HashSet<ModId> known,
        HashSet<ModId> installedInactive,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        foreach (var mod in active)
        {
            foreach (var dep in mod.Dependencies)
            {
                if (activeSet.Contains(dep.PackageId)) continue;

                var depName = dep.DisplayName ?? dep.PackageId.Display;

                if (dep.PackageId == KnownMods.Core || KnownMods.IsOfficialDlc(dep.PackageId))
                {
                    var owned = known.Contains(dep.PackageId) ? " (owned but not active)" : " (not owned)";
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, IssueCodes.MissingDlc,
                        $"'{mod.PackageId.Display}' requires DLC '{depName}'{owned}.", mod.PackageId, dep.PackageId));
                }
                else if (installedInactive.Contains(dep.PackageId))
                {
                    // Its own code since it has its own fix: the mod is on disk, one
                    // activation away — sending the user to the Workshop for it would
                    // be a search for something already found. dep.inactive existed
                    // unused from the day it was declared; this is its first emission.
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, IssueCodes.DependencyInactive,
                        $"'{mod.PackageId.Display}' requires '{depName}', which is installed but not active.",
                        mod.PackageId, dep.PackageId));
                }
                else
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, IssueCodes.MissingDependency,
                        $"'{mod.PackageId.Display}' requires '{depName}', which is not active.",
                        mod.PackageId, dep.PackageId));
                }
            }
        }
    }

    private static void CheckIncompatibilities(
        IReadOnlyList<Mod> active, HashSet<ModId> activeSet, ImmutableArray<ValidationIssue>.Builder issues)
    {
        var reported = new HashSet<(ModId, ModId)>();
        foreach (var mod in active)
        {
            foreach (var inc in mod.IncompatibleWith)
            {
                if (!activeSet.Contains(inc)) continue;

                var pair = string.CompareOrdinal(mod.PackageId.Value, inc.Value) <= 0
                    ? (mod.PackageId, inc) : (inc, mod.PackageId);
                if (!reported.Add(pair)) continue;

                issues.Add(new ValidationIssue(ValidationSeverity.Error, IssueCodes.IncompatibleActive,
                    $"'{mod.PackageId.Display}' is incompatible with '{inc.Display}', but both are active.",
                    mod.PackageId, inc));
            }
        }
    }

    private static void CheckOrder(
        IReadOnlyList<Mod> active, LoadOrderRules? community, RuleOverrides? overrides,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        var position = new Dictionary<ModId, int>(active.Count);
        for (int i = 0; i < active.Count; i++) position[active[i].PackageId] = i;

        var ruleSet = RuleGraphBuilder.Build(active, community, overrides: overrides);
        var sorted = new ModSorter().Sort(active, ruleSet);

        // Applied edges: the rule holds, so being out of order is a violation to fix.
        foreach (var edge in sorted.AppliedEdges)
        {
            if (position[edge.Before] > position[edge.After])
            {
                // The rule and nothing else. The provenance used to be in the sentence
                // — "(Community/LoadAfter)" — and it answered a question nobody was
                // asking in the middle of the one they were: does this load before that
                // or not. Where a rule came from belongs in the detail panel, next to
                // the buttons that act on it.
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, IssueCodes.OrderViolated,
                    $"'{edge.Before.Display}' should load before '{edge.After.Display}' " +
                    "but currently loads after it.",
                    edge.Before, edge.After, edge.Provenance.DeclaredBy));
            }
        }

        // Dropped-for-tier edges: the rule does NOT hold, and until now nothing said so
        // anywhere in the app. Measured on a real 548-mod install, ten rules were being
        // discarded in silence — four of them written by the mods' own authors, five by
        // the community database. Tiering dominating rules is the design; dominating
        // them invisibly is how a user concludes the app simply missed something.
        //
        // Reported only when the CURRENT ORDER actually breaks the rule. It used to be
        // reported unconditionally, on the reasoning that the rule is not enforced and
        // never will be — which produced a warning the user could not clear by any
        // means, including dragging the mods into exactly the order the rule asks for.
        // A complaint that "A should load before B" while A does load before B is
        // simply false, whatever the sorter would do on its next run.
        foreach (var drop in sorted.DroppedForTier)
        {
            var edge = drop.Edge;
            if (position[edge.Before] < position[edge.After]) continue;
            // The rule, in the same words as a violation, and nothing about tiers. The
            // sentence said "but load-order tiers take precedence, so this rule is not
            // applied", which explained OUR MECHANISM to somebody who wanted to know
            // whether one mod loads before another. It reads as Info rather than a
            // warning: nothing is broken, and this is the residue no ordering can
            // satisfy — see IssueCodes.OrderTierOverride.
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, IssueCodes.OrderTierOverride,
                $"'{edge.Before.Display}' should load before '{edge.After.Display}'.",
                edge.Before, edge.After, edge.Provenance.DeclaredBy));
        }
    }

    private static void CheckVersions(
        IReadOnlyList<Mod> active, string? gameMajorMinor, KnownGoodDatabase? knownGood,
        ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (gameMajorMinor is null) return;

        foreach (var mod in active)
        {
            // Core/DLC track the game version implicitly; skip to avoid noise.
            if (mod.Source is ModSource.Core or ModSource.Dlc) continue;
            // A list item is not a mod (NF-10): "unsupported version" would sit amber
            // forever on something the game never loads, training people to ignore
            // the Warnings tab — the dock's own argument.
            if (mod.IsRwListItem) continue;
            if (mod.SupportedVersions.IsDefaultOrEmpty) continue; // unknown, don't guess
            if (mod.SupportedVersions.Contains(gameMajorMinor)) continue;

            // Reported working on this version despite the missing declaration (N7,
            // NoVersionWarning). The suppression is per-mod and per-version — the list
            // is fetched for gameMajorMinor — so nothing else gets quieter.
            if (knownGood is not null && knownGood.Contains(mod.PackageId)) continue;

            issues.Add(new ValidationIssue(ValidationSeverity.Warning, IssueCodes.UnsupportedVersion,
                $"'{mod.PackageId.Display}' does not list support for {gameMajorMinor} " +
                $"(supports: {string.Join(", ", mod.SupportedVersions)}).", mod.PackageId));
        }
    }

    private static void CheckReplacements(
        IReadOnlyList<Mod> mods, ImmutableArray<ModReplacement> replacements,
        string? gameMajorMinor, ImmutableArray<ValidationIssue>.Builder issues)
    {
        if (replacements.IsDefaultOrEmpty) return;

        foreach (var mod in mods)
        {
            if (mod.IsRwListItem) continue; // not a mod; see CheckVersions
            if (ReplacementMatcher.For(mod, replacements, gameMajorMinor) is not { } rule) continue;

            issues.Add(new ValidationIssue(ValidationSeverity.Warning, IssueCodes.ReplacementAvailable,
                $"'{mod.PackageId.Display}' has a maintained replacement: " +
                $"'{rule.NewName}' by {rule.NewAuthor} (Workshop {rule.NewWorkshopId}).",
                mod.PackageId));
        }
    }
}
