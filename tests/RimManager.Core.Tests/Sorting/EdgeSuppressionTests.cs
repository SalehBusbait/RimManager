using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;
using static RimManager.Core.Tests.Sorting.SortFixtures;

namespace RimManager.Core.Tests.Sorting;

/// <summary>
/// User edge suppression: "Drop a different edge…" in the Warnings detail panel
/// (<c>2a</c>) and click-an-edge in the cycle graph (<c>3b</c>).
/// <para>
/// The sorter already breaks cycles deterministically, but its choice is still a
/// guess. These tests cover the user overriding it — and, just as importantly, that
/// the override does not break determinism or idempotence (constraint #6).
/// </para>
/// </summary>
public sealed class EdgeSuppressionTests
{
    private static readonly ModSorter Sorter = new();

    private static EdgeSuppressions Suppress(params (string before, string after)[] edges)
    {
        var set = EdgeSuppressions.Empty;
        foreach (var (before, after) in edges)
            set = set.With(ModId.From(before), ModId.From(after));
        return set;
    }

    // --- the set type -------------------------------------------------------

    [Fact]
    public void Empty_suppresses_nothing() =>
        EdgeSuppressions.Empty.Contains(ModId.From("a"), ModId.From("b")).Should().BeFalse();

    [Fact]
    public void With_is_idempotent()
    {
        var once = Suppress(("a", "b"));
        var twice = once.With(ModId.From("a"), ModId.From("b"));

        twice.Edges.Should().HaveCount(1);
    }

    [Fact]
    public void Suppression_is_directional()
    {
        var set = Suppress(("a", "b"));

        set.Contains(ModId.From("a"), ModId.From("b")).Should().BeTrue();
        set.Contains(ModId.From("b"), ModId.From("a")).Should().BeFalse(
            "a → b and b → a are different constraints");
    }

    [Fact]
    public void Without_restores_the_edge()
    {
        var set = Suppress(("a", "b"), ("c", "d"))
            .Without(ModId.From("a"), ModId.From("b"));

        set.Contains(ModId.From("a"), ModId.From("b")).Should().BeFalse();
        set.Contains(ModId.From("c"), ModId.From("d")).Should().BeTrue();
    }

    // --- effect on the sort -------------------------------------------------

    [Fact]
    public void A_suppressed_edge_is_not_applied_and_is_reported_separately()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b") };
        var rules = Edges(("b", "a")); // b must load before a

        var result = Sorter.Sort(mods, rules, Suppress(("b", "a")));

        result.AppliedEdges.Should().BeEmpty();
        result.SuppressedByUser.Should().ContainSingle();
        result.DroppedForTier.Should().BeEmpty("this was a user choice, not a tier conflict");

        // With the constraint gone, the original order stands.
        Order(result).Should().Equal("a", "b");
    }

    [Fact]
    public void Without_suppression_the_same_edge_is_honoured()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b") };

        Order(Sorter.Sort(mods, Edges(("b", "a")))).Should().Equal("b", "a");
    }

    /// <summary>
    /// The point of the feature: the user picks which edge of a cycle gives way, and
    /// the two the sorter would have kept are then both honoured.
    /// </summary>
    [Fact]
    public void Suppressing_a_cycle_edge_lets_the_rest_of_the_cycle_be_honoured()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b"), Mod("c") };
        var rules = Edges(("a", "b"), ("b", "c"), ("c", "a")); // a→b→c→a

        var result = Sorter.Sort(mods, rules, Suppress(("c", "a")));

        result.Cycles.Should().BeEmpty("removing one edge makes the graph acyclic");
        result.BrokenEdges.Should().BeEmpty("nothing had to be broken automatically");
        Order(result).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Suppressing_a_different_edge_of_the_same_cycle_gives_a_different_order()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b"), Mod("c") };
        var rules = Edges(("a", "b"), ("b", "c"), ("c", "a"));

        var viaC = Order(Sorter.Sort(mods, rules, Suppress(("c", "a"))));
        var viaA = Order(Sorter.Sort(mods, rules, Suppress(("a", "b"))));

        viaC.Should().Equal("a", "b", "c");
        viaA.Should().Equal("b", "c", "a");
    }

    /// <summary>Suppressing an edge that is not in any cycle is harmless — it just
    /// relaxes a constraint, and must not invent a cycle report.</summary>
    [Fact]
    public void Suppressing_a_non_cycle_edge_only_relaxes_it()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b"), Mod("c") };
        var rules = Edges(("c", "a"), ("c", "b"));

        var result = Sorter.Sort(mods, rules, Suppress(("c", "b")));

        result.Cycles.Should().BeEmpty();
        result.AppliedEdges.Should().ContainSingle("only c -> a survives");

        // b is now unconstrained, so priority-Kahn places it by input position (1),
        // ahead of c (2). Only the c -> a constraint still binds.
        Order(result).Should().Equal("b", "c", "a");
        result.PositionOf(ModId.From("c")).Should().BeLessThan(result.PositionOf(ModId.From("a")));
    }

    [Fact]
    public void Suppressing_an_edge_that_does_not_exist_changes_nothing()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b") };
        var rules = Edges(("b", "a"));

        var result = Sorter.Sort(mods, rules, Suppress(("a", "zzz")));

        result.SuppressedByUser.Should().BeEmpty();
        Order(result).Should().Equal("b", "a");
    }

    /// <summary>Constraint #6 still holds with suppressions in play.</summary>
    [Fact]
    public void Sorting_with_suppressions_stays_idempotent()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b"), Mod("c"), Mod("d") };
        var rules = Edges(("a", "b"), ("b", "c"), ("c", "a"), ("d", "b"));
        var suppressed = Suppress(("b", "c"));

        var once = Sorter.Sort(mods, rules, suppressed);
        var reordered = once.Order.Select(id => Mod(id.Value)).ToList();
        var twice = Sorter.Sort(reordered, rules, suppressed);

        Order(twice).Should().Equal(Order(once));
    }

    [Fact]
    public void A_null_suppression_set_behaves_exactly_like_an_empty_one()
    {
        var mods = new List<Mod> { Mod("a"), Mod("b") };
        var rules = Edges(("b", "a"));

        Order(Sorter.Sort(mods, rules, null))
            .Should().Equal(Order(Sorter.Sort(mods, rules, EdgeSuppressions.Empty)));
    }
}
