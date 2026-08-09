using System.Net.Http;
using FluentAssertions;
using RimManager.Core.ModDatabases;
using RimManager.Integrations.Http;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Fetches Mlie's two mod databases (N7) over the wire. Skips (never fails) when
/// offline. These guard the live formats this integration was measured against: the
/// gzip + BOM payload shape for UseThisInstead, and the per-version XML for
/// NoVersionWarning — including the 404-is-an-absence rule for a version that has no
/// list.
/// </summary>
public sealed class ModDatabasesLiveTests
{
    [SkippableFact]
    public async Task Downloads_gunzips_and_parses_the_live_replacements_database()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(20));

        ReplacementDatabase db;
        try
        {
            db = await new UseThisInsteadClient(fetcher).FetchAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Skip.If(true, $"UseThisInstead unreachable: {ex.Message}");
            return;
        }

        // Measured 2,648 on 7 Aug 2026; assert substantive, not exact.
        db.Count.Should().BeGreaterThan(1000);
        db.PublishedUtc.Should().NotBeNull("the database carries a version stamp");
        db.Replacements.Should().OnlyContain(r => r.OldWorkshopId.Length > 0);
    }

    [SkippableFact]
    public async Task Downloads_and_parses_the_live_known_good_list_for_1_6()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(20));

        KnownGoodDatabase db;
        try
        {
            db = await new NoVersionWarningClient(fetcher).FetchAsync("1.6");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Skip.If(true, $"NoVersionWarning unreachable: {ex.Message}");
            return;
        }

        // Measured 296 ids (600 lines) on 7 Aug 2026; assert substantive.
        db.Count.Should().BeGreaterThan(50);
    }

    [SkippableFact]
    public async Task A_game_version_with_no_list_returns_empty_over_the_live_wire()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(20));

        KnownGoodDatabase db;
        try
        {
            db = await new NoVersionWarningClient(fetcher).FetchAsync("0.9");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Skip.If(true, $"NoVersionWarning unreachable: {ex.Message}");
            return;
        }

        db.Count.Should().Be(0, "0.9 has no upstream list, and 404 is an absence, not an error");
    }
}
