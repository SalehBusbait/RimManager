using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// The "there is always exactly one undeletable default modlist" invariant. Pure, so it
/// is decided here rather than by whatever the storage layer happened to do.
/// </summary>
public sealed class DefaultModlistTests
{
    private static Modlist List(
        string id, string name, bool isDefault = false, int createdDaysAgo = 0) =>
        new()
        {
            Id = id,
            Name = name,
            IsDefault = isDefault,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-createdDaysAgo),
        };

    [Fact]
    public void No_lists_at_all_asks_the_caller_to_seed_one()
    {
        var result = DefaultModlist.Reconcile([]);

        result.NeedsSeeding.Should().BeTrue(
            "seeding means reading the game's live ModsConfig.xml, which Core cannot do");
        result.Lists.Should().BeEmpty();
    }

    [Fact]
    public void Exactly_one_default_is_left_completely_alone()
    {
        var lists = new[] { List("a", "Default", isDefault: true), List("b", "Modded") };

        var result = DefaultModlist.Reconcile(lists);

        result.NeedsSeeding.Should().BeFalse();
        result.Changed.Should().BeEmpty(
            "the common case must not cost a disk write on every single load");
        result.Lists.Should().BeSameAs(lists);
    }

    [Fact]
    public void With_no_default_the_list_actually_named_Default_is_promoted()
    {
        // The newest, so age alone would not have chosen it.
        var lists = new[]
        {
            List("a", "Heavily modded", createdDaysAgo: 10),
            List("b", "Default", createdDaysAgo: 1),
        };

        var result = DefaultModlist.Reconcile(lists);

        result.Lists.Single(l => l.IsDefault).Id.Should().Be("b",
            "a list the user already called Default is the one they will expect");
        result.Changed.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public void With_no_default_and_no_such_name_the_oldest_is_promoted()
    {
        var lists = new[]
        {
            List("young", "Testing", createdDaysAgo: 1),
            List("old", "Main", createdDaysAgo: 30),
        };

        var result = DefaultModlist.Reconcile(lists);

        result.Lists.Single(l => l.IsDefault).Id.Should().Be("old");
    }

    [Fact]
    public void More_than_one_default_keeps_the_oldest_and_demotes_the_rest()
    {
        var lists = new[]
        {
            List("new", "Copy", isDefault: true, createdDaysAgo: 1),
            List("old", "Original", isDefault: true, createdDaysAgo: 9),
            List("plain", "Other"),
        };

        var result = DefaultModlist.Reconcile(lists);

        result.Lists.Where(l => l.IsDefault).Should().ContainSingle()
            .Which.Id.Should().Be("old");
        result.Changed.Should().ContainSingle().Which.Id.Should().Be("new",
            "only the demoted list needs writing back");
    }

    /// <summary>
    /// Migration creates every list in one loop, so their timestamps collide. Without a
    /// tiebreak the winner would be whichever the filesystem enumerated first, which is
    /// not stable across machines or runs.
    /// </summary>
    [Fact]
    public void Lists_created_in_the_same_tick_still_resolve_to_one_stable_answer()
    {
        var a = List("aaa", "One", isDefault: true);
        var b = List("bbb", "Two", isDefault: true);

        var forwards = DefaultModlist.Reconcile([a, b]);
        var backwards = DefaultModlist.Reconcile([b, a]);

        forwards.Lists.Single(l => l.IsDefault).Id.Should().Be("aaa");
        backwards.Lists.Single(l => l.IsDefault).Id
            .Should().Be("aaa", "enumeration order must not change which list wins");
    }

    [Fact]
    public void Reconciling_an_already_reconciled_set_changes_nothing_further()
    {
        var once = DefaultModlist.Reconcile([List("a", "One"), List("b", "Two")]);
        var twice = DefaultModlist.Reconcile(once.Lists);

        twice.Changed.Should().BeEmpty("reconcile runs on every load; it has to settle");
        twice.Lists.Single(l => l.IsDefault).Id.Should().Be(once.Lists.Single(l => l.IsDefault).Id);
    }

    [Fact]
    public void The_default_can_be_renamed_and_stays_the_default()
    {
        var renamed = List("a", "Main playthrough", isDefault: true);

        var result = DefaultModlist.Reconcile([renamed, List("b", "Other")]);

        result.Changed.Should().BeEmpty(
            "the flag is identity, not the name — renaming the default must not demote it");
        result.Lists.Single(l => l.IsDefault).Name.Should().Be("Main playthrough");
    }

    [Fact]
    public void The_default_can_never_be_deleted()
    {
        DefaultModlist.CanDelete(List("a", "Default", isDefault: true), totalLists: 5)
            .Should().BeFalse();
    }

    [Fact]
    public void The_last_list_standing_can_never_be_deleted_either()
    {
        DefaultModlist.CanDelete(List("a", "Only one"), totalLists: 1)
            .Should().BeFalse("deleting it would leave nothing to load and force a reseed anyway");
    }

    [Fact]
    public void An_ordinary_list_can_be_deleted()
    {
        DefaultModlist.CanDelete(List("b", "Testing"), totalLists: 3).Should().BeTrue();
    }
}
