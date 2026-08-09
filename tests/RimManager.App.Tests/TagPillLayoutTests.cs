using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The pill zone's degradation ladder (v2 §4A.1): labels while they fit, then
/// dots, then "+n" — and EVERY tag is always represented one way or another.
/// </summary>
public sealed class TagPillLayoutTests
{
    private static double Plus(int n) => 12 + 6 * n.ToString().Length;

    [Fact]
    public void Everything_labelled_when_the_zone_fits()
    {
        var (labelled, dots, overflow) = TagPillLayout.Arrange([40, 40], 132, 4, 7, Plus);

        (labelled, dots, overflow).Should().Be((2, 0, 0));
    }

    [Fact]
    public void Overflowing_labels_degrade_to_dots()
    {
        var (labelled, dots, overflow) = TagPillLayout.Arrange([60, 60, 60], 132, 4, 7, Plus);

        labelled.Should().BeLessThan(3);
        (labelled + dots + overflow).Should().Be(3, "every tag is represented");
        overflow.Should().Be(0, "dots still fit here");
    }

    [Fact]
    public void A_tiny_budget_still_represents_every_tag()
    {
        var widths = Enumerable.Repeat(50d, 12).ToArray();
        var (labelled, dots, overflow) = TagPillLayout.Arrange(widths, 30, 4, 7, Plus);

        (labelled + dots + overflow).Should().Be(12);
        overflow.Should().BeGreaterThan(0, "thirty pixels cannot hold twelve dots");
    }

    [Fact]
    public void No_budget_means_everything_is_overflow()
    {
        var (labelled, dots, overflow) = TagPillLayout.Arrange([50, 50], 0, 4, 7, Plus);

        (labelled, dots, overflow).Should().Be((0, 0, 2));
    }

    [Fact]
    public void Taking_a_label_never_evicts_the_rest_from_representation()
    {
        // One wide label that would fit alone, but taking it must leave room for
        // the remainder's cheapest representation.
        var (labelled, dots, overflow) = TagPillLayout.Arrange([120, 40], 132, 4, 7, Plus);

        (labelled + dots + overflow).Should().Be(2);
        if (labelled == 1) (dots + overflow).Should().Be(1);
    }
}
