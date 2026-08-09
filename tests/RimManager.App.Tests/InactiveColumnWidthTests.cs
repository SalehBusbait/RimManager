using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The inactive pane decides its own columns from its own measured width (N1 · §0b).
/// <para>
/// This replaced Columns ▾, and it is arithmetic rather than a picker because of a
/// measurement: literally taking "the active pane's columns minus the order number"
/// costs 286px of fixed width, which in the design's 298px pane leaves NAME
/// <b>ten pixels</b> — the R9 defect, where equal panes left NAME at 30px and every mod
/// rendered as three characters and an ellipsis, reintroduced and worse.
/// </para>
/// <para>
/// Pinned here because a width is a poor thing to verify by eye: a column that is 8px
/// too narrow still looks like a column.
/// </para>
/// </summary>
public sealed class InactiveColumnWidthTests
{
    private const double NoChevron = 0;
    private const double Chevron = 14;

    private static InactiveColumnLayout At(double paneWidth, double chevron = NoChevron) =>
        InactiveColumns.For(paneWidth, segmented: false, chevron);

    /// <summary>
    /// The design's own figure for this pane (`1a`). Whatever else changes, the default
    /// width has to show something beyond a bare name list, or the header it now has is
    /// a legend for two columns and a gap.
    /// </summary>
    [Fact]
    public void At_the_designs_298px_the_pane_shows_version_and_nothing_wider()
    {
        var l = At(298);

        l.Version.Should().Be(52);
        l.Author.Should().Be(0);
        l.PackageId.Should().Be(0);
        InactiveColumns.NameWidth(298, NoChevron, l).Should().Be(167);
    }

    /// <summary>
    /// The floor is the whole point: NAME never goes below 150 at any width, with any
    /// combination of columns showing. Swept rather than sampled, because the failure
    /// this guards is an off-by-one at a threshold.
    /// </summary>
    [Fact]
    public void Name_never_drops_below_its_floor_at_any_width()
    {
        foreach (var chevron in new[] { NoChevron, Chevron })
        {
            // Below this the pane cannot reach the floor even showing nothing optional,
            // so there is no column left to drop — that case is its own test below.
            var narrowest = InactiveColumns.Fixed + chevron + InactiveColumns.MinName;

            for (var w = narrowest; w <= 2000; w += 1)
            {
                var name = InactiveColumns.NameWidth(w, chevron, At(w, chevron));

                name.Should().BeGreaterThanOrEqualTo(InactiveColumns.MinName,
                    $"at {w}px (chevron {chevron}) the pane showed columns it could not afford");
            }
        }
    }

    /// <summary>
    /// Below the width where even a bare name list reaches the floor, there is no
    /// column left to drop — the pane cannot honour a floor it lacks the pixels for, so
    /// NAME simply shrinks rather than the layout doing something surprising.
    /// </summary>
    [Fact]
    public void Below_the_floor_there_is_simply_nothing_left_to_drop()
    {
        var l = At(180);

        l.Count.Should().Be(0);
        InactiveColumns.NameWidth(180, NoChevron, l).Should().BeLessThan(InactiveColumns.MinName);
    }

    /// <summary>
    /// Cheapest first — VER (52), then AUTHOR (110), then PACKAGEID (150). Pinned at
    /// each threshold and one pixel below it.
    /// </summary>
    [Theory]
    [InlineData(280, 0, 0, 0)]
    [InlineData(281, 52, 0, 0)]
    [InlineData(397, 52, 0, 0)]
    [InlineData(398, 52, 110, 0)]
    [InlineData(554, 52, 110, 0)]
    [InlineData(555, 52, 110, 150)]
    public void Columns_appear_cheapest_first_as_the_splitter_is_dragged(
        double paneWidth, double version, double author, double packageId)
    {
        var l = At(paneWidth);

        l.Version.Should().Be(version);
        l.Author.Should().Be(author);
        l.PackageId.Should().Be(packageId);
    }

    /// <summary>A column never disappears again as the pane gets wider.</summary>
    [Fact]
    public void Widening_the_pane_only_ever_adds_columns()
    {
        var shown = -1;

        for (var w = 200.0; w <= 1400; w += 1)
        {
            var count = At(w).Count;

            count.Should().BeGreaterThanOrEqualTo(shown, $"a column vanished as the pane grew, at {w}px");
            shown = count;
        }

        shown.Should().Be(3, "a wide pane shows every column — §0b's 'all columns at full width'");
    }

    /// <summary>
    /// 2k · below 900px every optional column collapses regardless of room. The pane is
    /// the whole window down there, so the arithmetic alone would show all three — but
    /// the ACTIVE row deliberately drops packageId and version at that breakpoint, and
    /// two lists disagreeing about what a narrow window shows is worse than either
    /// answer on its own.
    /// </summary>
    [Fact]
    public void The_segmented_breakpoint_collapses_every_optional_column()
    {
        InactiveColumns.For(860, segmented: false, NoChevron).Count.Should().Be(3,
            "the test is meaningless unless that width would otherwise afford them");

        InactiveColumns.For(860, segmented: true, NoChevron).Count.Should().Be(0);
    }

    /// <summary>
    /// The chevron is 14px of the pane between 900 and 1150 (`2k`), taken out of the
    /// same budget — otherwise the last column to appear would be 14px too eager and
    /// overlap the button that opens mod info.
    /// </summary>
    [Fact]
    public void The_info_chevron_is_paid_for_out_of_the_same_budget()
    {
        At(281, NoChevron).Version.Should().Be(52);
        At(281, Chevron).Version.Should().Be(0, "the chevron took the 14px VER needed");
        At(295, Chevron).Version.Should().Be(52, "14px wider, and it fits again");
    }
}
