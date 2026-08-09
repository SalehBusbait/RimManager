using System;
using System.Collections.Generic;
using FluentAssertions;
using RimManager.App.Services;
using RimManager.Core.Abstractions;
using RimManager.Core.Workshop;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Every "Workshop ↗" in the app goes through one policy: prefer the Steam client,
/// fall back to the browser. Four call sites used to reach for the browser URL
/// directly, so all four opened a browser with Steam running in the background.
/// </summary>
public sealed class WorkshopLinkServiceTests
{
    private const string Id = "2009463077";

    /// <summary>Records what was attempted, and can refuse a scheme the way a machine with no handler does.</summary>
    private sealed class RecordingLauncher(params string[] refusedSchemes) : IUriLauncher
    {
        public List<string> Attempted { get; } = [];

        public void Launch(string uri)
        {
            Attempted.Add(uri);
            foreach (var scheme in refusedSchemes)
            {
                if (uri.StartsWith(scheme, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"no handler for {scheme}");
                }
            }
        }
    }

    [Fact]
    public void With_Steam_running_a_view_goes_to_the_client()
    {
        var launcher = new RecordingLauncher();
        var opened = new WorkshopLinkService(launcher, () => true).Open(Id);

        opened.Should().Be(SteamUrls.CommunityFilePage(Id));
        launcher.Attempted.Should().ContainSingle("the client took it, so the browser is never asked");
    }

    /// <summary>
    /// Not merely "installed": launching a cold client and waiting half a minute to
    /// read one description is worse than the tab that opens instantly.
    /// </summary>
    [Fact]
    public void With_Steam_closed_a_view_goes_straight_to_the_browser()
    {
        var launcher = new RecordingLauncher();
        var opened = new WorkshopLinkService(launcher, () => false).Open(Id);

        opened.Should().Be(SteamUrls.WebFilePage(Id));
        launcher.Attempted.Should().ContainSingle()
            .Which.Should().StartWith("https://", "a cold client must not be woken to read a page");
    }

    [Fact]
    public void A_refused_steam_scheme_falls_through_to_the_browser()
    {
        var launcher = new RecordingLauncher("steam://");
        var opened = new WorkshopLinkService(launcher, () => true).Open(Id);

        opened.Should().Be(SteamUrls.WebFilePage(Id));
        launcher.Attempted.Should().Equal(SteamUrls.CommunityFilePage(Id), SteamUrls.WebFilePage(Id));
    }

    [Fact]
    public void Nothing_opening_is_reported_rather_than_thrown()
    {
        var launcher = new RecordingLauncher("steam://", "https://");

        new WorkshopLinkService(launcher, () => true).Open(Id).Should().BeNull();
    }

    /// <summary>
    /// Only the client can subscribe, so waiting for it to start is the point rather
    /// than a cost — this one does not consult the probe at all.
    /// </summary>
    [Fact]
    public void Subscribing_always_tries_the_client_first_even_when_it_is_closed()
    {
        var launcher = new RecordingLauncher();
        var opened = new WorkshopLinkService(launcher, () => false).OpenToSubscribe(Id);

        opened.Should().Be(SteamUrls.CommunityFilePage(Id));
    }

    /// <summary>A probe that throws must mean "browser", never "no link at all".</summary>
    [Fact]
    public void A_failing_probe_degrades_to_the_browser()
    {
        var launcher = new RecordingLauncher();
        var opened = new WorkshopLinkService(
            launcher, () => throw new InvalidOperationException("process enumeration denied")).Open(Id);

        opened.Should().Be(SteamUrls.WebFilePage(Id));
    }

    [Fact]
    public void The_attempt_order_is_client_then_browser() =>
        SteamLink.Attempts(Id, preferSteamClient: true)
            .Should().Equal(SteamUrls.CommunityFilePage(Id), SteamUrls.WebFilePage(Id));
}
