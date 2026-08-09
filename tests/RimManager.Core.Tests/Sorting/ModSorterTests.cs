using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;
using static RimManager.Core.Tests.Sorting.SortFixtures;

namespace RimManager.Core.Tests.Sorting;

public sealed class ModSorterTests
{
    private static readonly ModSorter Sorter = new();

    [Fact]
    public void Respects_a_simple_ordering_edge()
    {
        var mods = new[] { Mod("z.b"), Mod("z.a") };   // input order b, a
        var rules = Edges(("z.a", "z.b"));             // a must load before b

        Order(Sorter.Sort(mods, rules)).Should().Equal("z.a", "z.b");
    }

    [Fact]
    public void Orders_by_tier_regardless_of_input_order()
    {
        var mods = new[]
        {
            Mod("some.mod"),
            Mod("brrainz.harmony"),
            Mod("ludeon.rimworld", ModSource.Core),
            Mod("ludeon.rimworld.royalty", ModSource.Dlc),
        };

        Order(Sorter.Sort(mods, NoRules()))
            .Should().Equal("brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "some.mod");
    }

    [Fact]
    public void Drops_edges_that_violate_hard_tiering()
    {
        // A normal mod claims to load before Core — impossible under hard tiering.
        var mods = new[] { Mod("some.mod"), Mod("ludeon.rimworld", ModSource.Core) };
        var rules = Edges(("some.mod", "ludeon.rimworld"));

        var result = Sorter.Sort(mods, rules);

        Order(result).Should().Equal("ludeon.rimworld", "some.mod");
        result.DroppedForTier.Should().ContainSingle();
        result.AppliedEdges.Should().BeEmpty();
    }

    [Fact]
    public void Stable_tiebreak_preserves_input_order_when_unconstrained()
    {
        var mods = new[] { Mod("z.one"), Mod("a.two"), Mod("m.three") };

        // No rules, same tier -> current position wins, so input order is preserved.
        Order(Sorter.Sort(mods, NoRules())).Should().Equal("z.one", "a.two", "m.three");
    }

    [Fact]
    public void Output_is_a_permutation_of_the_input()
    {
        var mods = new[] { Mod("a"), Mod("b"), Mod("c"), Mod("d") };
        var rules = Edges(("c", "a"), ("d", "b"));

        Order(Sorter.Sort(mods, rules)).Should().BeEquivalentTo("a", "b", "c", "d");
    }

    [Fact]
    public void Detects_and_breaks_a_two_cycle_still_producing_a_full_order()
    {
        var mods = new[] { Mod("a"), Mod("b") };
        var rules = Edges(("a", "b"), ("b", "a"));

        var result = Sorter.Sort(mods, rules);

        result.HasCycles.Should().BeTrue();
        result.Cycles.Should().ContainSingle();
        result.BrokenEdges.Should().ContainSingle();
        Order(result).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void Cycle_breaking_is_deterministic()
    {
        var mods = new[] { Mod("a"), Mod("b"), Mod("c") };
        var rules = Edges(("a", "b"), ("b", "c"), ("c", "a"));

        var r1 = Sorter.Sort(mods, rules);
        var r2 = Sorter.Sort(mods, rules);

        Order(r1).Should().Equal(Order(r2));
        r1.BrokenEdges[0].Edge.Should().Be(r2.BrokenEdges[0].Edge);
    }
}
