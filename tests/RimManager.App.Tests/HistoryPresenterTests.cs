using System.Collections.Immutable;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The History tab (<c>2d</c>). History is append-only, and the CHANGE column has to
/// agree with the diff panel beside it — a mismatch there reads as a bug in the
/// snapshot store rather than in the formatting.
/// </summary>
public sealed class HistoryPresenterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 6, 0, TimeSpan.Zero);

    private static ModlistState State(params string[] ids) =>
        new([.. ids.Select(id => ModlistEntry.Mod(ModId.From(id)))]);

    private static ModlistSnapshot Snap(
        string id, DateTimeOffset at, string reason, ModlistState state,
        string? name = null) =>
        new()
        {
            Id = id,
            ModlistId = "p",
            TakenUtc = at,
            Reason = reason,
            Name = name,
            State = state,
        };

    private static readonly Dictionary<ModId, string> Names = new()
    {
        [ModId.From("a")] = "Alpha",
        [ModId.From("b")] = "Beta",
        [ModId.From("c")] = "Gamma",
    };

    // --- rows ---------------------------------------------------------------

    /// <summary>
    /// Numbering runs oldest-to-newest so a number stays put as history grows. If it
    /// counted from the top, "#48" would mean a different state tomorrow.
    /// </summary>
    [Fact]
    public void Rows_are_numbered_from_the_oldest_state_upward()
    {
        var newestFirst = new[]
        {
            Snap("s3", Now, "manual", State("a", "b")),
            Snap("s2", Now.AddHours(-1), "manual", State("a")),
            Snap("s1", Now.AddHours(-2), "manual", State()),
        };

        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        rows.Select(r => r.Number).Should().Equal(3, 2, 1);
    }

    [Fact]
    public void The_change_column_reads_against_the_state_before_it()
    {
        var newestFirst = new[]
        {
            Snap("s2", Now, "manual", State("a", "b", "c")),
            Snap("s1", Now.AddHours(-1), "manual", State("a")),
        };

        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        rows[0].Change.Should().Be("+2");
        rows[1].Change.Should().Be("—", "the oldest state has nothing to compare against");
    }

    [Fact]
    public void A_pure_reorder_reads_as_plus_minus_zero_and_a_move_count()
    {
        var before = State("a", "b", "c");
        var after = State("c", "b", "a");

        HistoryPresenter.Change(ProfileDiff.Between(before, after))
            .Should().Match("±0 · * moved");
    }

    [Fact]
    public void An_unchanged_step_is_a_dash_not_a_zero()
    {
        HistoryPresenter.Change(ProfileDiff.Between(State("a"), State("a"))).Should().Be("—");
    }

    /// <summary>
    /// Expectations are computed from the same local conversion the presenter uses:
    /// hard-coding "12:06 today" passes only in the timezone it was written in, which
    /// is a test that fails for someone else on a Tuesday.
    /// </summary>
    [Fact]
    public void When_reads_as_recency_before_it_reads_as_a_date()
    {
        HistoryPresenter.When(Now, Now)
            .Should().Be($"{Now.ToLocalTime():HH:mm} today");

        HistoryPresenter.When(Now.AddDays(-1), Now).Should().Be("yesterday");

        var older = Now.AddDays(-4);
        HistoryPresenter.When(older, Now).Should().Be($"{older.ToLocalTime():MMM d}");
    }

    /// <summary>
    /// The restore path writes "restored &lt;snapshotId&gt;". Switching on the whole
    /// reason put a raw 27-character id in the widest column of the table.
    /// </summary>
    [Fact]
    public void A_reason_that_carries_a_payload_never_leaks_the_payload_into_the_table()
    {
        var rows = HistoryPresenter.BuildRows(
            [Snap("s1", Now, "restored 0198234567890123456-ab12cd34", State("a"))],
            new Dictionary<string, long>(), Now);

        rows[0].Action.Should().Be("Restored a state");
        rows[0].Action.Should().NotContain("0198");
        rows[0].Detail.Should().Be("created as new state");
    }

    [Fact]
    public void The_automatic_snapshot_before_a_restore_says_what_it_is()
    {
        var rows = HistoryPresenter.BuildRows(
            [Snap("s1", Now, "pre-restore", State("a"))], new Dictionary<string, long>(), Now);

        rows[0].Action.Should().Be("Before restoring");
    }

    // --- the filter chips ---------------------------------------------------

    [Fact]
    public void Applied_only_keeps_the_states_that_reached_the_game()
    {
        var newestFirst = new[]
        {
            Snap("s3", Now, "apply", State("a")),
            Snap("s2", Now.AddHours(-1), "manual", State("a")),
            Snap("s1", Now.AddHours(-2), "sort", State("a")),
        };
        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        HistoryPresenter.Filter(rows, HistoryFilter.AppliedOnly)
            .Should().ContainSingle().Which.Action.Should().Be("Applied to game");
    }

    /// <summary>
    /// The Named chip promises to list exactly the states that survive a prune, and
    /// since O26 that is one condition: it has a name. There is no second route in —
    /// pinning was retired precisely because it produced no outcome naming did not.
    /// </summary>
    [Fact]
    public void Named_lists_exactly_the_states_that_survive_a_prune()
    {
        var newestFirst = new[]
        {
            Snap("s3", Now, "manual", State("a"), name: "Before CE"),
            Snap("s2", Now.AddHours(-1), "manual", State("a"), name: "  "),
            Snap("s1", Now.AddHours(-2), "manual", State("a")),
        };
        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        HistoryPresenter.Filter(rows, HistoryFilter.Named)
            .Should().ContainSingle("only s3 has a real name — whitespace is not one")
            .Which.Action.Should().Be("Before CE");
        rows.Where(r => r.IsProtected).Should().ContainSingle(
            "the filter and the prune exemption must agree, or the chip lies about "
            + "what it is showing");
    }

    [Fact]
    public void A_named_state_shows_its_name_as_the_action()
    {
        var rows = HistoryPresenter.BuildRows(
            [Snap("s1", Now, "manual", State("a"), name: "Before CE 1.7 test")],
            new Dictionary<string, long>(), Now);

        rows[0].Action.Should().Be("Before CE 1.7 test");
        rows[0].IsProtected.Should().BeTrue("naming a state is what exempts it from pruning");
    }

    // --- the detail panel ---------------------------------------------------

    [Fact]
    public void A_move_carries_its_from_to_and_a_signed_delta()
    {
        var newestFirst = new[]
        {
            Snap("s2", Now, "sort", State("c", "a", "b")),
            Snap("s1", Now.AddHours(-1), "manual", State("a", "b", "c")),
        };
        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        var detail = HistoryPresenter.BuildDetail(rows[0], newestFirst, Names, "1.6", "none");

        detail.Changes.Should().NotBeEmpty();
        detail.Changes.Where(c => c.IsMove).Should().OnlyContain(c => c.From.Contains("→"));
        detail.Changes.Where(c => c.IsMove).Should().OnlyContain(c => c.Delta.Length > 0);
    }

    /// <summary>
    /// A sort moves most of the list. 71 rows of "moved" is not a diff, it is a wall,
    /// so the panel caps and says how many it is holding back.
    /// </summary>
    [Fact]
    public void A_long_move_list_is_capped_and_says_how_many_it_hid()
    {
        var ordered = Enumerable.Range(0, 30).Select(i => $"m{i}").ToArray();
        var reversed = ordered.Reverse().ToArray();
        var newestFirst = new[]
        {
            Snap("s2", Now, "sort", State(reversed)),
            Snap("s1", Now.AddHours(-1), "manual", State(ordered)),
        };
        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        var capped = HistoryPresenter.BuildDetail(rows[0], newestFirst, Names, "1.6", "none");
        capped.Changes.Count(c => c.IsMove).Should().Be(HistoryPresenter.MaxChangeLines);
        capped.HasHidden.Should().BeTrue();

        var expanded = HistoryPresenter.BuildDetail(rows[0], newestFirst, Names, "1.6", "none", showAll: true);
        expanded.Changes.Count(c => c.IsMove).Should().BeGreaterThan(HistoryPresenter.MaxChangeLines);
        expanded.HasHidden.Should().BeFalse();
    }

    [Fact]
    public void The_oldest_state_says_there_is_nothing_before_it()
    {
        var newestFirst = new[] { Snap("s1", Now, "manual", State("a")) };
        var rows = HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now);

        var detail = HistoryPresenter.BuildDetail(rows[0], newestFirst, Names, "1.6", "none");

        detail.Changes.Should().BeEmpty();
        detail.Paragraph.Should().Contain("nothing before it");
    }

    [Fact]
    public void Size_is_read_from_disk_and_absent_size_is_a_dash()
    {
        var newestFirst = new[] { Snap("s1", Now, "manual", State("a")) };

        HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long> { ["s1"] = 62 * 1024 }, Now)[0]
            .Size.Should().Be("62 KB");
        HistoryPresenter.BuildRows(newestFirst, new Dictionary<string, long>(), Now)[0]
            .Size.Should().Be("—");
    }

    [Fact]
    public void The_toolbar_states_the_count_and_the_total_on_disk()
    {
        var sizes = new Dictionary<string, long> { ["a"] = 1024 * 1024, ["b"] = 2L * 1024 * 1024 };

        HistoryPresenter.Total(48, sizes).Should().Be("48 snapshots · 3 MB");
    }
}
