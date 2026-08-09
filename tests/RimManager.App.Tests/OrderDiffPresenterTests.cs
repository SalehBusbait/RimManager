using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>The order-diff dialog's headline and rows (S-ORDERDIFF).</summary>
public sealed class OrderDiffPresenterTests
{
    private static ImmutableArray<ModId> Ids(params string[] ids) =>
        [.. ids.Select(ModId.From)];

    [Fact]
    public void The_headline_enumerates_what_exists_and_omits_what_does_not()
    {
        var diff = OrderDiff.Between(
            Ids("a", "b", "c", "d", "e"),
            Ids("x", "a", "c", "d", "b"));   // 1 insert · 1 removal · 1 move · 3 unchanged

        OrderDiffPresenter.Headline(diff)
            .Should().Be("1 insert · 1 removal · 1 move — 3 rows unchanged");
    }

    [Fact]
    public void A_pure_insert_headline_never_mentions_moves()
    {
        var diff = OrderDiff.Between(Ids("a", "b"), Ids("new", "a", "b"));

        OrderDiffPresenter.Headline(diff).Should().Be("1 insert — 2 rows unchanged");
    }

    [Fact]
    public void Identical_orders_say_so()
    {
        var diff = OrderDiff.Between(Ids("a", "b"), Ids("a", "b"));

        OrderDiffPresenter.Headline(diff).Should().Be("No differences — 2 rows identical");
    }

    [Fact]
    public void Rows_carry_marks_names_and_the_mono_position_grammar()
    {
        var diff = OrderDiff.Between(
            Ids("a", "b", "c", "d", "e"),
            Ids("x", "a", "c", "d", "b"));

        var rows = OrderDiffPresenter.Rows(diff, id => id.Value == "x" ? "Xenobionic" : null);

        // Incoming order first (insert at theirs #1, move to theirs #5), removals last.
        rows.Should().HaveCount(3);
        rows[0].Should().Be(new OrderDiffRow(OrderDiffRowKind.Insert, "Xenobionic", "theirs #1"));
        rows[1].Should().Be(new OrderDiffRow(OrderDiffRowKind.Move, "b", "yours #2 → theirs #5"));
        rows[2].Should().Be(new OrderDiffRow(OrderDiffRowKind.Remove, "e", "yours #5"));
    }

    /// <summary>A mod the scan cannot name still has its packageId — never a blank row.</summary>
    [Fact]
    public void Unnamed_mods_fall_back_to_their_package_id()
    {
        var diff = OrderDiff.Between(Ids("a"), Ids("a", "Ludeon.NewDlc"));

        var rows = OrderDiffPresenter.Rows(diff, _ => null);

        rows.Single().Name.Should().Be("Ludeon.NewDlc");
    }

    [Fact]
    public void Take_theirs_is_the_only_route_that_accepts()
    {
        var diff = OrderDiff.Between(Ids("a"), Ids("a", "b"));

        var vm = new OrderDiffViewModel(diff, _ => null);

        vm.Accepted.Should().BeFalse("closing by any route other than the verb changes nothing");
        vm.Headline.Should().Contain("1 insert");
        vm.Rows.Should().HaveCount(1);
    }
}
