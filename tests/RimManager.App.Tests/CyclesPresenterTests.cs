using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using Xunit;

namespace RimManager.App.Tests;

public sealed class CyclesPresenterTests
{
    private static ModId Id(string s) => ModId.From(s);

    private static OrderingEdge Edge(string before, string after) =>
        new(Id(before), Id(after), new RuleProvenance(RuleSource.Community, RuleType.LoadAfter));

    private static SortResult ResultWith(
        ImmutableArray<CycleReport> cycles, ImmutableArray<BrokenEdge> broken) =>
        new(
            order: [],
            tiers: ImmutableDictionary<ModId, Tier>.Empty,
            appliedEdges: [],
            droppedForTier: [],
            cycles: cycles,
            brokenEdges: broken);

    [Fact]
    public void BuildRows_shows_the_loop_and_the_broken_edge()
    {
        var cycle = ImmutableArray.Create(Id("a.mod"), Id("b.mod"));
        var result = ResultWith(
            [new CycleReport(cycle)],
            [new BrokenEdge(Edge("a.mod", "b.mod"), cycle)]);

        var row = CyclesPresenter.BuildRows(result).Single();
        row.Cycle.Should().Be("a.mod → b.mod → a.mod");   // loop closes back to the first node
        row.Broken.Should().Be("a.mod → b.mod");
        row.Source.Should().Be("Community");
    }

    [Fact]
    public void Summarize_counts_cycles_and_broken_edges()
    {
        var cycle = ImmutableArray.Create(Id("a"), Id("b"));
        var result = ResultWith(
            [new CycleReport(cycle)],
            [new BrokenEdge(Edge("a", "b"), cycle)]);

        CyclesPresenter.Summarize(result).Should().Be("1 cycle detected · 1 edge broken.");
    }

    [Fact]
    public void No_cycles_yields_empty_rows_and_a_clean_summary()
    {
        var result = ResultWith([], []);

        CyclesPresenter.BuildRows(result).Should().BeEmpty();
        CyclesPresenter.Summarize(result).Should().Be("No cycles — the load order is fully consistent.");
    }
}
