using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The per-mod conflict window's content (N6b). Same live-order rules as the badge —
/// the window and the mark that opened it must not disagree about who wins.
/// </summary>
public sealed class ModConflictsPresenterTests
{
    private static readonly ModId A = ModId.From("author.alpha");
    private static readonly ModId B = ModId.From("author.beta");
    private static readonly ModId C = ModId.From("author.gamma");

    private static readonly Dictionary<ModId, string> Names = new()
    {
        [A] = "Alpha", [B] = "Beta", [C] = "Gamma",
    };

    private static ModConflict Conflict(
        ConflictKind kind, string key, params ModId[] mods)
        => new(kind, key, [.. mods], mods[^1]);

    private static ModConflict WithXml(
        string key, params (ModId Id, string? Xml)[] providers)
        => new(ConflictKind.DefOverride, key,
            [.. providers.Select(p => p.Id)], providers[^1].Id,
            Providers: [.. providers.Select(p => new ConflictProvider(p.Id, Xml: p.Xml))]);

    private static ModConflictsDetail Build(
        ModId subject, IReadOnlyList<ModId> order, params ModConflict[] conflicts)
        => ModConflictsPresenter.Build(
            Names[subject], subject, conflicts, order, Names, scanRunning: false);

    [Fact]
    public void Won_and_lost_split_by_the_current_order_and_name_the_counterpart()
    {
        var detail = Build(B, [A, B, C],
            Conflict(ConflictKind.DefOverride, "Gun_A", A, B),      // B wins over A
            Conflict(ConflictKind.DefOverride, "Gun_B", B, C));     // B loses to C

        detail.Won.Should().ContainSingle(r => r.Key == "Gun_A")
            .Which.Counterpart.Should().Be("over Alpha");
        detail.Lost.Should().ContainSingle(r => r.Key == "Gun_B")
            .Which.Counterpart.Should().Be("to Gamma · #2");
        detail.Subtitle.Should().Be("overwrites 1 · overwritten in 1");
    }

    [Fact]
    public void A_contender_no_longer_active_is_excluded_like_the_badge_excludes_it()
    {
        var detail = Build(A, [A, C],
            Conflict(ConflictKind.DefOverride, "Gun_A", A, B));

        detail.IsEmpty.Should().BeTrue("B is not loaded, so nothing contends");
        detail.EmptyText.Should().Contain("Nothing contested");
    }

    [Fact]
    public void Harmony_gets_its_own_section_with_no_winner_and_no_diff()
    {
        var detail = Build(A, [A, B],
            Conflict(ConflictKind.HarmonyPatch, "Pawn.Tick", A, B));

        detail.Harmony.Should().ContainSingle()
            .Which.Should().Match<ContestRow>(r =>
                r.Counterpart == "with Beta" && !r.CanDiff && r.Other == null);
        detail.HasLost.Should().BeFalse();
        detail.HasWon.Should().BeFalse();
    }

    [Fact]
    public void Harmless_overlaps_are_hidden_and_the_hidden_count_is_stated()
    {
        var detail = Build(A, [A, B],
            WithXml("Gun_A", (A, "<x>1</x>"), (B, "<x>1</x>")));

        detail.IsEmpty.Should().BeTrue();
        detail.HarmlessHidden.Should().Be(1);
        detail.HarmlessNote.Should().Contain("1 identical overlap hidden");
    }

    [Fact]
    public void The_scan_still_running_changes_what_empty_means()
    {
        var running = ModConflictsPresenter.Build(
            "Alpha", A, [], [A], Names, scanRunning: true);
        running.EmptyText.Should().Contain("still running");

        var done = ModConflictsPresenter.Build(
            "Alpha", A, [], [A], Names, scanRunning: false);
        done.EmptyText.Should().Contain("Nothing contested");
    }

    [Fact]
    public void The_diff_puts_the_winner_on_the_right_whichever_side_the_subject_is()
    {
        var conflict = WithXml("Gun_A", (A, "<x>old</x>"), (B, "<x>new</x>"));

        // Subject lost: subject left, winner right.
        var lostRow = Build(A, [A, B], conflict).Lost.Single();
        var lostDiff = ModConflictsPresenter.DiffFor(lostRow, _ => 1, Names)!;
        lostDiff.LeftHeader.Should().StartWith("Alpha").And.Contain("overwritten");
        lostDiff.RightHeader.Should().StartWith("Beta").And.Contain("wins");

        // Subject won: counterpart left, subject right.
        var wonRow = Build(B, [A, B], conflict).Won.Single();
        var wonDiff = ModConflictsPresenter.DiffFor(wonRow, _ => 1, Names)!;
        wonDiff.LeftHeader.Should().StartWith("Alpha").And.Contain("overwritten");
        wonDiff.RightHeader.Should().StartWith("Beta").And.Contain("wins");
    }

    [Fact]
    public void A_provider_without_xml_offers_no_diff_but_keeps_its_row()
    {
        var conflict = WithXml("Gun_A", (A, "<x>old</x>"), (B, null));

        var row = Build(A, [A, B], conflict).Lost.Single();
        row.CanDiff.Should().BeFalse("B's XML was not captured — nothing to compare");
        ModConflictsPresenter.DiffFor(row, _ => 1, Names).Should().BeNull();
    }

    /// <summary>The tab's "Make another win" lives on these rows since N6c.</summary>
    [Fact]
    public void Win_this_is_offered_on_lost_override_rows_only()
    {
        var conflict = WithXml("Gun_A", (A, "<x>1</x>"), (B, "<x>2</x>"));

        Build(A, [A, B], conflict).Lost.Single().CanWin
            .Should().BeTrue("the subject lost and can be moved below the winner");
        Build(B, [A, B], conflict).Won.Single().CanWin
            .Should().BeFalse("a won row has nothing to win");
        Build(A, [A, B], Conflict(ConflictKind.HarmonyPatch, "M", A, B)).Harmony.Single().CanWin
            .Should().BeFalse("harmony has no winner to displace");
    }

    [Fact]
    public void In_a_three_mod_chain_the_diff_counterpart_is_the_nearest_competitor()
    {
        var conflict = WithXml("Gun_A", (A, "<x>1v</x>"), (B, "<x>2v</x>"), (C, "<x>3v</x>"));

        var won = Build(C, [A, B, C], conflict).Won.Single();
        won.Other.Should().Be(B, "B's version is what would load if C moved up one place");
        won.Counterpart.Should().Be("over Alpha · Beta");

        // A losing subject diffs against the WINNER, not its neighbour: only the
        // winner's version exists in game.
        var lost = Build(A, [A, B, C], conflict).Lost.Single();
        lost.Other.Should().Be(C);
    }
}
