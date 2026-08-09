using System;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The words for each drift state — the status bar's S-DRIFT zone since the UI audit
/// moved the display out of the pane footer.
/// <para>
/// Deliberately not a test that reads the recomputed property: <c>MainWindowViewModel</c>
/// cannot be constructed under test, and a test that reads a computed value passes just as
/// happily when nothing announces it — which is precisely how this class of bug has shipped
/// here before. The notification is made structural instead (<c>[ObservableProperty]</c> on
/// <c>_drift</c>, generated and therefore unforgettable); what a test can hold is the
/// mapping, and this holds it.
/// </para>
/// </summary>
public sealed class DriftIndicatorTests
{
    private static DriftKind[] All => Enum.GetValues<DriftKind>();

    private static readonly DateTimeOffset At =
        new(2026, 8, 8, 1, 53, 0, TimeSpan.Zero);

    /// <summary>
    /// S-DRIFT's in-sync state SPEAKS now — "the timestamp is the information". The
    /// old silence was justified by "no applied timestamp is persisted", which had
    /// gone stale: Modlist.LastAppliedUtc has been stamped since the migration.
    /// </summary>
    [Fact]
    public void In_sync_states_the_applied_time()
    {
        DriftIndicator.Zone(DriftKind.InSync, At).Should().Be("Applied 01:53");
    }

    [Fact]
    public void In_sync_without_a_stamp_says_in_sync_rather_than_inventing_a_time()
    {
        DriftIndicator.Zone(DriftKind.InSync, null).Should().Be("In sync",
            "a list last applied before the stamp existed has no time to show, and a "
            + "made-up one would be a lie wearing a clock");
    }

    [Fact]
    public void Every_state_says_something()
    {
        foreach (var kind in All)
        {
            DriftIndicator.Zone(kind, At).Should().NotBeEmpty(
                $"{kind} is a state the user has to be able to see");
            DriftIndicator.ZoneTip(kind).Should().NotBeEmpty(
                "the zone's tooltip carries the long sentence");
        }
    }

    /// <summary>
    /// The §0e decision, pinned as a string check because it is the whole point of the
    /// change. Nothing in this app has unsaved state: the modlist commits on every edit.
    /// </summary>
    [Fact]
    public void No_state_calls_anything_unsaved()
    {
        foreach (var kind in All)
        {
            DriftIndicator.Zone(kind, At).Should().NotContainEquivalentOf("unsaved");
            DriftIndicator.ZoneTip(kind).Should().NotContainEquivalentOf("unsaved");
            DriftIndicator.ApplyFlyout(kind, 42).Should().NotContainEquivalentOf("unsaved");
        }
    }

    /// <summary>
    /// The four states are four different sentences. Collapsing any pair is the failure
    /// this copy exists to avoid — most of all <see cref="DriftKind.ChangedOutsideRimManager"/>,
    /// the one state where the next Apply overwrites what RimWorld itself just wrote.
    /// </summary>
    [Fact]
    public void The_states_are_told_apart()
    {
        All.Select(k => DriftIndicator.Zone(k, At)).Distinct().Should().HaveCount(All.Length);
        All.Select(DriftIndicator.ZoneTip).Distinct().Should().HaveCount(All.Length);
        All.Select(k => DriftIndicator.ApplyFlyout(k, 1)).Distinct().Should().HaveCount(All.Length);
    }

    /// <summary>
    /// "Never applied" is a fact about the list, not a claim about the user. It is also the
    /// state every brand-new modlist starts in, so it has to read as ordinary.
    /// </summary>
    [Fact]
    public void Never_applied_is_not_confused_with_edited()
    {
        DriftIndicator.Zone(DriftKind.Unknown, At).Should().ContainEquivalentOf("never");
        DriftIndicator.Zone(DriftKind.Unknown, At).Should()
            .NotBe(DriftIndicator.Zone(DriftKind.PendingApply, At));
    }

    /// <summary>The changed-outside state advertises its click ("· Review"), because
    /// S-DRIFT makes the zone a click target in exactly that state and edited.</summary>
    [Fact]
    public void Changed_outside_offers_the_review()
    {
        DriftIndicator.Zone(DriftKind.ChangedOutsideRimManager, At)
            .Should().ContainEquivalentOf("review");
    }

    /// <summary>
    /// The flyout line keeps the count of what would be written — that is the thing about
    /// to happen — and replaces the installed count, which answered a different question.
    /// </summary>
    [Fact]
    public void The_apply_flyout_states_what_would_be_written()
    {
        DriftIndicator.ApplyFlyout(DriftKind.PendingApply, 548).Should().StartWith("548 active");
    }

    /// <summary>
    /// A new <see cref="DriftKind"/> must choose its words rather than inherit the
    /// in-sync arm from the switch's default — the same allow-list discipline N2 used
    /// for relational versus intrinsic validation codes.
    /// </summary>
    [Fact]
    public void The_vocabulary_is_closed()
    {
        All.Should().BeEquivalentTo(new[]
        {
            DriftKind.InSync,
            DriftKind.PendingApply,
            DriftKind.ChangedOutsideRimManager,
            DriftKind.Unknown,
        }, "a state added later must pick its own copy in DriftIndicator, not fall through "
           + "the default arm and silently claim the list is already applied");
    }
}
