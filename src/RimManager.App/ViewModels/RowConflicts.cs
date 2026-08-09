using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One active row's ⚡ badge state (N6): what this mod wins and loses among the mods
/// actually loaded, and which Harmony targets it shares. §0f keeps the two
/// relationships apart — an override has a winner, a Harmony collision does not,
/// and flattening them into one number would teach the user something false.
/// </summary>
public sealed record ConflictBadge(int Wins, int OverwrittenIn, int SharedHarmony)
{
    /// <summary>Override-style involvement: something is discarded, someone won.</summary>
    public bool HasOverrideConflict => Wins > 0 || OverwrittenIn > 0;

    /// <summary>Only shared Harmony targets: nothing is discarded, every patch runs.</summary>
    public bool IsHarmonyOnly => !HasOverrideConflict && SharedHarmony > 0;

    // The bolt's colour (owner's system): yellow = overrides only, blue = harmony is
    // involved (with or without overrides — the mark beside it says which), no bolt =
    // no conflict. Exclusive by construction.
    public bool IsOverrideOnly => HasOverrideConflict && SharedHarmony == 0;
    public bool HasHarmony => SharedHarmony > 0;

    // The mark, MO2's grammar (owner's call): + overwrites only, − overwritten only,
    // ± both. Three exclusive states so the template's visibility bindings cannot
    // stack two marks in one 14px cell.
    public bool IsOverwritingOnly => Wins > 0 && OverwrittenIn == 0;
    public bool IsOverwrittenOnly => Wins == 0 && OverwrittenIn > 0;
    public bool IsMixed => Wins > 0 && OverwrittenIn > 0;

    /// <summary>
    /// The badge's tooltip — the words are the §0f distinction. "Wins/overwritten"
    /// belongs to overrides alone; Harmony gets "every patch runs", because a user
    /// taught that Harmony has losers will "fix" an order that is not broken.
    /// </summary>
    public string Tip
    {
        get
        {
            var parts = new List<string>(2);

            if (HasOverrideConflict)
            {
                var bits = new List<string>(2);
                if (Wins > 0) bits.Add($"wins {Wins}");
                if (OverwrittenIn > 0) bits.Add($"overwritten in {OverwrittenIn}");
                parts.Add($"Contested content: {string.Join(" · ", bits)} — last loaded wins");
            }

            if (SharedHarmony > 0)
            {
                var s = SharedHarmony == 1 ? "target" : "targets";
                parts.Add($"{SharedHarmony} shared Harmony {s} — every patch runs; order decides the outcome");
            }

            return string.Join("\n", parts);
        }
    }
}

/// <summary>
/// Computes each active mod's <see cref="ConflictBadge"/> from the last scan's report
/// and the <b>current</b> active order.
/// <para>
/// Winners are recomputed here rather than read from <see cref="ModConflict.Winner"/>,
/// because that field froze at scan time: a drag changes who loads last without
/// changing what contends, and the scan deliberately does not re-run per edit (N5a6
/// dropped the incremental pass on measurement). Membership is order-independent, so
/// the report stays true across reorders; only the outcome moves, and the outcome is
/// pure arithmetic over the current order.
/// </para>
/// <para>
/// A contender that is no longer active is excluded — a mod that is not loaded
/// overrides nothing and patches nothing (the same reason the badge is on active rows
/// only) — and a conflict with fewer than two live contenders is not a conflict. The
/// one staleness this cannot fix: a mod <em>activated</em> since the scan is unknown
/// to the report and shows no badge until the next scan, which is the schedule N5a
/// defined and the next reload repairs.
/// </para>
/// </summary>
public static class RowConflicts
{
    public static ImmutableDictionary<ModId, ConflictBadge> Compute(
        IEnumerable<ModConflict> conflicts, IReadOnlyList<ModId> activeOrder)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(activeOrder);

        var position = new Dictionary<ModId, int>(activeOrder.Count);
        for (var i = 0; i < activeOrder.Count; i++) position[activeOrder[i]] = i;

        var wins = new Dictionary<ModId, int>();
        var overwritten = new Dictionary<ModId, int>();
        var harmony = new Dictionary<ModId, int>();

        foreach (var conflict in conflicts)
        {
            // The tab's own default, for the same reason: identical markup everywhere
            // means the overlap changes nothing, and a badge for it would put noise on
            // hundreds of rows (214 of 252 on the design's example install).
            if (ConflictsPresenter.IsHarmless(conflict)) continue;

            var live = conflict.Mods
                .Where(position.ContainsKey)
                .Distinct()
                .OrderBy(id => position[id])
                .ToList();
            if (live.Count < 2) continue;

            if (conflict.Kind == ConflictKind.HarmonyPatch)
            {
                foreach (var id in live)
                    harmony[id] = harmony.GetValueOrDefault(id) + 1;
                continue;
            }

            var winner = live[^1];
            wins[winner] = wins.GetValueOrDefault(winner) + 1;
            foreach (var loser in live.Take(live.Count - 1))
                overwritten[loser] = overwritten.GetValueOrDefault(loser) + 1;
        }

        var badges = ImmutableDictionary.CreateBuilder<ModId, ConflictBadge>();
        foreach (var id in wins.Keys.Concat(overwritten.Keys).Concat(harmony.Keys).Distinct())
        {
            badges[id] = new ConflictBadge(
                wins.GetValueOrDefault(id),
                overwritten.GetValueOrDefault(id),
                harmony.GetValueOrDefault(id));
        }

        return badges.ToImmutable();
    }

    /// <summary>
    /// The selected mod's live relationships, for the MO2-style row highlights: colour
    /// names the OTHER row's fate against the selection. Red — the selected mod wins,
    /// that row's contested content is discarded. Green — that row wins over the
    /// selected mod's content.
    /// <para>
    /// RimWorld semantics, not pairwise file semantics: for a def, only the LAST loaded
    /// version exists, so in a three-mod chain the middle mod beats nobody — it and the
    /// first both lose to the winner, and co-losers have no relationship to paint.
    /// Harmony conflicts paint nothing: both patches run, so win/lose colours would be
    /// the exact lie §0f exists to prevent.
    /// </para>
    /// </summary>
    public static ConflictRelations RelationsFor(
        ModId selected, IEnumerable<ModConflict> conflicts, IReadOnlyList<ModId> activeOrder)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(activeOrder);

        var position = new Dictionary<ModId, int>(activeOrder.Count);
        for (var i = 0; i < activeOrder.Count; i++) position[activeOrder[i]] = i;
        if (!position.ContainsKey(selected)) return ConflictRelations.None;

        var beaten = ImmutableHashSet.CreateBuilder<ModId>();
        var beats = ImmutableHashSet.CreateBuilder<ModId>();
        var harmony = ImmutableHashSet.CreateBuilder<ModId>();

        foreach (var conflict in conflicts)
        {
            var live = conflict.Mods
                .Where(position.ContainsKey)
                .Distinct()
                .OrderBy(id => position[id])
                .ToList();
            if (live.Count < 2 || !live.Contains(selected)) continue;

            if (conflict.Kind == ConflictKind.HarmonyPatch)
            {
                // The harmony relationship is SYMMETRIC and names no winner — every
                // patch runs (§0f). The paint that follows is "linked, not ranked".
                foreach (var other in live) harmony.Add(other);
                continue;
            }

            if (ConflictsPresenter.IsHarmless(conflict)) continue;

            var winner = live[^1];
            if (winner == selected)
            {
                foreach (var loser in live.Take(live.Count - 1)) beaten.Add(loser);
            }
            else
            {
                beats.Add(winner);
            }
        }

        // Override paint wins where both apply (v2 §4A.2: actionable beats ambient) —
        // and the selected mod itself never paints.
        harmony.Remove(selected);
        harmony.ExceptWith(beaten);
        harmony.ExceptWith(beats);

        return new ConflictRelations(beaten.ToImmutable(), beats.ToImmutable(), harmony.ToImmutable());
    }
}

/// <summary>
/// <see cref="OverwrittenBySelected"/> tint red; <see cref="OverwritesSelected"/> tint
/// green; <see cref="SharesHarmonyWithSelected"/> takes the DASHED harmony edge —
/// linked, not ranked, no winner named (v2 §4A.2). The selected mod itself is in no
/// set, and a row that is both override-related and harmony-sharing shows the
/// override paint alone.
/// </summary>
public sealed record ConflictRelations(
    ImmutableHashSet<ModId> OverwrittenBySelected,
    ImmutableHashSet<ModId> OverwritesSelected,
    ImmutableHashSet<ModId> SharesHarmonyWithSelected)
{
    public static readonly ConflictRelations None = new([], [], []);
}
