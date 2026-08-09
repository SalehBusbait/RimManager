using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of the per-mod conflict window (N6b): a contested key, and who is on the
/// other side of it. <see cref="Other"/> is the diff counterpart — the winner when the
/// subject lost, the nearest competitor when it won; null for Harmony rows, which have
/// no XML and no winner to compare against.
/// </summary>
public sealed record ContestRow(
    ConflictKind Kind,
    string KindLabel,
    string Key,
    string Counterpart,
    ModConflict Source,
    ModId Subject,
    ModId? Other,
    bool CanDiff,
    bool SubjectWins)
{
    /// <summary>
    /// "Win this" is offered on lost override rows only — a won row has nothing to
    /// win, and Harmony has no winner to displace (§0f).
    /// </summary>
    public bool CanWin => !SubjectWins && Other is not null;
}

/// <summary>Everything the window shows, built once at open — a snapshot, like the
/// dialogs it follows.</summary>
public sealed record ModConflictsDetail(
    string Title,
    string Subtitle,
    ImmutableArray<ContestRow> Lost,
    ImmutableArray<ContestRow> Won,
    ImmutableArray<ContestRow> Harmony,
    int HarmlessHidden,
    string EmptyText)
{
    public bool HasLost => !Lost.IsEmpty;
    public bool HasWon => !Won.IsEmpty;
    public bool HasHarmony => !Harmony.IsEmpty;
    public bool IsEmpty => !HasLost && !HasWon && !HasHarmony;

    // The header's inline badge (S-CONFLICTS: "mod name + inline badge (bolt(s) + ±)")
    // — the same two-channel grammar the row wears, so the window shows the mark it
    // was opened FROM. Amber bolt for override contests, harmony-blue when patches
    // are shared; + while this mod overwrites, − while it is overwritten.
    public bool HasOverrideContest => HasLost || HasWon;

    // T6, S-CONFLICTS: the fixed section order is OVERWRITTEN → OVERWRITES → HARMONY,
    // and the headings use the sections' own verbs so the subtitle, the headings and
    // the row copy all speak one vocabulary ("wins" was the odd one out).
    public string LostHeading => $"OVERWRITTEN — {Lost.Length}";
    public string WonHeading => $"OVERWRITES — {Won.Length}";
    public string HarmonyHeading => $"HARMONY — {Harmony.Length}";

    /// <summary>The hidden-count rule from 2c: a filter that hides must say how much.</summary>
    public string HarmlessNote => HarmlessHidden switch
    {
        0 => string.Empty,
        1 => "1 identical overlap hidden — every provider ships the same markup",
        var n => $"{n} identical overlaps hidden — every provider ships the same markup",
    };
}

/// <summary>
/// Builds the per-mod conflict window's content (N6b) — Avalonia-free, where the tests
/// are. The same live-order arithmetic as <see cref="RowConflicts"/>: membership from
/// the scan, winners from the CURRENT order, contenders no longer active excluded.
/// §0f carries through in the sections themselves — won and lost exist only for
/// override kinds, and Harmony is its own section with its own sentence, never a
/// winner.
/// </summary>
public static class ModConflictsPresenter
{
    public static ModConflictsDetail Build(
        string subjectName,
        ModId subject,
        IEnumerable<ModConflict> conflicts,
        IReadOnlyList<ModId> activeOrder,
        IReadOnlyDictionary<ModId, string> names,
        bool scanRunning)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(activeOrder);
        ArgumentNullException.ThrowIfNull(names);

        var position = new Dictionary<ModId, int>(activeOrder.Count);
        for (var i = 0; i < activeOrder.Count; i++) position[activeOrder[i]] = i;

        var lost = new List<ContestRow>();
        var won = new List<ContestRow>();
        var harmony = new List<ContestRow>();
        var harmlessHidden = 0;

        foreach (var conflict in conflicts)
        {
            var live = conflict.Mods
                .Where(position.ContainsKey)
                .Distinct()
                .OrderBy(id => position[id])
                .ToList();
            if (live.Count < 2 || !live.Contains(subject)) continue;

            if (ConflictsPresenter.IsHarmless(conflict))
            {
                harmlessHidden++;
                continue;
            }

            if (conflict.Kind == ConflictKind.HarmonyPatch)
            {
                var others = live.Where(id => id != subject).Select(id => NameOf(id, names));
                harmony.Add(new ContestRow(
                    conflict.Kind, ConflictsPresenter.KindLabel(conflict.Kind), conflict.Key,
                    $"with {string.Join(" · ", others)}",
                    conflict, subject, Other: null, CanDiff: false, SubjectWins: false));
                continue;
            }

            var winner = live[^1];
            if (winner == subject)
            {
                // The diff counterpart is the NEAREST competitor: the version that
                // would load if this mod moved up one place in the chain.
                var other = live[^2];
                var losers = live.Take(live.Count - 1).Select(id => NameOf(id, names));
                won.Add(new ContestRow(
                    conflict.Kind, ConflictsPresenter.KindLabel(conflict.Kind), conflict.Key,
                    $"over {string.Join(" · ", losers)}",
                    conflict, subject, other, CanDiff(conflict, subject, other), SubjectWins: true));
            }
            else
            {
                var text = position.TryGetValue(winner, out var p)
                    ? $"to {NameOf(winner, names)} · #{p}"
                    : $"to {NameOf(winner, names)}";
                lost.Add(new ContestRow(
                    conflict.Kind, ConflictsPresenter.KindLabel(conflict.Kind), conflict.Key,
                    text, conflict, subject, winner, CanDiff(conflict, subject, winner),
                    SubjectWins: false));
            }
        }

        var subtitleParts = new List<string>(3);
        if (won.Count > 0) subtitleParts.Add($"overwrites {won.Count}");
        if (lost.Count > 0) subtitleParts.Add($"overwritten in {lost.Count}");
        if (harmony.Count > 0)
        {
            subtitleParts.Add(
                $"shares {harmony.Count} Harmony target{(harmony.Count == 1 ? "" : "s")}");
        }

        return new ModConflictsDetail(
            subjectName,
            subtitleParts.Count == 0 ? "no live conflicts" : string.Join(" · ", subtitleParts),
            [.. Ordered(lost)],
            [.. Ordered(won)],
            [.. harmony.OrderBy(r => r.Key, StringComparer.Ordinal)],
            harmlessHidden,
            EmptyText(scanRunning));
    }

    /// <summary>
    /// The two-up diff for a row: the winner's version on the right, whichever side the
    /// subject is on. Null when either side's XML was not captured — textures, or an
    /// unreadable file — and the button that calls this is hidden by
    /// <see cref="ContestRow.CanDiff"/> for the same reason.
    /// </summary>
    public static XmlDiffViewModel? DiffFor(
        ContestRow row,
        Func<ModId, int?> positionOf,
        IReadOnlyDictionary<ModId, string> names)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Other is not { } other) return null;

        var subjectXml = ProviderXml(row.Source, row.Subject);
        var otherXml = ProviderXml(row.Source, other);
        if (subjectXml is null || otherXml is null) return null;

        var (leftId, leftXml, rightId, rightXml) = row.SubjectWins
            ? (other, otherXml, row.Subject, subjectXml)
            : (row.Subject, subjectXml, other, otherXml);

        return new XmlDiffViewModel(
            row.Key,
            $"{row.KindLabel} · {NameOf(row.Subject, names)} vs {NameOf(other, names)}",
            Header(leftId, "overwritten", positionOf, names),
            Header(rightId, "wins", positionOf, names),
            leftXml,
            rightXml);
    }

    private static string? ProviderXml(ModConflict conflict, ModId id) =>
        conflict.ProvidersOrEmpty.FirstOrDefault(p => p.ModId == id)?.Xml;

    private static bool CanDiff(ModConflict conflict, ModId subject, ModId other) =>
        ProviderXml(conflict, subject) is not null && ProviderXml(conflict, other) is not null;

    private static IEnumerable<ContestRow> Ordered(IEnumerable<ContestRow> rows) =>
        rows.OrderBy(r => Priority(r.Kind)).ThenBy(r => r.Key, StringComparer.Ordinal);

    private static int Priority(ConflictKind kind) => kind switch
    {
        ConflictKind.DefOverride => 0,
        ConflictKind.PatchCollision => 1,
        _ => 2,
    };

    private static string EmptyText(bool scanRunning) => scanRunning
        ? "The conflict scan is still running — reopen this window when it finishes."
        : "Nothing contested. Among the mods currently loaded, nothing overrides this "
          + "mod's content, it overrides nobody's, and it shares no Harmony patch targets.";

    private static string Header(
        ModId id, string state, Func<ModId, int?> positionOf,
        IReadOnlyDictionary<ModId, string> names)
    {
        var name = NameOf(id, names);
        return positionOf(id) is { } p ? $"{name}   #{p} · {state}" : $"{name}   · {state}";
    }

    private static string NameOf(ModId id, IReadOnlyDictionary<ModId, string> names) =>
        names.TryGetValue(id, out var name) ? name : id.Display;
}
