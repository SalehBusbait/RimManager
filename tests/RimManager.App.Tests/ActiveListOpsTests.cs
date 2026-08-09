using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

public sealed class ActiveListOpsTests
{
    private static ModRowViewModel Mod(string id)
        => new(new Mod { PackageId = ModId.From(id), Name = id, Source = ModSource.Workshop, RootPath = "/" + id });

    private static SeparatorRowViewModel Sep(string name) => new($"sep-{name}", name);

    // --- where a new separator goes (N4e) -----------------------------------

    [Fact]
    public void A_new_separator_goes_above_the_selection()
    {
        var rows = new List<RowViewModel> { Mod("a"), Mod("b"), Mod("c") };

        ActiveListOps.SeparatorInsertIndex(rows, [rows[1]]).Should().Be(1,
            "a separator owns the rows AFTER it, so above the selection is the only side "
            + "on which the selected row lands inside the new group");
    }

    [Fact]
    public void Nothing_selected_puts_it_at_the_top()
    {
        var rows = new List<RowViewModel> { Mod("a"), Mod("b") };

        ActiveListOps.SeparatorInsertIndex(rows, []).Should().Be(0);
    }

    /// <summary>
    /// The TOPMOST selected row decides, whatever order the selection arrived in. A
    /// multi-row selection has no inherent order, and a group starting halfway down its
    /// own selection is not a group anyone asked for.
    /// </summary>
    [Fact]
    public void The_topmost_selected_row_decides_regardless_of_click_order()
    {
        var rows = new List<RowViewModel> { Mod("a"), Mod("b"), Mod("c"), Mod("d") };

        ActiveListOps.SeparatorInsertIndex(rows, [rows[3], rows[1], rows[2]]).Should().Be(1);
    }

    /// <summary>
    /// The two panes hold independent selections, so a row that is not in this list must
    /// be ignored rather than trusted — otherwise "+ Separator" would try to insert above
    /// something that is not there and silently land at the top instead.
    /// </summary>
    [Fact]
    public void Rows_from_the_other_pane_are_ignored()
    {
        var rows = new List<RowViewModel> { Mod("a"), Mod("b") };
        var elsewhere = Mod("inactive");

        ActiveListOps.SeparatorInsertIndex(rows, [elsewhere]).Should().Be(0);
        ActiveListOps.SeparatorInsertIndex(rows, [elsewhere, rows[1]]).Should().Be(1);
    }

    /// <summary>
    /// Identity, not equality: two separators can carry the same name, and matching on
    /// one would insert above the wrong header.
    /// </summary>
    [Fact]
    public void Matching_is_by_identity_not_by_name()
    {
        var rows = new List<RowViewModel> { Sep("Content"), Mod("a"), Sep("Content"), Mod("b") };

        ActiveListOps.SeparatorInsertIndex(rows, [rows[2]]).Should().Be(2);
    }

    [Fact]
    public void Selecting_the_first_row_still_inserts_above_it()
    {
        var rows = new List<RowViewModel> { Mod("a"), Mod("b") };

        ActiveListOps.SeparatorInsertIndex(rows, [rows[0]]).Should().Be(0);
    }

    [Fact]
    public void Renumber_numbers_mods_and_counts_separator_groups()
    {
        var rows = new List<RowViewModel>
        {
            Sep("Frameworks"), Mod("a"), Mod("b"),
            Sep("Content"), Mod("c"),
        };

        ActiveListOps.Renumber(rows);

        ((ModRowViewModel)rows[1]).Index.Should().Be(1);
        ((ModRowViewModel)rows[2]).Index.Should().Be(2);
        ((ModRowViewModel)rows[4]).Index.Should().Be(3);
        ((SeparatorRowViewModel)rows[0]).ModCount.Should().Be(2);
        ((SeparatorRowViewModel)rows[3]).ModCount.Should().Be(1);
    }

    [Fact]
    public void GroupExtent_spans_the_separator_and_its_mods()
    {
        var rows = new List<RowViewModel> { Sep("A"), Mod("a"), Mod("b"), Sep("B"), Mod("c") };

        ActiveListOps.GroupExtent(rows, 0).Should().Be((0, 3));
        ActiveListOps.GroupExtent(rows, 3).Should().Be((3, 2));
    }

    [Fact]
    public void ApplyCollapsed_hides_and_shows_child_rows_only()
    {
        var sep = Sep("A");
        var a = Mod("a");
        var other = Mod("other");
        var rows = new List<RowViewModel> { sep, a, Sep("B"), other };

        ActiveListOps.ApplyCollapsed(rows, sep, true);
        sep.Collapsed.Should().BeTrue();
        a.IsCollapsedChild.Should().BeTrue();
        other.IsCollapsedChild.Should().BeFalse("it belongs to a different group");

        ActiveListOps.ApplyCollapsed(rows, sep, false);
        a.IsCollapsedChild.Should().BeFalse();
    }

    [Fact]
    public void GroupMods_returns_only_the_group_members()
    {
        var sep = Sep("A");
        var a = Mod("a");
        var b = Mod("b");
        var outside = Mod("outside");
        var rows = new List<RowViewModel> { sep, a, b, Sep("B"), outside };

        ActiveListOps.GroupMods(rows, sep).Should().Equal(a, b);
    }

    // --- the drop rule (N2 · 3a) ---------------------------------------------

    private static ModRowViewModel Game(string id, ModSource source)
        => new(new Mod { PackageId = ModId.From(id), Name = id, Source = source, RootPath = "/" + id });

    /// <summary>Prepatcher, Harmony, Loading Progress, Better Stacktraces, then the game.</summary>
    private static List<RowViewModel> RealisticOrder() =>
    [
        Mod("zetrith.prepatcher"),
        Mod("brrainz.harmony"),
        Game("ludeon.rimworld", ModSource.Core),
        Game("ludeon.rimworld.royalty", ModSource.Dlc),
        Mod("some.mod"),
    ];

    /// <summary>
    /// The refusal this replaced said "Core must load first" and blocked every drop
    /// above the last Core/DLC row. The developer's own live ModsConfig.xml — the file
    /// the game itself reads — has four mods above ludeon.rimworld, and our own sorter
    /// puts them there. The app was refusing by hand the order it produces by itself.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void An_ordinary_mod_may_be_dropped_anywhere_including_above_the_game(int index)
    {
        ActiveListOps.InvalidDropReason(RealisticOrder(), index, Mod("some.mod"))
            .Should().BeNull();
    }

    /// <summary>
    /// What IS true: the base game and its expansions keep their own order, which
    /// Ludeon pins with forceLoadAfter/forceLoadBefore and the sorter restores anyway.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void The_game_and_its_expansions_may_not_be_dragged_out_of_their_block(int index)
    {
        ActiveListOps.InvalidDropReason(
                RealisticOrder(), index, Game("ludeon.rimworld", ModSource.Core))
            .Should().Be("The base game and its expansions keep their own order");
    }

    /// <summary>Inside its own block, or just after it, is a shuffle and not a break.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_dlc_may_still_move_within_the_block(int index)
    {
        ActiveListOps.InvalidDropReason(
                RealisticOrder(), index, Game("ludeon.rimworld.royalty", ModSource.Dlc))
            .Should().BeNull();
    }

    /// <summary>
    /// A list with no Core at all — a modlist for an install we cannot see — refuses
    /// nothing, rather than refusing everything because the block is empty.
    /// </summary>
    [Fact]
    public void With_no_game_rows_present_nothing_is_refused()
    {
        List<RowViewModel> rows = [Mod("a"), Mod("b")];

        ActiveListOps.InvalidDropReason(rows, 0, Game("ludeon.rimworld", ModSource.Core))
            .Should().BeNull();
    }

    /// <summary>Dragging a separator is never a game-order question.</summary>
    [Fact]
    public void A_separator_is_never_refused()
    {
        ActiveListOps.InvalidDropReason(RealisticOrder(), 0, Sep("A")).Should().BeNull();
    }

    /// <summary>
    /// THE separator-offset regression, pinned: the old guard compared the drop index
    /// against the displayed number, which disagrees by one per separator above — so
    /// dragging a mod ONE position up on any separated list silently no-opped, and
    /// the keyboard nudge with it.
    /// </summary>
    [Fact]
    public void Dropping_one_position_up_below_a_separator_is_not_the_same_spot()
    {
        var b = Mod("b");
        List<RowViewModel> rows = [Sep("Group"), Mod("a"), b, Mod("c")];

        // b sits at list index 2; the insertion point one up (above a) is 1.
        ActiveListOps.IsSameSpotDrop(rows, b, 1).Should().BeFalse(
            "one position up is a real move, whatever the displayed number says");
    }

    [Fact]
    public void Dropping_where_the_row_already_sits_is_the_same_spot_on_both_sides()
    {
        var b = Mod("b");
        List<RowViewModel> rows = [Sep("Group"), Mod("a"), b, Mod("c")];

        // Insertion points 2 (above itself) and 3 (below itself) both land it where
        // it already is — the 3a no-op contract.
        ActiveListOps.IsSameSpotDrop(rows, b, 2).Should().BeTrue();
        ActiveListOps.IsSameSpotDrop(rows, b, 3).Should().BeTrue();
        ActiveListOps.IsSameSpotDrop(rows, b, 4).Should().BeFalse("one down is a real move");
    }

    [Fact]
    public void A_row_not_in_the_list_is_never_the_same_spot()
    {
        List<RowViewModel> rows = [Mod("a"), Mod("b")];

        ActiveListOps.IsSameSpotDrop(rows, Mod("elsewhere"), 0).Should().BeFalse();
    }
}
