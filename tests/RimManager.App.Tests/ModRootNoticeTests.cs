using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Scanning;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// What the strip says. <see cref="ModRootProbe"/> reports deltas; this accumulates them into
/// the running total since the last rescan, which is what the user actually needs to read.
/// </summary>
public sealed class ModRootNoticeTests
{
    private static ModRootChanges Added(params string[] names) =>
        new([.. names], [], 0);

    private static ModRootChanges Removed(params string[] names) =>
        new([], [.. names], 0);

    [Fact]
    public void Nothing_to_say_until_something_happens()
    {
        var notice = new ModRootNotice();

        notice.HasNews.Should().BeFalse();
        notice.Text.Should().BeEmpty();
    }

    /// <summary>
    /// The singular reads as a sentence rather than as a count. "1 added on disk" is what a
    /// spreadsheet says.
    /// </summary>
    [Fact]
    public void One_arrival_is_named_as_a_mod()
    {
        var notice = new ModRootNotice();
        notice.Record(Added("2109043747"));

        notice.Text.Should().Be("1 mod added on disk");
    }

    /// <summary>
    /// The reason this type exists. A collection landing mod by mod would otherwise flicker
    /// "1 mod added" over and over and never say twelve.
    /// </summary>
    [Fact]
    public void Arrivals_across_several_polls_accumulate()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("a"));
        notice.Record(Added("b", "c"));
        notice.Record(Added("d"));

        notice.AddedCount.Should().Be(4);
        notice.Text.Should().Be("4 added on disk");
    }

    [Fact]
    public void Both_directions_are_reported_together()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("a", "b", "c"));
        notice.Record(Removed("z"));

        notice.Text.Should().Be("3 added, 1 removed on disk");
    }

    /// <summary>
    /// A mod that arrives and leaves again before the rescan is not news: nothing about what
    /// is on screen changed. "1 added, 1 removed" about the same folder is arithmetic, not
    /// information.
    /// </summary>
    [Fact]
    public void A_mod_that_arrives_and_leaves_again_cancels()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("tryingitout"));
        notice.Record(Removed("tryingitout"));

        notice.HasNews.Should().BeFalse();
        notice.Text.Should().BeEmpty();
    }

    /// <summary>And the same the other way round — uninstalled, then reinstalled.</summary>
    [Fact]
    public void A_mod_that_leaves_and_comes_back_cancels()
    {
        var notice = new ModRootNotice();

        notice.Record(Removed("secondthoughts"));
        notice.Record(Added("secondthoughts"));

        notice.HasNews.Should().BeFalse();
    }

    /// <summary>Cancelling one pair must not take the unrelated news with it.</summary>
    [Fact]
    public void Cancelling_one_mod_leaves_the_others_alone()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("keeper", "fleeting"));
        notice.Record(Removed("fleeting"));

        notice.AddedCount.Should().Be(1);
        notice.Text.Should().Be("1 mod added on disk");
    }

    /// <summary>The same mod reported twice is one mod.</summary>
    [Fact]
    public void The_same_name_is_not_counted_twice()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("dupe"));
        notice.Record(Added("dupe"));

        notice.AddedCount.Should().Be(1);
    }

    /// <summary>Workshop ids are digits, but a local folder is whatever the user named it.</summary>
    [Fact]
    public void Names_are_matched_case_insensitively()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("MyLocalMod"));
        notice.Record(Removed("mylocalmod"));

        notice.HasNews.Should().BeFalse("Windows would call those the same folder");
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        var notice = new ModRootNotice();
        notice.Record(Added("a", "b"));

        notice.Clear();

        notice.HasNews.Should().BeFalse();
        notice.Text.Should().BeEmpty();
    }

    /// <summary>The count goes on screen; the names go in the log.</summary>
    [Fact]
    public void The_detail_names_what_the_count_only_totals()
    {
        var notice = new ModRootNotice();

        notice.Record(Added("1234"));
        notice.Record(Removed("5678"));

        notice.Detail.Should().Contain("+1234").And.Contain("-5678");
    }
}
