using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.Core.Tests.Workshop;

/// <summary>Update snooze, the three options offered in the Updates dock tab (2b).</summary>
public sealed class UpdateSnoozeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly ModId Mod = ModId.From("brrainz.cameraplus");

    [Fact]
    public void One_week_snooze_expires_after_seven_days()
    {
        var snooze = new ModSnooze(Mod, SnoozeKind.OneWeek, Now);

        snooze.IsActive(Now.AddDays(6), "3.5.1", "1.6").Should().BeTrue();
        snooze.IsActive(Now.AddDays(8), "3.5.1", "1.6").Should().BeFalse();
    }

    [Fact]
    public void Until_next_version_lasts_while_the_offered_version_is_unchanged()
    {
        var snooze = new ModSnooze(Mod, SnoozeKind.UntilNextVersion, Now, AtModVersion: "3.5.1");

        snooze.IsActive(Now.AddYears(1), "3.5.1", "1.6").Should().BeTrue("same version still on offer");
        snooze.IsActive(Now, "3.6.0", "1.6").Should().BeFalse("a newer version ends the snooze");
    }

    /// <summary>2b calls this "the one people actually want".</summary>
    [Fact]
    public void Until_next_game_version_lasts_until_RimWorld_moves_on()
    {
        var snooze = new ModSnooze(Mod, SnoozeKind.UntilNextGameVersion, Now, AtGameVersion: "1.6");

        snooze.IsActive(Now.AddYears(1), "9.9.9", "1.6").Should().BeTrue();
        snooze.IsActive(Now, "3.5.1", "1.7").Should().BeFalse();
    }

    /// <summary>
    /// A snooze with nothing recorded to compare against cannot expire by comparison.
    /// Treating it as spent is the safe reading — the alternative hides an update
    /// forever with no way for the user to discover why.
    /// </summary>
    [Theory]
    [InlineData(SnoozeKind.UntilNextVersion)]
    [InlineData(SnoozeKind.UntilNextGameVersion)]
    public void A_snooze_with_no_recorded_version_is_already_spent(SnoozeKind kind) =>
        new ModSnooze(Mod, kind, Now).IsActive(Now, "1.0", "1.6").Should().BeFalse();

    [Fact]
    public void Version_comparison_ignores_case() =>
        new ModSnooze(Mod, SnoozeKind.UntilNextVersion, Now, AtModVersion: "1.7.0-RC1")
            .IsActive(Now, "1.7.0-rc1", "1.6").Should().BeTrue();

    // --- the set -------------------------------------------------------------

    [Fact]
    public void Snoozing_twice_replaces_rather_than_stacks()
    {
        var set = SnoozeSet.Empty
            .With(new ModSnooze(Mod, SnoozeKind.OneWeek, Now))
            .With(new ModSnooze(Mod, SnoozeKind.UntilNextGameVersion, Now, AtGameVersion: "1.6"));

        set.Entries.Should().ContainSingle();
        set.For(Mod)!.Kind.Should().Be(SnoozeKind.UntilNextGameVersion);
    }

    [Fact]
    public void Without_unsnoozes_only_the_named_mod()
    {
        var other = ModId.From("jaxe.rimhud");
        var set = SnoozeSet.Empty
            .With(new ModSnooze(Mod, SnoozeKind.OneWeek, Now))
            .With(new ModSnooze(other, SnoozeKind.OneWeek, Now))
            .Without(Mod);

        set.For(Mod).Should().BeNull();
        set.For(other).Should().NotBeNull();
    }

    [Fact]
    public void IsSnoozed_is_false_for_a_mod_that_was_never_snoozed() =>
        SnoozeSet.Empty.IsSnoozed(Mod, Now, "1.0", "1.6").Should().BeFalse();

    /// <summary>
    /// Spent entries are dropped rather than left to accumulate: a stale one is
    /// invisible in the UI but would still match by packageId if the mod came back.
    /// </summary>
    [Fact]
    public void Prune_drops_spent_snoozes_and_keeps_live_ones()
    {
        var live = ModId.From("jaxe.rimhud");
        var set = SnoozeSet.Empty
            .With(new ModSnooze(Mod, SnoozeKind.OneWeek, Now))
            .With(new ModSnooze(live, SnoozeKind.UntilNextGameVersion, Now, AtGameVersion: "1.6"));

        var pruned = set.Prune(Now.AddDays(30), _ => ("1.0", "1.6"));

        pruned.Entries.Should().ContainSingle();
        pruned.For(live).Should().NotBeNull();
    }

    [Fact]
    public void An_empty_set_round_trips_through_prune() =>
        SnoozeSet.Empty.Prune(Now, _ => (null, null)).Entries.Should().BeEmpty();
}
