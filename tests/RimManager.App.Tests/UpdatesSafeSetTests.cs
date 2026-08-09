using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// <c>2b</c>'s header checkbox: "tri-state and <b>only ever selects the safe set</b> —
/// never a pre-release, never a mod with uncommitted local edits".
/// <para>
/// This is the highest-consequence rule in the tab and the least visible: a "select
/// all" that quietly swept up a release candidate, or overwrote a folder someone was
/// editing, looks exactly like one that did not.
/// </para>
/// </summary>
public sealed class UpdatesSafeSetTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static UpdateRowViewModel Row(
        string name, UpdateStatus status, string? version = "1.2.0", ModSnooze? snooze = null) =>
        new(new ModUpdateStatus
        {
            Id = ModId.From(name),
            Name = name,
            Status = status,
            InstalledVersion = version,
        }, Now, snooze);

    [Fact]
    public void Only_a_plain_available_update_is_safe_to_batch()
    {
        UpdatesPresenter.IsSafeToBatch(Row("ok", UpdateStatus.UpdateAvailable)).Should().BeTrue();
        UpdatesPresenter.IsSafeToBatch(Row("current", UpdateStatus.UpToDate)).Should().BeFalse();
        UpdatesPresenter.IsSafeToBatch(Row("gone", UpdateStatus.Delisted)).Should().BeFalse();
        UpdatesPresenter.IsSafeToBatch(Row("unknown", UpdateStatus.NotTracked)).Should().BeFalse();
    }

    [Theory]
    [InlineData("1.7.0-rc1")]
    [InlineData("2.0-beta")]
    [InlineData("0.9-alpha3")]
    [InlineData("3.1-pre")]
    public void A_pre_release_is_never_swept_up_by_the_header(string version)
    {
        var row = Row("rc", UpdateStatus.UpdateAvailable, version);

        row.IsPreRelease.Should().BeTrue();
        UpdatesPresenter.IsSafeToBatch(row).Should().BeFalse();
        row.StatusText.Should().Be("pre-release", "the state column has to say why it was skipped");
    }

    [Fact]
    public void A_snoozed_row_is_not_in_the_safe_set()
    {
        var snooze = new ModSnooze(ModId.From("z"), SnoozeKind.OneWeek, Now);
        UpdatesPresenter.IsSafeToBatch(Row("z", UpdateStatus.UpdateAvailable, snooze: snooze))
            .Should().BeFalse();
    }

    /// <summary>
    /// A hand-ticked unsafe row must not make the header read "all" — the header only
    /// ever speaks for the safe set, so All has to mean "every safe row", not
    /// "everything ticked".
    /// </summary>
    [Fact]
    public void A_hand_ticked_pre_release_does_not_make_the_header_read_all()
    {
        var safe = Row("safe", UpdateStatus.UpdateAvailable);
        var rc = Row("rc", UpdateStatus.UpdateAvailable, "1.7.0-rc1");

        safe.IsSelected = true;
        rc.IsSelected = true;

        UpdatesPresenter.HeaderState([safe, rc]).Should().Be(TriState.All);

        safe.IsSelected = false;
        UpdatesPresenter.HeaderState([safe, rc]).Should().Be(TriState.None,
            "no safe row is ticked, whatever the user did to the unsafe one");
    }

    [Fact]
    public void The_header_reads_some_when_only_part_of_the_safe_set_is_ticked()
    {
        var a = Row("a", UpdateStatus.UpdateAvailable);
        var b = Row("b", UpdateStatus.UpdateAvailable);
        a.IsSelected = true;

        UpdatesPresenter.HeaderState([a, b]).Should().Be(TriState.Some);
    }

    [Fact]
    public void With_nothing_updatable_the_header_is_unchecked_not_indeterminate()
    {
        UpdatesPresenter.HeaderState([Row("x", UpdateStatus.UpToDate)]).Should().Be(TriState.None);
    }

    // --- what earns a row ---------------------------------------------------

    /// <summary>
    /// The table is a worklist, not an inventory. A real install checks ~365 mods and
    /// 344 have nothing to say; listing them buries the ones that need a decision,
    /// which is the tab's whole job undone. The totals stay in the summary line.
    /// </summary>
    [Fact]
    public void Only_rows_that_need_a_decision_earn_a_place_in_the_table()
    {
        Show(UpdateStatus.UpdateAvailable).Should().BeTrue();
        Show(UpdateStatus.Delisted).Should().BeTrue("a delisted mod is a decision");
        Show(UpdateStatus.UpToDate).Should().BeFalse();
        Show(UpdateStatus.NotTracked).Should().BeFalse();

        static bool Show(UpdateStatus status) => UpdatesPresenter.IsWorthShowing(
            new ModUpdateStatus { Id = ModId.From("m"), Name = "m", Status = status },
            isSnoozed: false);
    }

    /// <summary>
    /// A snoozed row stays visible. The user asked to be quiet about it, not to lose
    /// it — un-snoozing has to be reachable from somewhere.
    /// </summary>
    [Fact]
    public void A_snoozed_row_is_still_listed()
    {
        UpdatesPresenter.IsWorthShowing(
            new ModUpdateStatus { Id = ModId.From("m"), Name = "m", Status = UpdateStatus.UpToDate },
            isSnoozed: true)
            .Should().BeTrue();
    }

    // --- column formatting --------------------------------------------------

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(4, "4 hours ago")]
    [InlineData(24, "yesterday")]
    [InlineData(48, "2 days ago")]
    [InlineData(24 * 8, "a week ago")]
    [InlineData(24 * 20, "2 weeks ago")]
    public void Published_reads_as_recency_not_as_a_date(int hoursAgo, string expected)
    {
        UpdatesPresenter.Published(Now.AddHours(-hoursAgo), Now).Should().Be(expected);
    }

    [Fact]
    public void An_unknown_publish_time_is_a_dash_not_a_guess()
    {
        UpdatesPresenter.Published(null, Now).Should().Be("—");
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(512L, "512 B")]
    [InlineData(44L * 1024, "44 KB")]
    [InlineData(318L * 1024 * 1024, "318 MB")]
    public void Size_uses_steams_own_units(long? bytes, string expected)
    {
        UpdatesPresenter.Size(bytes).Should().Be(expected);
    }

    // The LATEST column is DELETED (T6): Steam publishes an update time, never a
    // version, so the column could only ever print a dash. PUBLISHED is the signal,
    // and the row no longer carries a LatestVersion to mis-print.

    [Fact]
    public void The_selection_summary_counts_only_updatable_rows_as_the_total()
    {
        var a = Row("a", UpdateStatus.UpdateAvailable);
        var b = Row("b", UpdateStatus.UpdateAvailable);
        var c = Row("c", UpdateStatus.UpToDate);
        a.IsSelected = true;

        UpdatesPresenter.SelectionSummary([a, b, c]).Should().Be("1 of 2 selected");
    }
}
