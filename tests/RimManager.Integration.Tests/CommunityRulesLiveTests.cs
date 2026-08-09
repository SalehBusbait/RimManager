using System.Net.Http;
using FluentAssertions;
using RimManager.Core.Rules;
using RimManager.Integrations.Http;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Fetches the real community rules database over the wire. Skips (never fails) when
/// offline, so an isolated runner stays green. Exercises the GET path end-to-end and
/// guards that the live DB still parses into a non-trivial rule set.
/// </summary>
public sealed class CommunityRulesLiveTests
{
    [SkippableFact]
    public async Task Downloads_and_parses_the_live_rules_database()
    {
        LiveEndpoints.SkipInCi();

        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(20));
        var client = new CommunityRulesClient(fetcher);

        CommunityRulesDatabase db;
        try
        {
            db = await client.FetchAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Skip.If(true, $"Rules database unreachable: {ex.Message}");
            return;
        }

        // The real DB has thousands of entries; assert it's substantive, not just non-empty.
        db.RuleCount.Should().BeGreaterThan(100);
        db.PublishedUtc.Should().NotBeNull("the database carries a top-level timestamp");
    }
}
