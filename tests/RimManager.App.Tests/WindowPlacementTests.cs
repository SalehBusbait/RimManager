using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// O17 · whether a remembered window rectangle may be used. Every case here is one a
/// user hits by unplugging a monitor, and none of them is visible to anyone testing on
/// one screen — which is the whole reason the rule is arithmetic and not a heuristic
/// in the view.
/// </summary>
public class WindowPlacementTests
{
    private static readonly PlacementRect Primary = new(0, 0, 1920, 1080);
    private static readonly PlacementRect Secondary = new(-1920, 0, 1920, 1080);

    private static IReadOnlyList<PlacementRect> OneScreen => [Primary];
    private static IReadOnlyList<PlacementRect> TwoScreens => [Primary, Secondary];

    [Fact]
    public void A_window_fully_on_screen_is_restored_where_it_was()
    {
        WindowPlacement.Restore(100, 80, 1400, 900, OneScreen)
            .Should().Be(new PlacementRect(100, 80, 1400, 900));
    }

    [Fact]
    public void A_window_on_a_second_monitor_to_the_LEFT_is_restored()
    {
        // Negative coordinates are ordinary on Windows, and a naive "x >= 0" guard
        // would throw away every window on a left-hand monitor.
        WindowPlacement.Restore(-1800, 100, 1400, 900, TwoScreens)
            .Should().NotBeNull();
    }

    [Fact]
    public void A_window_on_a_monitor_that_is_gone_falls_back_to_centred()
    {
        // Saved on the left-hand monitor; that monitor is no longer connected.
        WindowPlacement.Restore(-1800, 100, 1400, 900, OneScreen)
            .Should().BeNull();
    }

    [Fact]
    public void A_corner_on_screen_is_not_enough()
    {
        // The case a top-left-corner test passes and the user cannot use: the corner
        // is on the primary screen, the body is out in space where a monitor used to be.
        var mostlyOff = WindowPlacement.Restore(1880, 1040, 1400, 900, OneScreen);

        mostlyOff.Should().BeNull("40x40 of a 1400x900 window is not a usable window");
    }

    [Fact]
    public void A_quarter_on_screen_is_enough_to_grab()
    {
        // Half the width and half the height overlap = a quarter of the area, exactly
        // at the threshold, and enough to drag back into view.
        WindowPlacement.Restore(1920 - 700, 1080 - 450, 1400, 900, OneScreen)
            .Should().NotBeNull();
    }

    // The literals are doubles on purpose: xUnit binds InlineData by reflection, and an
    // int cannot be converted to a double? — the four cases failed on the harness, not
    // on the subject.
    [Theory]
    [InlineData(null, 80d, 1400d, 900d)]
    [InlineData(100d, null, 1400d, 900d)]
    [InlineData(100d, 80d, null, 900d)]
    [InlineData(100d, 80d, 1400d, null)]
    public void A_partial_record_is_not_restored(double? x, double? y, double? w, double? h)
    {
        WindowPlacement.Restore(x, y, w, h, OneScreen).Should().BeNull();
    }

    [Fact]
    public void An_absurdly_small_saved_size_is_ignored()
    {
        // A minimised or mid-animation measurement, not an intent.
        WindowPlacement.Restore(100, 80, 120, 40, OneScreen).Should().BeNull();
    }

    [Fact]
    public void Nothing_is_restored_when_no_screens_are_reported()
    {
        WindowPlacement.Restore(100, 80, 1400, 900, []).Should().BeNull();
    }

    [Fact]
    public void NaN_never_reaches_the_window()
    {
        WindowPlacement.Restore(double.NaN, 80, 1400, 900, OneScreen).Should().BeNull();
    }

    // --- size-only fallback ---------------------------------------------------

    [Fact]
    public void When_the_position_is_rejected_the_SIZE_is_still_theirs()
    {
        // The monitor went away; their preference for a big window did not.
        WindowPlacement.RestoreSizeOnly(1400, 900, OneScreen)
            .Should().Be((1400d, 900d));
    }

    [Fact]
    public void A_size_from_a_bigger_monitor_is_clamped_to_what_exists_now()
    {
        // Saved on a 4K panel, reopened on a laptop: opening 3800 wide would put the
        // controls on both edges out of reach.
        WindowPlacement.RestoreSizeOnly(3800, 2000, [new PlacementRect(0, 0, 1920, 1080)])
            .Should().Be((1920d, 1080d));
    }

    [Fact]
    public void No_saved_size_means_no_opinion()
    {
        WindowPlacement.RestoreSizeOnly(null, null, OneScreen).Should().BeNull();
    }
}
