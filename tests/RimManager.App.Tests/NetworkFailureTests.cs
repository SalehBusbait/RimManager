using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Abstractions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// 2k's offline state rests entirely on one distinction: the request got no answer,
/// versus a server answered and said no. Get it wrong in the second direction and a
/// 404 for one delisted mod puts the whole app into an offline state it cannot leave.
/// </summary>
public sealed class NetworkFailureTests
{
    [Fact]
    public void A_server_that_answered_is_proof_we_are_online()
    {
        var replied = new HttpFetchException("https://steam/api", 404, "Not Found");

        NetworkFailure.IsConnectivity(replied).Should().BeFalse(
            "Steam answering 404 for one delisted mod is not a network outage");
    }

    [Fact]
    public void A_request_that_never_landed_is_connectivity()
    {
        NetworkFailure.IsConnectivity(new HttpRequestException("no such host")).Should().BeTrue();
        NetworkFailure.IsConnectivity(new SocketException(10051)).Should().BeTrue();
        NetworkFailure.IsConnectivity(new TimeoutException()).Should().BeTrue();
    }

    /// <summary>
    /// An HttpClient timeout surfaces as TaskCanceledException with nothing cancelled.
    /// A user who closed the wizard mid-fetch carries a cancelled token, and that must
    /// not raise an offline strip over their own click.
    /// </summary>
    [Fact]
    public void A_timeout_is_connectivity_but_a_user_cancellation_is_not()
    {
        NetworkFailure.IsConnectivity(new TaskCanceledException()).Should().BeTrue();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        NetworkFailure.IsConnectivity(new TaskCanceledException("cancelled", null, cts.Token))
            .Should().BeFalse();
    }

    [Fact]
    public void A_wrapped_cause_is_still_found()
    {
        var wrapped = new InvalidOperationException("client failed", new HttpRequestException("dns"));

        NetworkFailure.IsConnectivity(wrapped).Should().BeTrue();
    }

    [Fact]
    public void Nothing_at_all_is_not_a_network_failure()
    {
        NetworkFailure.IsConnectivity(null).Should().BeFalse();
        NetworkFailure.IsConnectivity(new InvalidOperationException("bad state")).Should().BeFalse();
    }

    /// <summary>
    /// The strip's second line leads with what still works, because the whole point of
    /// per-feature degradation is that almost everything does.
    /// </summary>
    [Fact]
    public void The_detail_line_names_the_cache_it_is_falling_back_on()
    {
        NetworkFailure.Detail(3412, TimeSpan.FromDays(2))
            .Should().Be("Everything else works. Cached rules from 2 days ago are in use.");
    }

    /// <summary>
    /// With no cache there is no fallback to reassure anyone about, and saying
    /// "cached rules from an unknown date are in use" would be an invention.
    /// </summary>
    [Fact]
    public void With_no_cached_rules_it_says_what_sorting_falls_back_to_instead()
    {
        NetworkFailure.Detail(0, null)
            .Should().Be("Everything else works. There are no cached rules, so sorting uses About.xml only.");
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(45, "45 minutes ago")]
    [InlineData(60, "1 hour ago")]
    [InlineData(60 * 5, "5 hours ago")]
    [InlineData(60 * 24, "1 day ago")]
    [InlineData(60 * 24 * 2, "2 days ago")]
    [InlineData(60 * 24 * 65, "2 months ago")]
    public void The_age_is_coarse_because_the_question_is_roughly_how_old(double minutes, string expected)
    {
        NetworkFailure.Age(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }

    [Fact]
    public void Status_bar_zone_two_states_the_count_and_when_it_was_synced()
    {
        NetworkFailure.RulesStatus(3412, TimeSpan.FromDays(2), offline: false)
            .Should().Be("Community rules 3,412 · synced 2d ago");
    }

    /// <summary>Offline it reads "cached" — the count is real, its currency is not.</summary>
    [Fact]
    public void Offline_zone_two_reads_cached_rather_than_a_sync_date()
    {
        NetworkFailure.RulesStatus(3412, TimeSpan.FromDays(2), offline: true)
            .Should().Be("Community rules 3,412 · cached");
    }

    [Fact]
    public void With_no_rules_at_all_it_says_so_rather_than_showing_a_zero()
    {
        NetworkFailure.RulesStatus(0, null, offline: false).Should().Be("No community rules");
        NetworkFailure.RulesStatus(0, null, offline: true).Should().Be("No community rules");
    }
}
