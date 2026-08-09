using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// 2k's two thresholds. Pinned here rather than left as two numbers in a resize
/// handler, because which layout is in force decides where the width goes — and
/// "the load order is the last thing to lose space".
/// </summary>
public sealed class BreakpointsTests
{
    [Theory]
    [InlineData(1920, WindowLayout.Full)]
    [InlineData(1440, WindowLayout.Full)]
    [InlineData(1150, WindowLayout.Full)]      // the breakpoint is exclusive
    [InlineData(1149, WindowLayout.Drawer)]
    [InlineData(1100, WindowLayout.Drawer)]    // the window's own minimum width
    [InlineData(900, WindowLayout.Drawer)]
    [InlineData(899, WindowLayout.Segmented)]
    [InlineData(640, WindowLayout.Segmented)]
    public void The_layout_follows_the_two_breakpoints(double width, WindowLayout expected)
    {
        Breakpoints.For(width).Should().Be(expected);
    }

    /// <summary>
    /// Mod info is an overlay in BOTH narrow layouts. Below 900 the design calls it a
    /// full-height sheet, which is this drawer at a different width — not a third
    /// arrangement to build and keep in step.
    /// </summary>
    [Fact]
    public void Mod_info_is_an_overlay_in_both_narrow_layouts()
    {
        Breakpoints.InfoIsOverlay(1440).Should().BeFalse();
        Breakpoints.InfoIsOverlay(1000).Should().BeTrue();
        Breakpoints.InfoIsOverlay(800).Should().BeTrue();
    }

    /// <summary>
    /// UI scale moves the breakpoints, because it changes how much room the layout
    /// gets. At 150% on an 1180px window the app is laid out in 787 logical pixels —
    /// which is below the segmented breakpoint, even though the window is nowhere near
    /// it. Deciding from the window's physical width left the three-pane layout running
    /// in 787px and nothing knew.
    /// </summary>
    [Theory]
    [InlineData(1180, 100, WindowLayout.Full)]
    [InlineData(1180, 125, WindowLayout.Drawer)]      // 944
    [InlineData(1180, 150, WindowLayout.Segmented)]   // 787
    [InlineData(1600, 125, WindowLayout.Full)]        // 1280
    [InlineData(1600, 150, WindowLayout.Drawer)]      // 1067
    [InlineData(1000, 80, WindowLayout.Full)]         // 1250 — scaling DOWN gains room
    public void The_layout_width_is_the_window_divided_by_the_ui_scale(
        double windowWidth, int scalePercent, WindowLayout expected)
    {
        var layoutWidth = windowWidth / (scalePercent / 100.0);

        Breakpoints.For(layoutWidth).Should().Be(expected);
    }

    [Fact]
    public void The_drawer_is_the_specified_340px_where_there_is_room_for_it()
    {
        Breakpoints.OverlayWidth(1000).Should().Be(340);
    }

    /// <summary>
    /// A 340px drawer on a 380px window leaves 40px of list, which is not a drawer, it
    /// is a takeover. It narrows instead, and never past the point of being readable.
    /// </summary>
    [Fact]
    public void On_a_very_narrow_window_the_drawer_leaves_the_list_visible()
    {
        Breakpoints.OverlayWidth(380).Should().Be(320);
        Breakpoints.OverlayWidth(300).Should().Be(260, "it stops narrowing before it is unreadable");
    }
}
