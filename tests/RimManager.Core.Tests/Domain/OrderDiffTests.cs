using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// The LCS-anchored order diff (S-ORDERDIFF). The canonical case is the spec's own
/// sentence: an insert at the top is ONE insert, not 547 moves.
/// </summary>
public sealed class OrderDiffTests
{
    private static ImmutableArray<ModId> Ids(params string[] ids) =>
        [.. ids.Select(ModId.From)];

    [Fact]
    public void An_insert_at_the_top_is_one_insert_and_zero_moves()
    {
        var yours = Ids("a", "b", "c", "d", "e");
        var theirs = Ids("new", "a", "b", "c", "d", "e");

        var diff = OrderDiff.Between(yours, theirs);

        diff.Inserted.Should().ContainSingle()
            .Which.Should().Be(new OrderInsert(ModId.From("new"), TheirsPosition: 1));
        diff.Moved.Should().BeEmpty("index-inequality diffs are discredited and must not return");
        diff.Removed.Should().BeEmpty();
        diff.UnchangedCount.Should().Be(5);
    }

    [Fact]
    public void A_row_dragged_to_the_bottom_is_one_move_with_both_positions()
    {
        var yours = Ids("a", "b", "c", "d");
        var theirs = Ids("b", "c", "d", "a");

        var diff = OrderDiff.Between(yours, theirs);

        diff.Moved.Should().ContainSingle()
            .Which.Should().Be(new OrderMove(ModId.From("a"), YoursPosition: 1, TheirsPosition: 4));
        diff.UnchangedCount.Should().Be(3);
        diff.IsIdentical.Should().BeFalse();
    }

    [Fact]
    public void A_dropped_mod_is_a_removal_not_a_wall_of_moves()
    {
        var yours = Ids("a", "b", "c", "d", "e");
        var theirs = Ids("a", "b", "d", "e");

        var diff = OrderDiff.Between(yours, theirs);

        diff.Removed.Should().ContainSingle()
            .Which.Should().Be(new OrderRemove(ModId.From("c"), YoursPosition: 3));
        diff.Moved.Should().BeEmpty();
        diff.UnchangedCount.Should().Be(4);
    }

    [Fact]
    public void Insert_move_and_remove_can_coexist()
    {
        var yours = Ids("a", "b", "c", "d", "e");
        var theirs = Ids("x", "a", "c", "d", "b");   // +x · b moved · e removed

        var diff = OrderDiff.Between(yours, theirs);

        diff.Inserted.Should().ContainSingle().Which.Id.Should().Be(ModId.From("x"));
        diff.Removed.Should().ContainSingle().Which.Id.Should().Be(ModId.From("e"));
        diff.Moved.Should().ContainSingle()
            .Which.Should().Be(new OrderMove(ModId.From("b"), 2, 5));
        diff.UnchangedCount.Should().Be(3);
    }

    [Fact]
    public void Identical_orders_have_nothing_to_say()
    {
        var order = Ids("a", "b", "c");

        var diff = OrderDiff.Between(order, order);

        diff.IsIdentical.Should().BeTrue();
        diff.UnchangedCount.Should().Be(3);
    }

    /// <summary>Two adjacent rows swapping is one move, whichever the anchor keeps.</summary>
    [Fact]
    public void A_swap_of_neighbours_is_one_move()
    {
        var diff = OrderDiff.Between(Ids("a", "b", "c"), Ids("b", "a", "c"));

        diff.Moved.Should().HaveCount(1);
        diff.UnchangedCount.Should().Be(2);
    }

    [Fact]
    public void Package_id_identity_is_case_insensitive()
    {
        var diff = OrderDiff.Between(Ids("Jaxe.RimHUD"), Ids("jaxe.rimhud"));

        diff.IsIdentical.Should().BeTrue("the same mod spelled two ways is the same mod");
    }

    /// <summary>A corrupt ModsConfig with a duplicated id degrades, never crashes.</summary>
    [Fact]
    public void Duplicates_keep_their_first_occurrence()
    {
        var diff = OrderDiff.Between(Ids("a", "b"), Ids("a", "b", "a"));

        diff.IsIdentical.Should().BeTrue();
        diff.UnchangedCount.Should().Be(2);
    }
}
