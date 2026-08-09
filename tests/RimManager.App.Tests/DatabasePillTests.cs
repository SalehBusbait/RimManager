using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

public class DatabasePillTests
{
    [Fact]
    public void On_with_data_is_active_and_green()
    {
        var pill = DatabasePill.For(enabled: true, count: 629);
        pill.Text.Should().Be("active");
        pill.IsOn.Should().BeTrue();
        pill.IsWarn.Should().BeFalse();
    }

    [Fact]
    public void On_but_empty_is_the_one_state_that_wants_something_so_it_warns()
    {
        var pill = DatabasePill.For(enabled: true, count: 0);
        pill.Text.Should().Be("not synced");
        pill.IsOn.Should().BeFalse();
        pill.IsWarn.Should().BeTrue();
    }

    [Fact]
    public void Off_is_neutral_because_a_choice_is_not_a_problem()
    {
        // Even with cached data on disk: off means the data is not IN USE, and the
        // pill reports use, not possession.
        var pill = DatabasePill.For(enabled: false, count: 2648);
        pill.Text.Should().Be("off");
        pill.IsOn.Should().BeFalse();
        pill.IsWarn.Should().BeFalse();
        pill.IsBad.Should().BeFalse();
    }

    /// <summary>
    /// S-INTEG's fourth state: a sync that ERRORED is different news from an upstream
    /// not yet asked, and the two used to wear one amber. The message rides the
    /// tooltip so the alarm carries its sentence.
    /// </summary>
    [Fact]
    public void A_failed_sync_is_bad_and_carries_its_message_even_over_cached_data()
    {
        var pill = DatabasePill.For(enabled: true, count: 629, syncError: "HTTP 500");
        pill.Text.Should().Be("sync failed");
        pill.IsBad.Should().BeTrue();
        pill.IsOn.Should().BeFalse();
        pill.IsWarn.Should().BeFalse();
        pill.Tip.Should().Be("HTTP 500",
            "a pill that said 'failed' without saying why would be an alarm with no sentence");
    }

    [Fact]
    public void Off_beats_error_because_a_database_not_in_use_has_no_news()
    {
        var pill = DatabasePill.For(enabled: false, count: 0, syncError: "HTTP 500");
        pill.Text.Should().Be("off");
        pill.IsBad.Should().BeFalse();
    }
}
