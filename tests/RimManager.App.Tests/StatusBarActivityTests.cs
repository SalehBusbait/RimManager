using RimManager.App.ViewModels;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Status bar zone 5 is <c>1a</c>'s ONLY background-progress surface. Guards that it
/// stays driven from one place, because it was not: of the five operations that block
/// for seconds, exactly one wrote to it, and that one wrote <c>"idle"</c> in its own
/// <c>finally</c> — over the top of the rescan it had itself started.
/// </summary>
public sealed class StatusBarActivityTests
{
    private static string ViewModel => RepoPaths.HubSource();

    /// <summary>
    /// One writer, exactly as <c>RmRowHeight</c> has one writer and the theme has one
    /// store. Every background operation claims the zone instead; the claim is what
    /// makes overlapping operations safe and makes the exception path free.
    /// </summary>
    [Fact]
    public void The_activity_zone_has_exactly_one_writer()
    {
        var writes = Regex.Matches(ViewModel, @"ActivityText\s*=(?!=)")
            .Select(m => m.Value)
            .ToList();

        writes.Should().HaveCount(1,
            "ActivityText must only be assigned in RefreshActivity — every operation " +
            "claims the zone with Activity(...) so that two running at once cannot " +
            "fight and a finally cannot clear someone else's work");
    }

    /// <summary>
    /// The two halves of zone 5 — the bar and the label — must come from the same
    /// fact. The bar was bound to <c>IsBusy</c>, which is the SCAN's re-entrancy guard
    /// (Import reads it too), so it appeared for the scan and for nothing else: not
    /// the update check, not the rules sync, and not the conflict scan, which is the
    /// slowest thing in the app and the one that most looked like a hang.
    /// </summary>
    [Fact]
    public void The_activity_progress_bar_follows_the_same_claim_as_its_label()
    {
        var markup = File.ReadAllText(Path.Combine(
            RepoPaths.AppProject, "Views", "Shell", "StatusBarView.axaml"));

        markup.Should().Contain("IsVisible=\"{Binding HasActivity}\"",
            "the bar shows whenever any operation holds a claim");
        markup.Should().NotContain("IsVisible=\"{Binding IsBusy}\"",
            "IsBusy means 'a scan is running', which is not the same question");
    }

    // --- Follow, and the way back (Bug 2) -------------------------------------

    /// <summary>
    /// "Jump to newest" appears only when it has something to offer. Follow disarms
    /// itself the moment you scroll up — which is right, a log that yanks you back
    /// while you are reading is worse than one that never follows — but it disarmed
    /// SILENTLY, and lines kept arriving below the viewport with nothing saying so.
    /// </summary>
    [Theory]
    [InlineData(true, true, false)]    // following, at the end   -> nothing to offer
    [InlineData(true, false, false)]   // following, catching up  -> it will get there itself
    [InlineData(false, true, false)]   // stopped, already at end -> nothing to jump to
    [InlineData(false, false, true)]   // stopped, newest off screen -> the one case
    public void The_way_back_is_offered_only_when_there_is_somewhere_to_go(
        bool following, bool atEnd, bool expected)
    {
        ActivityJump.CanJump(following, atEnd).Should().Be(expected);
    }
}
