using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>O9 · what the active list does while a filter is narrowing it.</summary>
public class FilteredListOpsTests
{
    private static ModRowViewModel Mod(string id, bool filteredOut = false) =>
        new(new Mod
        {
            PackageId = ModId.From(id), Name = id,
            Source = ModSource.Workshop, RootPath = "/" + id,
        })
        { IsFilteredOut = filteredOut };

    private static SeparatorRowViewModel Sep(string name) => new($"sep-{name}", name);

    // --- separators over an emptied group ------------------------------------

    [Fact]
    public void A_separator_whose_whole_group_is_filtered_out_hides()
    {
        List<RowViewModel> rows =
            [Sep("Core"), Mod("a", filteredOut: true), Mod("b", filteredOut: true)];

        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);

        rows[0].IsFilteredOut.Should().BeTrue("a heading over nothing is a heading for nothing");
    }

    [Fact]
    public void A_separator_with_one_survivor_stays()
    {
        List<RowViewModel> rows =
            [Sep("Core"), Mod("a", filteredOut: true), Mod("b")];

        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);

        rows[0].IsFilteredOut.Should().BeFalse();
    }

    [Fact]
    public void Each_separator_is_judged_on_its_OWN_group()
    {
        // The positional rule: a separator owns the contiguous mods after it, up to the
        // next separator. The second group survives; the first must still hide.
        List<RowViewModel> rows =
        [
            Sep("Empty"), Mod("a", filteredOut: true),
            Sep("Kept"), Mod("b"),
        ];

        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);

        rows[0].IsFilteredOut.Should().BeTrue();
        rows[2].IsFilteredOut.Should().BeFalse();
    }

    [Fact]
    public void With_no_filter_running_an_empty_separator_stays()
    {
        // A separator the user has just made, with nothing dragged into it yet. Hiding it
        // would make creating one look like it failed.
        List<RowViewModel> rows = [Sep("New group")];

        ActiveListOps.ApplySeparatorVisibility(rows, filtering: false);

        rows[0].IsFilteredOut.Should().BeFalse();
    }

    [Fact]
    public void Clearing_the_filter_brings_a_hidden_separator_back()
    {
        List<RowViewModel> rows = [Sep("Core"), Mod("a", filteredOut: true)];
        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);
        rows[0].IsFilteredOut.Should().BeTrue();

        // Filter lifted: the mod matches again, and so must its heading.
        ((ModRowViewModel)rows[1]).IsFilteredOut = false;
        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);

        rows[0].IsFilteredOut.Should().BeFalse("the pass must be able to UN-hide, not only hide");
    }

    [Fact]
    public void A_collapsed_group_keeps_its_separator()
    {
        // Collapse hides the mods through IsCollapsedChild, not IsFilteredOut — and the
        // separator is then the only thing left to click to get them back.
        var child = Mod("a");
        child.IsCollapsedChild = true;
        List<RowViewModel> rows = [Sep("Collapsed"), child];

        ActiveListOps.ApplySeparatorVisibility(rows, filtering: true);

        rows[0].IsFilteredOut.Should().BeFalse();
    }

    // --- where an activated mod lands ----------------------------------------

    [Fact]
    public void With_no_filter_an_activated_mod_goes_to_the_end()
    {
        List<RowViewModel> rows = [Mod("a"), Mod("b"), Mod("c")];

        ActiveListOps.ActivationIndex(rows, filtering: false).Should().Be(3);
    }

    [Fact]
    public void While_filtering_it_lands_after_the_last_VISIBLE_mod()
    {
        // The end is below a run of hidden rows and off screen, so appending there made
        // the mod look like it vanished.
        List<RowViewModel> rows =
            [Mod("a"), Mod("b"), Mod("c", filteredOut: true), Mod("d", filteredOut: true)];

        ActiveListOps.ActivationIndex(rows, filtering: true).Should().Be(2);
    }

    [Fact]
    public void A_trailing_visible_mod_still_means_the_end()
    {
        List<RowViewModel> rows = [Mod("a", filteredOut: true), Mod("b")];

        ActiveListOps.ActivationIndex(rows, filtering: true).Should().Be(2);
    }

    [Fact]
    public void With_nothing_visible_it_falls_back_to_the_end()
    {
        List<RowViewModel> rows = [Mod("a", filteredOut: true), Mod("b", filteredOut: true)];

        ActiveListOps.ActivationIndex(rows, filtering: true).Should().Be(2);
    }

    [Fact]
    public void A_separator_is_never_the_anchor()
    {
        // Landing "after the last visible separator" would drop the mod into a group
        // heading's position rather than after a mod.
        List<RowViewModel> rows = [Mod("a"), Sep("Tail")];

        ActiveListOps.ActivationIndex(rows, filtering: true).Should().Be(1);
    }

    [Fact]
    public void An_empty_active_list_activates_at_zero()
    {
        ActiveListOps.ActivationIndex([], filtering: true).Should().Be(0);
    }
}
