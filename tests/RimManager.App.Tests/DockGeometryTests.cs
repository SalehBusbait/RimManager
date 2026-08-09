using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The dock's remembered sizes. None of this is visible to CI once it is on screen,
/// which is exactly why the arithmetic lives outside Avalonia.
/// </summary>
public sealed class DockGeometryTests
{
    [Fact]
    public void The_dock_opens_at_the_default_height()
    {
        new DockGeometry().BodyHeight.Should().Be(DockGeometry.DefaultBodyHeight);
    }

    /// <summary>
    /// O4 · ONE height, whichever tab is showing. The design argued it per tab
    /// ("Conflicts wants more than Updates"), but Conflicts stopped being a tab in N6c
    /// and what per-tab memory produces in use is the dock resizing itself on every
    /// switch. This test asserted the opposite before; it is rewritten, not deleted.
    /// </summary>
    [Fact]
    public void The_height_does_not_change_when_the_tab_does()
    {
        var geometry = new DockGeometry { BodyHeight = 420 };

        geometry.SetDetailWidth(1, 300);
        geometry.SetDetailWidth(2, 640);

        geometry.BodyHeight.Should().Be(420, "switching tabs must not resize the dock");
    }

    /// <summary>Reset is the layout-reset command's half, and it takes the height too.</summary>
    [Fact]
    public void Reset_returns_the_height_and_the_widths_to_their_defaults()
    {
        var geometry = new DockGeometry { BodyHeight = 420 };
        geometry.SetDetailWidth(2, 640);

        geometry.Reset();

        geometry.BodyHeight.Should().Be(DockGeometry.DefaultBodyHeight);
        geometry.DetailWidthFor(2).Should().Be(DockGeometry.DefaultDetailWidth(2));
    }

    [Fact]
    public void Detail_widths_start_at_the_designed_width_per_tab()
    {
        DockGeometry.DefaultDetailWidth(0).Should().Be(392, "Warnings");
        DockGeometry.DefaultDetailWidth(1).Should().Be(392, "Updates");
        DockGeometry.DefaultDetailWidth(3).Should().Be(392, "Activity");
    }

    /// <summary>
    /// History is three panes (2d): master list, diff, and a fixed 248px snapshot
    /// rail. Its detail column is the diff alone, so it is wider than the two-pane
    /// tabs — and on 2d's 1440 window the master lands on its designed 560.
    /// (History is tab 2 since N6c removed Conflicts.)
    /// </summary>
    [Fact]
    public void Historys_detail_leaves_its_master_the_designed_width()
    {
        const double aside = 248, splitter = 5, window = 1440;

        var detail = DockGeometry.DefaultDetailWidth(2);
        var master = window - detail - aside - splitter;

        detail.Should().BeGreaterThan(DockGeometry.DefaultDetailWidth(0));
        master.Should().BeApproximately(560, 10);
    }

    [Fact]
    public void Each_tab_remembers_its_own_splitter_position()
    {
        var geometry = new DockGeometry();

        geometry.SetDetailWidth(2, 520);

        geometry.DetailWidthFor(2).Should().Be(520);
        geometry.DetailWidthFor(0).Should().Be(392);
    }

    [Theory]
    [InlineData(40, 120)]     // below the floor
    [InlineData(300, 300)]    // inside the band
    [InlineData(9000, 474)]   // above the 50% ceiling: 1000/2 - 26
    public void Height_is_clamped_to_the_designed_band(double requested, double expected)
    {
        DockGeometry.ClampBodyHeight(requested, windowHeight: 1000).Should().Be(expected);
    }

    /// <summary>
    /// The floor wins over the ceiling on a tiny window. A dock clamped to 40px is a
    /// dock you cannot read; one that overflows a 200px window is merely ugly.
    /// </summary>
    [Fact]
    public void The_minimum_survives_a_window_too_short_for_it()
    {
        DockGeometry.ClampBodyHeight(300, windowHeight: 200)
            .Should().Be(DockGeometry.MinBodyHeight);
    }

    /// <summary>
    /// A dock dragged tall on a big window has to come back down when the window
    /// shrinks. The ceiling was only ever checked at drag time, so restoring a
    /// maximised window left the dock owning most of it — with the splitter that
    /// would fix it pushed off screen.
    /// </summary>
    [Fact]
    public void A_height_that_was_legal_on_a_big_window_is_reclamped_on_a_small_one()
    {
        var tall = DockGeometry.ClampBodyHeight(900, windowHeight: 2000);
        tall.Should().Be(900);

        DockGeometry.ClampBodyHeight(tall, windowHeight: 800).Should().Be(374, "800/2 - 26");
    }

    /// <summary>⤢ and dragging the grip to the top must agree, or ⤢ looks broken.</summary>
    [Fact]
    public void Maximising_lands_exactly_on_the_ceiling_the_splitter_enforces()
    {
        var maximum = DockGeometry.MaximisedBodyHeight(windowHeight: 900);

        maximum.Should().Be(DockGeometry.ClampBodyHeight(double.MaxValue, 900));
        DockGeometry.ClampBodyHeight(maximum, 900).Should().Be(maximum);
    }
}
