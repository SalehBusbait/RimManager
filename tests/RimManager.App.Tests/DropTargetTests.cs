using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// O9 · where a drop lands. This arithmetic decides a real reorder of the load order
/// and had NO test of any kind — the drag path's three stages were computed (untested),
/// screened (18 tests), then applied (untested).
/// </summary>
public class DropTargetTests
{
    private const double H = 20;

    /// <summary>An unfiltered list of <paramref name="n"/> rows, each 20px tall.</summary>
    private static List<DropRow> Rows(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new DropRow(i, i * H, H, true))];

    /// <summary>The owner's case: only 12, 23 and 40 survive the filter.</summary>
    private static List<DropRow> Filtered()
    {
        var visible = new[] { 12, 23, 40 };
        var rows = new List<DropRow>();
        var y = 0d;

        for (var i = 0; i < 50; i++)
        {
            if (visible.Contains(i))
            {
                rows.Add(new DropRow(i, y, H, true));
                y += H;
            }
            else
            {
                // Hidden: Avalonia leaves stale or zero geometry behind, and the
                // arithmetic must not read it. Deliberately given a WRONG rectangle so a
                // regression that trusts geometry fails here.
                rows.Add(new DropRow(i, i * H, H, false));
            }
        }

        return rows;
    }

    // --- unfiltered: the existing behaviour must not move ---------------------

    [Theory]
    [InlineData(5, 0)]      // above the first midpoint
    [InlineData(15, 1)]     // past row 0's midpoint
    [InlineData(25, 1)]     // above row 1's midpoint
    [InlineData(35, 2)]
    [InlineData(190, 10)]   // past the last row's midpoint → append
    public void An_unfiltered_drop_lands_where_it_always_did(double pointerY, int expected) =>
        DropTarget.For(Rows(10), pointerY, fallback: 10).Should().Be(expected);

    [Fact]
    public void A_gap_between_two_adjacent_rows_anchors_on_the_upper_one()
    {
        // The general rule and the old behaviour agree when nothing is hidden: the gap
        // between rows 3 and 4 gives 4.
        DropTarget.For(Rows(10), pointerY: 80, fallback: 10).Should().Be(4);
    }

    // --- filtered: the owner's worked example ---------------------------------

    [Fact]
    public void Dropping_between_the_visible_12_and_23_lands_at_13()
    {
        // Visible 12 occupies y 0-20, visible 23 occupies y 20-40. A pointer below 12's
        // midpoint is "in the gap under 12" and must land adjacent to it — not after the
        // ten hidden rows the user cannot see.
        DropTarget.For(Filtered(), pointerY: 15, fallback: 50).Should().Be(13);
    }

    [Fact]
    public void Dropping_between_the_visible_23_and_40_lands_at_24()
    {
        DropTarget.For(Filtered(), pointerY: 35, fallback: 50).Should().Be(24);
    }

    [Fact]
    public void Dropping_below_the_last_visible_row_lands_at_41()
    {
        DropTarget.For(Filtered(), pointerY: 55, fallback: 50).Should().Be(41);
    }

    [Fact]
    public void Dropping_above_the_topmost_visible_row_lands_on_it()
    {
        // The one place this departs from the plan's worked example, which says 11.
        // Adopting 11 would mean "one place above the row you aimed at", which cannot be
        // confined to the filtered case — with no filter it would move every ordinary
        // drop one row higher than the indicator promises.
        DropTarget.For(Filtered(), pointerY: 2, fallback: 50).Should().Be(12);
    }

    // --- the defect this replaced --------------------------------------------

    [Fact]
    public void Stale_geometry_on_a_hidden_row_cannot_capture_the_drop()
    {
        // The real bug: Avalonia never re-arranges a hidden container, so row 1 keeps a
        // PRE-FILTER rectangle far down the list while the surviving rows have been
        // re-laid out at the top. The old code read that rectangle and took the MINIMUM
        // qualifying index, so the stale row won and the mod moved to position 1.
        //
        // The fixture is built so the two algorithms disagree: old → 1, new → 6. An
        // earlier version of this test used a stale rect at y=20, where both happened to
        // return 1 — it would have passed against the bug it was written for.
        List<DropRow> rows =
        [
            new(1, 100, H, false),     // stale, far below where it now renders
            new(5, 0, H, true),
            new(6, 20, H, true),
            new(7, 40, H, true),
        ];

        DropTarget.For(rows, pointerY: 25, fallback: 8)
            .Should().Be(6, "the anchor is visible row 5, so the landing is 5 + 1");

        // And the shape that would capture THIS algorithm rather than the old one: a
        // hidden row with a HIGH index whose stale rectangle sits above the pointer. It
        // would out-rank the true anchor and send the mod ten places down the list.
        // Checked by reintroducing the bug and watching this fail.
        List<DropRow> staleAbove =
        [
            new(5, 0, H, true),
            new(6, 20, H, true),
            new(15, 0, H, false),      // stale: claims to be at the very top
        ];

        DropTarget.For(staleAbove, pointerY: 25, fallback: 20)
            .Should().Be(6, "row 15 is hidden, so its rectangle is not evidence of anything");
    }

    [Fact]
    public void A_hidden_row_realized_at_the_origin_cannot_capture_the_drop_either()
    {
        // The other half: a container realized while already hidden has Bounds 0,0,0,0.
        List<DropRow> rows =
        [
            new(0, 0, H, true),
            new(1, 0, 0, false),
            new(2, 20, H, true),
        ];

        // Visible 0 and 2 are adjacent on screen; a pointer at 25 is in the upper half of
        // row 2, which is the gap BELOW row 0 — so the landing is 0 + 1. The point of the
        // test is that hidden row 1's origin rectangle changes nothing.
        DropTarget.For(rows, pointerY: 25, fallback: 3).Should().Be(1);
        DropTarget.For(rows, pointerY: 35, fallback: 3).Should().Be(3, "past row 2's midpoint");
    }

    // --- degenerate cases -----------------------------------------------------

    [Fact]
    public void An_empty_list_falls_back()
    {
        DropTarget.For([], pointerY: 40, fallback: 7).Should().Be(7);
    }

    [Fact]
    public void A_list_with_nothing_visible_falls_back()
    {
        // Every row filtered out: there is no row to be adjacent to, so appending at the
        // end is the only answer that is not a guess.
        List<DropRow> rows = [new(0, 0, H, false), new(1, 20, H, false)];

        DropTarget.For(rows, pointerY: 10, fallback: 2).Should().Be(2);
    }

    [Fact]
    public void Containers_arriving_out_of_order_do_not_change_the_answer()
    {
        // GetRealizedContainers gives no ordering guarantee, so the arithmetic must not
        // depend on one.
        List<DropRow> rows =
        [
            new(3, 60, H, true),
            new(0, 0, H, true),
            new(2, 40, H, true),
            new(1, 20, H, true),
        ];

        // Pointer 45 is in row 2's upper half, so the anchor is row 1 and the landing 2 —
        // the same answer the in-order theory above gives for a pointer of 35.
        DropTarget.For(rows, pointerY: 45, fallback: 4).Should().Be(2);
    }
}
