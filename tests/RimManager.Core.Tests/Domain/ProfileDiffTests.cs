using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

public sealed class ProfileDiffTests
{
    private static ModlistState State(params (string id, bool enabled)[] mods) =>
        ModlistState.Empty.WithEntries(mods.Select(m => ModlistEntry.Mod(ModId.From(m.id), m.enabled)));

    [Fact]
    public void Identical_states_diff_to_nothing()
    {
        var a = State(("a", true), ("b", true));
        ProfileDiff.Between(a, a).IsIdentical.Should().BeTrue();
    }

    [Fact]
    public void Detects_added_and_removed()
    {
        var from = State(("a", true), ("b", true));
        var to = State(("a", true), ("c", true));

        var diff = ProfileDiff.Between(from, to);

        diff.Added.Should().Equal(ModId.From("c"));
        diff.Removed.Should().Equal(ModId.From("b"));
    }

    [Fact]
    public void Detects_reorder()
    {
        var from = State(("a", true), ("b", true), ("c", true));
        var to = State(("c", true), ("a", true), ("b", true));

        var diff = ProfileDiff.Between(from, to);

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
        diff.Moved.Should().NotBeEmpty();
        diff.Moved.Should().Contain(m => m.Id == ModId.From("c") && m.ToIndex == 0);
    }

    [Fact]
    public void Detects_enable_toggle()
    {
        var from = State(("a", true));
        var to = State(("a", false));

        var diff = ProfileDiff.Between(from, to);

        diff.EnableChanged.Should().ContainSingle(e => e.Id == ModId.From("a") && !e.NowEnabled);
    }
}
