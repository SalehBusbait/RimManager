using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>O7/O8 · the bulk-tagging rules. The direction rule is the one that
/// matters: a partial row must ASSIGN, never clear.</summary>
public class TagAssignTests
{
    [Theory]
    [InlineData(0, 3, TagAssignState.None)]
    [InlineData(1, 3, TagAssignState.Some)]
    [InlineData(2, 3, TagAssignState.Some)]
    [InlineData(3, 3, TagAssignState.All)]
    [InlineData(1, 1, TagAssignState.All)]
    [InlineData(0, 1, TagAssignState.None)]
    public void The_tri_state_counts_how_many_of_the_selection_carry_it(
        int assigned, int total, TagAssignState expected) =>
        TagAssign.StateOf(assigned, total).Should().Be(expected);

    [Fact]
    public void An_empty_selection_is_never_partially_anything()
    {
        TagAssign.StateOf(0, 0).Should().Be(TagAssignState.None);
    }

    [Fact]
    public void Clicking_a_partial_row_assigns_rather_than_clears()
    {
        // The whole reason the tri-state exists. Clearing here would take the tag off
        // the rows that already had it, and those rows are off screen — there would be
        // nothing to notice.
        TagAssign.AssignsOnClick(TagAssignState.Some).Should().BeTrue();
    }

    [Fact]
    public void Clicking_an_empty_row_assigns_and_a_full_row_clears()
    {
        TagAssign.AssignsOnClick(TagAssignState.None).Should().BeTrue();
        TagAssign.AssignsOnClick(TagAssignState.All).Should().BeFalse();
    }

    [Fact]
    public void The_heading_names_the_count_when_it_is_more_than_one()
    {
        // The flyout hangs off ONE mod's info pane, so without this there is nothing
        // on screen to say the click will touch twelve.
        TagAssign.Heading(12).Should().Be("ASSIGN TO 12 MODS");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void One_mod_or_none_gets_the_plain_heading(int count) =>
        TagAssign.Heading(count).Should().Be("ASSIGN A TAG");

    [Fact]
    public void The_result_line_says_what_happened_to_how_many()
    {
        TagAssign.Result(assigned: true, 12, "Furniture")
            .Should().Be("Tagged 12 mods “Furniture”.");
        TagAssign.Result(assigned: false, 1, "Furniture")
            .Should().Be("Removed “Furniture” from 1 mod.");
    }

    [Fact]
    public void A_row_reports_how_many_of_the_selection_have_it()
    {
        var tag = new RimManager.Core.Domain.Tag { Id = "t1", Name = "Furniture", PaletteIndex = 2 };

        new TagAssignRowViewModel(tag, assignedCount: 1, selectionCount: 3)
            .CountText.Should().Be("1 of 3");
    }

    [Fact]
    public void A_single_selection_shows_no_count_because_the_tick_says_it_all()
    {
        var tag = new RimManager.Core.Domain.Tag { Id = "t1", Name = "Furniture", PaletteIndex = 2 };

        new TagAssignRowViewModel(tag, assignedCount: 1, selectionCount: 1)
            .CountText.Should().BeEmpty();
    }

    [Fact]
    public void A_fully_assigned_row_shows_no_count_either()
    {
        var tag = new RimManager.Core.Domain.Tag { Id = "t1", Name = "Furniture", PaletteIndex = 2 };
        var row = new TagAssignRowViewModel(tag, assignedCount: 3, selectionCount: 3);

        row.IsAll.Should().BeTrue();
        row.IsSome.Should().BeFalse();
        row.CountText.Should().BeEmpty();
    }
}
