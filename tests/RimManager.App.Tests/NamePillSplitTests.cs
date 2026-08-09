using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The NAME cell's negotiation. Each case here is a shape the layout got wrong
/// before the panel existed — the fixed 132px cap and the uncapped Auto column each
/// failed in the opposite direction.
/// </summary>
public class NamePillSplitTests
{
    private const double Reserve = 110;

    [Fact]
    public void A_short_name_on_a_wide_row_hands_the_pills_everything_left_over()
    {
        // The reported bug: "Loading Progress" collapsed to dots and "+2" with half
        // the row empty, because a 132px cap cannot see the row.
        var (name, pills) = NamePillSplit.Split(available: 404, nameWant: 95, pillWant: 380, Reserve);

        pills.Should().Be(309, "the pills get every pixel the name did not ask for");
        name.Should().Be(95);
        (name + pills).Should().Be(404, "the cell is spent exactly, never overrun");
    }

    [Fact]
    public void The_pills_never_take_more_than_they_want()
    {
        var (name, pills) = NamePillSplit.Split(available: 900, nameWant: 120, pillWant: 60, Reserve);

        pills.Should().Be(60);
        name.Should().Be(840, "unwanted width falls back to the name rather than padding the zone");
    }

    [Fact]
    public void When_both_want_more_than_there_is_the_name_keeps_half()
    {
        var (name, pills) = NamePillSplit.Split(available: 404, nameWant: 300, pillWant: 380, Reserve);

        name.Should().Be(202);
        pills.Should().Be(202);
    }

    [Fact]
    public void Below_twice_the_reserve_the_reserve_is_the_floor()
    {
        // Half of 200 is less than the reserve, so the name stops yielding here.
        var (name, pills) = NamePillSplit.Split(available: 200, nameWant: 300, pillWant: 380, Reserve);

        name.Should().Be(110);
        pills.Should().Be(90, "the zone still gets something — its own ladder degrades to +n");
    }

    [Fact]
    public void A_long_name_never_starves_the_pills_out_of_existence()
    {
        var (_, pills) = NamePillSplit.Split(available: 404, nameWant: 4000, pillWant: 70, Reserve);

        pills.Should().Be(70, "tags stay visible on a long name; that is what the reserve buys");
    }

    [Fact]
    public void An_untagged_row_gives_the_whole_cell_to_the_name()
    {
        var (name, pills) = NamePillSplit.Split(available: 404, nameWant: 300, pillWant: 0, Reserve);

        name.Should().Be(404, "no zone is reserved for pills that do not exist");
        pills.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_cell_with_no_width_allocates_nothing(double available)
    {
        NamePillSplit.Split(available, nameWant: 300, pillWant: 380, Reserve)
            .Should().Be((0d, 0d));
    }
}
