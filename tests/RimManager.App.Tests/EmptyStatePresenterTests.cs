using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// 3e: "Every one names a next action; none is a shrug." These tests are mostly
/// about not LYING — the states are easy to confuse, and the wrong one is worse
/// than none.
/// </summary>
public sealed class EmptyStatePresenterTests
{
    [Fact]
    public void A_list_with_visible_rows_shows_no_empty_state() =>
        EmptyStatePresenter.For(totalRows: 10, visibleRows: 10, anyModsInstalled: true)
            .Should().Be(EmptyState.None);

    /// <summary>
    /// Nothing scanned is a "not yet", never a success. Telling someone everything is
    /// fine when the app has not found their mods is a lie.
    /// </summary>
    [Fact]
    public void Nothing_installed_is_not_yet_rather_than_success() =>
        EmptyStatePresenter.For(0, 0, anyModsInstalled: false).Should().Be(EmptyState.NotYet);

    /// <summary>
    /// The one that matters most: rows exist, a filter hides them all. Reporting
    /// that as "everything is active" is actively misleading.
    /// </summary>
    [Fact]
    public void Rows_hidden_entirely_by_filters_report_as_filtered_not_success() =>
        EmptyStatePresenter.For(totalRows: 128, visibleRows: 0, anyModsInstalled: true)
            .Should().Be(EmptyState.FilteredOut);

    [Fact]
    public void A_genuinely_empty_list_with_mods_installed_is_success() =>
        EmptyStatePresenter.For(totalRows: 0, visibleRows: 0, anyModsInstalled: true)
            .Should().Be(EmptyState.Success);

    // --- naming the filters --------------------------------------------------

    [Fact]
    public void No_filters_produces_no_text() =>
        EmptyStatePresenter.DescribeFilters(null, false, 0).Should().BeEmpty();

    [Fact]
    public void A_single_filter_reads_as_one_clause() =>
        EmptyStatePresenter.DescribeFilters("vehicel", false, 0)
            .Should().Be("search \u201cvehicel\u201d is narrowing this list.");

    /// <summary>A stale tag filter from ten minutes ago is exactly the case this
    /// text exists for — it must be named, not left to be rediscovered.</summary>
    [Fact]
    public void Forgotten_tag_filters_are_named()
    {
        var text = EmptyStatePresenter.DescribeFilters("vehicel", false, 2);

        text.Should().Contain("2 tag filters");
        text.Should().Contain("vehicel");
    }

    [Fact]
    public void Several_filters_read_as_a_list_with_and()
    {
        var text = EmptyStatePresenter.DescribeFilters(null, warningsOnly: true, tagFilters: 2);

        text.Should().Be("2 tag filters and Warnings are narrowing this list.");
    }

    [Fact]
    public void One_tag_filter_is_singular() =>
        EmptyStatePresenter.DescribeFilters(null, false, 1)
            .Should().Contain("1 tag filter is");
}
