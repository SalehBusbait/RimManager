using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;
using static RimManager.Core.Tests.Sorting.SortFixtures;

namespace RimManager.Core.Tests.Sorting;

/// <summary>
/// Property tests for constraint #6: sorting is deterministic and idempotent.
/// Each seed builds a random DAG (edges only from lower to higher index, so it's
/// acyclic), shuffles the input order, and checks the invariants.
/// </summary>
public sealed class ModSorterPropertyTests
{
    private static readonly ModSorter Sorter = new();

    private static (List<Mod> mods, RuleSet rules, List<(string, string)> edges) BuildRandomDag(int seed, int n)
    {
        var rng = new Random(seed);
        var ids = Enumerable.Range(0, n).Select(i => $"mod.{i:D3}").ToList();

        var edges = new List<(string, string)>();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (rng.NextDouble() < 0.12) edges.Add((ids[i], ids[j])); // i before j -> acyclic
            }
        }

        var shuffled = ids.OrderBy(_ => rng.Next()).ToList();
        var mods = shuffled.Select(id => Mod(id)).ToList();
        return (mods, Edges([.. edges]), edges);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(123)]
    [InlineData(999)]
    [InlineData(2026)]
    public void Sort_is_deterministic_and_idempotent(int seed)
    {
        var (mods, rules, _) = BuildRandomDag(seed, 40);

        var first = Order(Sorter.Sort(mods, rules));
        var again = Order(Sorter.Sort(mods, rules));
        first.Should().Equal(again, "sorting the same input twice must be identical");

        // Idempotence: feed the sorted order back in and it must not change.
        var reordered = first.Select(v => Mod(v)).ToList();
        var second = Order(Sorter.Sort(reordered, rules));
        second.Should().Equal(first, "sorting an already-sorted list is a no-op");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(58)]
    [InlineData(301)]
    public void Output_preserves_all_mods_and_respects_every_edge(int seed)
    {
        var (mods, rules, edges) = BuildRandomDag(seed, 50);
        var result = Sorter.Sort(mods, rules);

        Order(result).Should().BeEquivalentTo(mods.Select(m => m.PackageId.Value),
            "the output is a permutation of the input");
        result.HasCycles.Should().BeFalse("the generated graph is acyclic");

        foreach (var (before, after) in edges)
        {
            result.PositionOf(ModId.From(before)).Should()
                .BeLessThan(result.PositionOf(ModId.From(after)),
                    $"edge {before} -> {after} must be respected");
        }
    }

    [Fact]
    public void Output_tiers_are_monotonic_non_decreasing()
    {
        var mods = new[]
        {
            Mod("some.a"), Mod("brrainz.harmony"), Mod("some.b"),
            Mod("ludeon.rimworld", ModSource.Core), Mod("ludeon.rimworld.royalty", ModSource.Dlc),
            Mod("some.c"),
        };

        var result = Sorter.Sort(mods, NoRules());

        var tierSequence = result.Order.Select(id => (int)result.Tiers[id]).ToList();
        tierSequence.Should().BeInAscendingOrder("tiers must never go backwards in the output");
    }
}
