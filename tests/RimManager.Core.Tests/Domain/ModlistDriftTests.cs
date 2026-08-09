using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// Telling "I have unsaved edits" apart from "RimWorld rewrote ModsConfig.xml behind me".
/// From the file alone the two look identical, and they need opposite responses.
/// </summary>
public sealed class ModlistDriftTests
{
    private static ModlistState Listing(params string[] ids) =>
        ModlistState.Empty.WithEntries(ids.Select(i => ModlistEntry.Mod(ModId.From(i))));

    private static IEnumerable<ModId> Game(params string[] ids) => ids.Select(ModId.From);

    [Fact]
    public void Matching_orders_are_in_sync()
    {
        ModlistDrift.Classify(Listing("a", "b"), Game("a", "b"), lastAppliedHash: null)
            .Should().Be(DriftKind.InSync, "no evidence is needed when they agree");
    }

    /// <summary>Load order is ordered. Same mods in a different order is a different list.</summary>
    [Fact]
    public void Reordering_is_a_difference_not_a_match()
    {
        var applied = ModlistDrift.HashOrder(Game("a", "b"));

        ModlistDrift.Classify(Listing("b", "a"), Game("a", "b"), applied)
            .Should().Be(DriftKind.PendingApply);
    }

    [Fact]
    public void The_list_moving_while_the_game_stayed_put_is_ordinary_unsaved_work()
    {
        var applied = ModlistDrift.HashOrder(Game("a", "b"));

        ModlistDrift.Classify(Listing("a", "b", "c"), Game("a", "b"), applied)
            .Should().Be(DriftKind.PendingApply, "the commit bar already owns this state");
    }

    /// <summary>
    /// The case this exists for: the player loaded a save and accepted "use this save's
    /// mod list", so RimWorld rewrote the file. The next Apply would silently discard it.
    /// </summary>
    [Fact]
    public void The_game_moving_away_from_what_we_applied_is_external_drift()
    {
        var applied = ModlistDrift.HashOrder(Game("a", "b"));

        ModlistDrift.Classify(Listing("a", "b"), Game("a", "b", "c"), applied)
            .Should().Be(DriftKind.ChangedOutsideRimManager);
    }

    [Fact]
    public void Never_applied_means_no_evidence_about_which_one_moved()
    {
        ModlistDrift.Classify(Listing("a"), Game("b"), lastAppliedHash: null)
            .Should().Be(DriftKind.Unknown, "reported as a question, never an accusation");
    }

    [Fact]
    public void Disabled_mods_are_not_part_of_the_comparison()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Mod(ModId.From("a")),
            ModlistEntry.Mod(ModId.From("off"), enabled: false),
        ]);

        ModlistDrift.Classify(state, Game("a"), lastAppliedHash: null)
            .Should().Be(DriftKind.InSync, "the game only ever sees the enabled set");
    }

    [Fact]
    public void Separators_are_not_part_of_the_comparison()
    {
        var state = ModlistState.Empty.WithEntries(
        [
            ModlistEntry.Separator("s", "Frameworks"),
            ModlistEntry.Mod(ModId.From("a")),
        ]);

        ModlistDrift.Classify(state, Game("a"), lastAppliedHash: null)
            .Should().Be(DriftKind.InSync, "ModsConfig.xml has no concept of a separator");
    }

    [Fact]
    public void PackageId_comparison_is_case_insensitive()
    {
        ModlistDrift.Classify(Listing("Brrainz.Harmony"), Game("brrainz.harmony"), null)
            .Should().Be(DriftKind.InSync, "packageId identity always routes through ModId");
    }

    [Fact]
    public void The_hash_is_stable_and_order_sensitive()
    {
        ModlistDrift.HashOrder(Game("a", "b")).Should().Be(ModlistDrift.HashOrder(Game("a", "b")));
        ModlistDrift.HashOrder(Game("a", "b")).Should().NotBe(ModlistDrift.HashOrder(Game("b", "a")));
    }

    // --- which hash is the evidence -----------------------------------------

    private static Modlist Applied(string name, string hash, int hoursAgo) => new()
    {
        Id = name,
        Name = name,
        State = ModlistState.Empty,
        LastAppliedHash = hash,
        LastAppliedUtc = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero).AddHours(-hoursAgo),
    };

    /// <summary>
    /// The measured case, from a real install. Two lists, both applied at different times;
    /// selecting the older one compared the game against <b>its</b> stamp and cried
    /// <see cref="DriftKind.ChangedOutsideRimManager"/> — blaming RimWorld for a file
    /// RimManager itself had rewritten hours earlier by applying the other list.
    /// </summary>
    [Fact]
    public void Switching_to_an_older_list_is_pending_work_not_an_outside_change()
    {
        var vanilla = ModlistDrift.HashOrder(Game("core"));
        var plus = ModlistDrift.HashOrder(Game("core", "harmony"));

        var lists = new[] { Applied("Vanilla", vanilla, hoursAgo: 14), Applied("Vanilla Plus", plus, hoursAgo: 1) };

        // The game holds Vanilla Plus's order; the user selects Vanilla.
        var evidence = ModlistDrift.LastWrittenToGame(lists);
        evidence.Should().Be(plus, "the most recently applied list is where RimManager left the game");

        ModlistDrift.Classify(Listing("core"), Game("core", "harmony"), evidence)
            .Should().Be(DriftKind.PendingApply,
                "RimManager put the game there, so the thing that has not happened is "
                + "applying THIS list — nothing external touched anything");
    }

    /// <summary>And the real warning still fires: the game moved away from every stamp.</summary>
    [Fact]
    public void An_order_matching_no_stamp_is_still_an_outside_change()
    {
        var lists = new[]
        {
            Applied("Vanilla", ModlistDrift.HashOrder(Game("core")), hoursAgo: 14),
            Applied("Vanilla Plus", ModlistDrift.HashOrder(Game("core", "harmony")), hoursAgo: 1),
        };

        ModlistDrift.Classify(
                Listing("core"), Game("core", "harmony", "surprise"),
                ModlistDrift.LastWrittenToGame(lists))
            .Should().Be(DriftKind.ChangedOutsideRimManager,
                "RimWorld accepting a save's mod list is exactly this, and it must not be "
                + "drowned out by the switching case");
    }

    [Fact]
    public void A_list_that_was_never_applied_carries_no_evidence()
    {
        var never = new Modlist { Id = "n", Name = "New list", State = ModlistState.Empty };

        ModlistDrift.LastWrittenToGame([never]).Should().BeNull();
        ModlistDrift.LastWrittenToGame([]).Should().BeNull();

        // An unstamped list must not outrank a stamped one just by coming later.
        ModlistDrift.LastWrittenToGame([Applied("old", "AAAA", hoursAgo: 9), never])
            .Should().Be("AAAA");
    }

    [Fact]
    public void Only_the_two_states_worth_a_sentence_get_one()
    {
        ModlistDrift.Describe(DriftKind.InSync, 5, 5).Should().BeEmpty();
        ModlistDrift.Describe(DriftKind.PendingApply, 5, 6).Should().BeEmpty(
            "the commit bar already says this, and saying it twice is two things to reconcile");

        ModlistDrift.Describe(DriftKind.ChangedOutsideRimManager, 543, 202)
            .Should().Contain("543").And.Contain("202").And.Contain("outside RimManager");
        ModlistDrift.Describe(DriftKind.Unknown, 543, 202).Should().Contain("never been applied");
    }
}
