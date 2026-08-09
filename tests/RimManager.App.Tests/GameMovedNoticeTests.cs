using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The game-moved strip's decision logic (N5b). These tests are the reason the logic
/// lives in <see cref="GameMovedNotice"/> rather than the view model: the strip's offer,
/// words and name are the safety-relevant part, and <c>MainWindowViewModel</c> is not
/// constructible under test.
/// </summary>
public sealed class GameMovedNoticeTests
{
    private static readonly ModId Core = KnownMods.Core;
    private static readonly ModId Royalty = ModId.From("ludeon.rimworld.royalty");
    private static readonly ModId A = ModId.From("author.alpha");
    private static readonly ModId B = ModId.From("author.beta");
    private static readonly ModId C = ModId.From("author.gamma");

    private static GameMovedNotice Decide(
        DriftKind drift = DriftKind.ChangedOutsideRimManager,
        IReadOnlyList<ModId>? game = null,
        IReadOnlyList<ModId>? list = null,
        string? appliedHash = null,
        Func<ModId, bool>? installed = null)
        => GameMovedNotice.Decide(
            drift,
            game ?? [Core, A, B],
            list ?? [Core, A, B, C],
            appliedHash,
            installed ?? (_ => true));

    [Fact]
    public void Only_the_changed_outside_state_produces_a_notice()
    {
        Decide(DriftKind.InSync).Show.Should().BeFalse();
        Decide(DriftKind.PendingApply).Show.Should().BeFalse();
        // Unknown means "never applied, no evidence which side moved" — an offer to
        // adopt would present a guess as a diagnosis.
        Decide(DriftKind.Unknown).Show.Should().BeFalse();
        Decide(DriftKind.ChangedOutsideRimManager).Show.Should().BeTrue();
    }

    [Fact]
    public void The_general_case_names_the_likely_cause_and_both_counts()
    {
        var notice = Decide(game: [Core, A, B], list: [Core, A, B, C]);

        notice.IsCrashReset.Should().BeFalse();
        notice.Headline.Should().Contain("changed outside RimManager");
        notice.Detail.Should().Contain("3 active").And.Contain("4");
        notice.Detail.Should().Contain("loading a save's mod list", "the likely cause is named");
    }

    [Fact]
    public void An_order_only_change_is_distinguished_the_way_RimWorlds_own_dialog_does()
    {
        var notice = Decide(game: [Core, B, A], list: [Core, A, B]);

        notice.Detail.Should().Contain("No mods were added or removed");
        notice.Detail.Should().Contain("only the order changed");
    }

    [Fact]
    public void Game_named_mods_that_are_not_installed_are_reported_not_discovered_at_2am()
    {
        var notice = Decide(
            game: [Core, A, B], list: [Core, C],
            installed: id => id != A && id != B);

        notice.Detail.Should().Contain("2 of the game's mods are not installed");

        Decide(game: [Core, A], list: [Core, C], installed: id => id != A)
            .Detail.Should().Contain("1 of the game's mods is not installed");
    }

    [Fact]
    public void A_dirty_list_is_told_its_own_edits_are_at_stake_and_a_clean_one_is_not()
    {
        var list = new[] { Core, A, B };
        var cleanHash = ModlistDrift.HashOrder(list);

        Decide(list: list, appliedHash: cleanHash)
            .Detail.Should().NotContain("edits never applied");

        Decide(list: list, appliedHash: "0123456789abcdef")
            .Detail.Should().Contain("edits never applied");
    }

    [Fact]
    public void A_never_applied_list_falls_to_the_dirty_side_because_caution_is_the_safe_default()
    {
        Decide(list: [Core, A, B], appliedHash: null)
            .Detail.Should().Contain("edits never applied");
    }

    [Fact]
    public void IsDirty_carries_the_split_so_the_strip_can_demote_the_discarding_action()
    {
        var list = new[] { Core, A, B };
        var cleanHash = ModlistDrift.HashOrder(list);

        Decide(list: list, appliedHash: cleanHash).IsDirty.Should().BeFalse();
        Decide(list: list, appliedHash: "0123456789abcdef").IsDirty.Should().BeTrue();
        Decide(list: list, appliedHash: null).IsDirty.Should().BeTrue();

        // The crash reset never demotes: that list is untouched by construction, and
        // the strip offers Apply rather than Replace anyway.
        Decide(game: [Core], list: [Core, A, B], appliedHash: null)
            .IsDirty.Should().BeFalse();
    }

    [Fact]
    public void The_copy_states_the_two_truths_no_separators_and_a_restorable_snapshot()
    {
        var notice = Decide();

        notice.Detail.Should().Contain("no separators",
            "a flat ModsConfig.xml cannot express one, and the copy must say so");
        notice.Detail.Should().Contain("restorable snapshot",
            "Vortex cannot undo either of its choices; ours can, and the copy says so");
    }

    [Fact]
    public void A_bare_game_against_a_modded_list_is_the_crash_reset_and_offers_no_adoption()
    {
        var core = Decide(game: [Core], list: [Core, A, B, C]);
        core.IsCrashReset.Should().BeTrue();
        core.Headline.Should().Contain("reset");
        core.Detail.Should().Contain("only Core active");
        core.Detail.Should().Contain("not a decision anyone made");
        core.Detail.Should().NotContain("snapshot", "no adoption is offered, so none is promised");

        var withDlc = Decide(game: [Core, Royalty], list: [Core, A, B]);
        withDlc.IsCrashReset.Should().BeTrue();
        withDlc.Detail.Should().Contain("Core and its expansions");

        Decide(game: [], list: [Core, A]).IsCrashReset.Should().BeTrue();
    }

    [Fact]
    public void A_bare_game_against_a_bare_list_is_an_ordinary_change_not_a_crash()
    {
        // Nothing third-party on either side: "against a list with many more" is the
        // condition, and it is not met.
        var notice = Decide(game: [Core], list: [Core, Royalty]);

        notice.Show.Should().BeTrue();
        notice.IsCrashReset.Should().BeFalse();
    }

    [Fact]
    public void The_suggested_name_matches_the_snapshot_label_convention_and_stays_unique()
    {
        var now = new DateTimeOffset(2026, 8, 7, 14, 9, 0, TimeSpan.Zero);

        // The exact month text is culture-dependent, so the expectation is built with
        // the same format string the pre-apply snapshot label uses — the convention
        // being pinned is the SHAPE and the sibling relationship, not English.
        var basis = $"RimWorld · {now:d MMM HH:mm}";

        GameMovedNotice.SuggestedName(now, []).Should().Be(basis);
        GameMovedNotice.SuggestedName(now, [basis]).Should().Be($"{basis} (2)");
        GameMovedNotice.SuggestedName(now, [basis, $"{basis} (2)"]).Should().Be($"{basis} (3)");
    }
}
