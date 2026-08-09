using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The ⚡ badge's arithmetic (N6). The load-bearing property: winners are recomputed
/// from the CURRENT order, never read from <see cref="ModConflict.Winner"/> — the scan
/// froze that at scan time, a drag changes who loads last, and re-running Cecil per
/// drag was measured out in N5a6.
/// </summary>
public sealed class RowConflictsTests
{
    private static readonly ModId A = ModId.From("author.alpha");
    private static readonly ModId B = ModId.From("author.beta");
    private static readonly ModId C = ModId.From("author.gamma");

    private static ModConflict Conflict(
        ConflictKind kind, string key, ModId winner, params ModId[] mods)
        => new(kind, key, [.. mods], winner);

    [Fact]
    public void The_last_loaded_mod_wins_and_everyone_before_it_is_overwritten()
    {
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B, C) };
        var badges = RowConflicts.Compute(conflicts, [A, B, C]);

        badges[C].Should().Be(new ConflictBadge(Wins: 1, OverwrittenIn: 0, SharedHarmony: 0));
        badges[A].Should().Be(new ConflictBadge(Wins: 0, OverwrittenIn: 1, SharedHarmony: 0));
        badges[B].Should().Be(new ConflictBadge(Wins: 0, OverwrittenIn: 1, SharedHarmony: 0));
    }

    [Fact]
    public void The_winner_is_recomputed_from_the_current_order_not_the_scan_stamp()
    {
        // The scan said B won. The user has since dragged A below B — A wins now,
        // whatever the report's frozen Winner field claims.
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B) };
        var badges = RowConflicts.Compute(conflicts, [B, A]);

        badges[A].Wins.Should().Be(1);
        badges[B].OverwrittenIn.Should().Be(1);
    }

    [Fact]
    public void A_deactivated_contender_is_excluded_and_two_mods_must_remain()
    {
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B) };

        // B was deactivated since the scan: A contends with nothing that is loaded.
        RowConflicts.Compute(conflicts, [A, C]).Should().BeEmpty();

        // C was never part of this conflict and gets nothing either way.
        RowConflicts.Compute(conflicts, [A, B, C]).Should().NotContainKey(C);
    }

    [Fact]
    public void Harmony_is_counted_apart_and_names_no_winner()
    {
        var conflicts = new[] { Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", B, A, B) };
        var badges = RowConflicts.Compute(conflicts, [A, B]);

        badges[A].Should().Be(new ConflictBadge(Wins: 0, OverwrittenIn: 0, SharedHarmony: 1));
        badges[B].Should().Be(new ConflictBadge(Wins: 0, OverwrittenIn: 0, SharedHarmony: 1));
        badges[A].IsHarmonyOnly.Should().BeTrue();
        badges[A].HasOverrideConflict.Should().BeFalse();
    }

    [Fact]
    public void A_mod_can_carry_both_relationships_and_the_tip_keeps_their_words_apart()
    {
        var conflicts = new[]
        {
            Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B),
            Conflict(ConflictKind.TextureCollision, "Things/Gun", A, B, A),
            Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", B, A, B),
        };
        var badges = RowConflicts.Compute(conflicts, [A, B]);

        badges[A].Should().Be(new ConflictBadge(Wins: 0, OverwrittenIn: 2, SharedHarmony: 1));
        badges[B].Should().Be(new ConflictBadge(Wins: 2, OverwrittenIn: 0, SharedHarmony: 1));

        // §0f: override grammar and Harmony grammar never mix into one clause.
        badges[B].Tip.Should().Contain("wins 2").And.Contain("last loaded wins");
        badges[B].Tip.Should().Contain("every patch runs");
        badges[B].Tip.Should().NotContain("wins 3", "the Harmony target must not be counted as a win");
    }

    [Fact]
    public void Harmless_conflicts_put_no_badge_on_anyone()
    {
        // Identical markup on both sides — the overlap changes nothing, the same
        // default the Conflicts surface ships with (214 of 252 on the design install).
        var harmless = new ModConflict(
            ConflictKind.DefOverride, "Gun_A", [A, B], B,
            Providers: [new ConflictProvider(A, Xml: "<x>1</x>"), new ConflictProvider(B, Xml: "<x>1</x>")]);

        RowConflicts.Compute([harmless], [A, B]).Should().BeEmpty();

        // But "we could not tell" is not harmless: a provider with no XML keeps the badge.
        var unknown = new ModConflict(
            ConflictKind.DefOverride, "Gun_A", [A, B], B,
            Providers: [new ConflictProvider(A, Xml: "<x>1</x>"), new ConflictProvider(B)]);

        RowConflicts.Compute([unknown], [A, B]).Should().ContainKey(A);
    }

    [Fact]
    public void An_empty_report_or_empty_order_yields_no_badges()
    {
        RowConflicts.Compute([], [A, B]).Should().BeEmpty();
        RowConflicts.Compute(
            [Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B)],
            []).Should().BeEmpty();
    }

    [Fact]
    public void The_mark_is_plus_minus_or_both_and_the_states_are_exclusive()
    {
        new ConflictBadge(Wins: 2, OverwrittenIn: 0, SharedHarmony: 0)
            .Should().Match<ConflictBadge>(b => b.IsOverwritingOnly && !b.IsOverwrittenOnly && !b.IsMixed);
        new ConflictBadge(Wins: 0, OverwrittenIn: 3, SharedHarmony: 1)
            .Should().Match<ConflictBadge>(b => b.IsOverwrittenOnly && !b.IsOverwritingOnly && !b.IsMixed);
        new ConflictBadge(Wins: 1, OverwrittenIn: 1, SharedHarmony: 0)
            .Should().Match<ConflictBadge>(b => b.IsMixed && !b.IsOverwritingOnly && !b.IsOverwrittenOnly);
        // Harmony-only carries no override mark at all.
        new ConflictBadge(Wins: 0, OverwrittenIn: 0, SharedHarmony: 2)
            .Should().Match<ConflictBadge>(b => b.IsHarmonyOnly && !b.IsOverwritingOnly && !b.IsOverwrittenOnly && !b.IsMixed);
    }

    [Fact]
    public void The_bolt_colour_channel_is_exclusive_yellow_overrides_only_blue_when_harmony_is_involved()
    {
        // Yellow: overrides, no harmony.
        new ConflictBadge(Wins: 1, OverwrittenIn: 0, SharedHarmony: 0)
            .Should().Match<ConflictBadge>(b => b.IsOverrideOnly && !b.HasHarmony);

        // Blue: harmony present — with or without overrides, the mark says which.
        new ConflictBadge(Wins: 0, OverwrittenIn: 0, SharedHarmony: 1)
            .Should().Match<ConflictBadge>(b => b.HasHarmony && !b.IsOverrideOnly);
        new ConflictBadge(Wins: 2, OverwrittenIn: 1, SharedHarmony: 3)
            .Should().Match<ConflictBadge>(b => b.HasHarmony && !b.IsOverrideOnly && b.IsMixed);
    }

    // --- selection-relative highlights (the MO2 interaction) -----------------

    [Fact]
    public void Selecting_the_winner_paints_every_live_loser_red_and_nothing_green()
    {
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", C, A, B, C) };
        var relations = RowConflicts.RelationsFor(C, conflicts, [A, B, C]);

        relations.OverwrittenBySelected.Should().BeEquivalentTo([A, B]);
        relations.OverwritesSelected.Should().BeEmpty();
    }

    [Fact]
    public void Selecting_a_loser_paints_only_the_winner_green_because_co_losers_beat_nobody()
    {
        // RimWorld keeps ONE def: in A→B→C only C's version exists. B beats nobody —
        // it and A both lose to C — so selecting A must not paint B at all.
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", C, A, B, C) };
        var relations = RowConflicts.RelationsFor(A, conflicts, [A, B, C]);

        relations.OverwritesSelected.Should().BeEquivalentTo([C]);
        relations.OverwrittenBySelected.Should().BeEmpty();
    }

    [Fact]
    public void The_highlights_follow_the_current_order_not_the_scan_stamp()
    {
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B) };

        RowConflicts.RelationsFor(A, conflicts, [B, A])
            .OverwrittenBySelected.Should().BeEquivalentTo([B], "A loads last now, whatever the scan said");
        RowConflicts.RelationsFor(A, conflicts, [A, B])
            .OverwritesSelected.Should().BeEquivalentTo([B]);
    }

    [Fact]
    public void Harmony_paints_the_dashed_linked_state_never_win_lose()
    {
        // v2 §4A.2: the harmony relationship IS painted now — but as its own
        // linked-not-ranked state, never through the win/lose sets, because both
        // patches genuinely run and a winner would be a lie (§0f).
        var conflicts = new[] { Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", B, A, B) };
        var relations = RowConflicts.RelationsFor(A, conflicts, [A, B]);

        relations.OverwritesSelected.Should().BeEmpty();
        relations.OverwrittenBySelected.Should().BeEmpty();
        relations.SharesHarmonyWithSelected.Should().BeEquivalentTo([B]);
    }

    [Fact]
    public void Override_paint_wins_where_a_row_is_both_override_related_and_harmony_sharing()
    {
        // The same pair contests a def AND shares a Harmony target: the actionable
        // relationship (who wins the def) takes the paint; the row's blue bolt
        // still says harmony (v2 §4A.2).
        var conflicts = new[]
        {
            Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B),
            Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", B, A, B),
        };
        var relations = RowConflicts.RelationsFor(A, conflicts, [A, B]);

        relations.OverwritesSelected.Should().BeEquivalentTo([B]);
        relations.SharesHarmonyWithSelected.Should().BeEmpty(
            "a row cannot carry two relationship paints at once");
    }

    [Fact]
    public void An_inactive_harmony_partner_paints_nothing()
    {
        var conflicts = new[] { Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", B, A, B) };

        RowConflicts.RelationsFor(A, conflicts, [A, C])
            .SharesHarmonyWithSelected.Should().BeEmpty("B is not active — it patches nothing");
    }

    [Fact]
    public void A_selected_mod_outside_the_active_list_or_the_conflict_paints_nothing()
    {
        var conflicts = new[] { Conflict(ConflictKind.DefOverride, "Gun_A", B, A, B) };

        // C is active but party to nothing.
        var uninvolved = RowConflicts.RelationsFor(C, conflicts, [A, B, C]);
        uninvolved.OverwrittenBySelected.Should().BeEmpty();
        uninvolved.OverwritesSelected.Should().BeEmpty();

        // C is not even active.
        RowConflicts.RelationsFor(C, conflicts, [A, B])
            .Should().BeSameAs(ConflictRelations.None);
    }
}
