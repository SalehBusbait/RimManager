using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Domain;
using RimManager.Core.Validation;

namespace RimManager.App.ViewModels;

/// <summary>What can be done about one unmet dependency — the state decides the buttons.</summary>
public enum DependencyState
{
    /// <summary>On disk but not in the load order. One click fixes it.</summary>
    InstalledButInactive,

    /// <summary>Not on disk. Needs downloading or subscribing first.</summary>
    NotInstalled,

    /// <summary>An official DLC the user does not own. Nothing RimManager can do.</summary>
    DlcNotOwned,

    /// <summary>An official DLC that is owned but not enabled.</summary>
    DlcNotActive,
}

/// <summary>One card in the resolver (<c>2i</c>-4).</summary>
/// <param name="PackageId">The dependency's id — the identity, always shown.</param>
/// <param name="DisplayName">Its name if the requiring mod declared one, else the id.</param>
/// <param name="RequiredBy">Every active mod that wants it. Plural matters: resolving one
/// dependency often clears several warnings at once, and the card should say so.</param>
/// <param name="WorkshopId">Its Workshop id, when the About.xml gave a link to parse.</param>
public sealed record DependencyCard(
    ModId PackageId,
    string DisplayName,
    DependencyState State,
    ImmutableArray<string> RequiredBy,
    string? WorkshopId = null)
{
    /// <summary>"Required by Vanilla Expanded Framework and 2 others".</summary>
    public string RequiredByText => RequiredBy.Length switch
    {
        0 => "Required by an active mod",
        1 => $"Required by {RequiredBy[0]}",
        2 => $"Required by {RequiredBy[0]} and 1 other",
        _ => $"Required by {RequiredBy[0]} and {RequiredBy.Length - 1} others",
    };

    /// <summary>What the card says is wrong, in the state's own terms.</summary>
    public string StateText => State switch
    {
        DependencyState.InstalledButInactive => "Installed, but not in the load order",
        DependencyState.NotInstalled => "Not installed",
        DependencyState.DlcNotOwned => "Official DLC — not owned",
        DependencyState.DlcNotActive => "Official DLC — owned, but not enabled",
        _ => string.Empty,
    };

    /// <summary>Only the inactive case is one click away.</summary>
    public bool CanActivate => State is DependencyState.InstalledButInactive;

    /// <summary>Downloading only makes sense for something that is not on disk and is not DLC.</summary>
    public bool CanDownload => State is DependencyState.NotInstalled && WorkshopId is not null;

    /// <summary>Open on Workshop needs an id to open.</summary>
    public bool CanOpenWorkshop => WorkshopId is not null;

    /// <summary>
    /// DLC is bought from Steam, not downloaded by us, and enabling it is RimWorld's own
    /// business. Saying so beats offering a button that cannot work.
    /// </summary>
    public string? Unactionable => State switch
    {
        DependencyState.DlcNotOwned =>
            "RimManager cannot buy DLC. Buy it on Steam, then it appears here.",
        DependencyState.DlcNotActive =>
            "Enable it from RimWorld's own mod list — DLC is not something RimManager activates.",
        DependencyState.NotInstalled when WorkshopId is null =>
            "No Workshop link was declared, so there is nowhere to send you. Search for it by name.",
        _ => null,
    };
}

/// <summary>
/// Turns validation's missing-dependency issues into the resolver's cards (<c>2i</c>-4).
/// <para>
/// Pure, and it is the part worth testing: the same dependency is usually reported once per
/// mod that wants it, so a naive list shows "Harmony" four times and makes four warnings look
/// like four problems.
/// </para>
/// </summary>
public static class DependencyResolver
{
    public static ImmutableArray<DependencyCard> Plan(
        IEnumerable<ValidationIssue> issues,
        IReadOnlyDictionary<ModId, Mod> installed,
        IReadOnlySet<ModId> active,
        IReadOnlyCollection<ModId> ownedExpansions,
        IReadOnlyDictionary<ModId, ModDependency> declared)
    {
        var byDependency = new Dictionary<ModId, List<string>>();

        foreach (var issue in issues)
        {
            if (issue.Code is not (IssueCodes.MissingDependency or IssueCodes.MissingDlc
                or IssueCodes.DependencyInactive)) continue;
            if (issue.Related is not { } needed) continue;

            // Grouped by the DEPENDENCY, not by the mod that reported it: one missing
            // Harmony is one card, however many mods asked for it.
            if (!byDependency.TryGetValue(needed, out var requiredBy))
            {
                byDependency[needed] = requiredBy = [];
            }

            if (issue.Subject is { } requester)
            {
                var name = installed.TryGetValue(requester, out var mod) ? mod.Name : requester.Display;
                if (!requiredBy.Contains(name)) requiredBy.Add(name);
            }
        }

        return
        [
            .. byDependency
                .Select(pair => Card(pair.Key, pair.Value, installed, active, ownedExpansions, declared))
                // Actionable first: a list that opens with three DLC you do not own reads
                // as "nothing can be done here".
                .OrderByDescending(c => c.CanActivate)
                .ThenByDescending(c => c.CanDownload)
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static DependencyCard Card(
        ModId needed,
        List<string> requiredBy,
        IReadOnlyDictionary<ModId, Mod> installed,
        IReadOnlySet<ModId> active,
        IReadOnlyCollection<ModId> ownedExpansions,
        IReadOnlyDictionary<ModId, ModDependency> declared)
    {
        declared.TryGetValue(needed, out var dependency);
        var name = dependency?.DisplayName ?? (installed.TryGetValue(needed, out var mod)
            ? mod.Name
            : needed.Display);

        var isDlc = needed == KnownMods.Core || KnownMods.IsOfficialDlc(needed);

        var state = isDlc
            ? (ownedExpansions.Contains(needed) ? DependencyState.DlcNotActive : DependencyState.DlcNotOwned)
            : installed.ContainsKey(needed) && !active.Contains(needed)
                ? DependencyState.InstalledButInactive
                : DependencyState.NotInstalled;

        return new DependencyCard(
            needed, name, state, [.. requiredBy], WorkshopId(dependency, installed, needed));
    }

    /// <summary>
    /// The Workshop id, from the installed copy if we have one or from the link the
    /// requiring mod declared. About.xml writes these three ways, so all three are tried.
    /// </summary>
    private static string? WorkshopId(
        ModDependency? dependency,
        IReadOnlyDictionary<ModId, Mod> installed,
        ModId needed)
    {
        if (installed.TryGetValue(needed, out var mod) && mod.PublishedFileId is { } id) return id;

        foreach (var url in new[] { dependency?.SteamWorkshopUrl, dependency?.DownloadUrl })
        {
            if (Core.Workshop.WorkshopUrl.TryGetId(url, out var parsed)) return parsed;
        }

        return null;
    }

    /// <summary>The footer's summary: how much of this the resolver can actually fix.</summary>
    public static string Summary(IReadOnlyCollection<DependencyCard> cards)
    {
        if (cards.Count == 0) return "Nothing to resolve.";

        var activatable = cards.Count(c => c.CanActivate);
        var downloadable = cards.Count(c => c.CanDownload);
        var stuck = cards.Count - activatable - downloadable;

        var parts = new List<string>();
        if (activatable > 0) parts.Add($"{activatable} can be activated");
        if (downloadable > 0) parts.Add($"{downloadable} can be downloaded");
        if (stuck > 0) parts.Add($"{stuck} need you");

        return $"{cards.Count} unmet · {string.Join(" · ", parts)}";
    }
}
