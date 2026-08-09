using System.Net.Http;
using FluentAssertions;
using RimManager.Core.Github;
using RimManager.Integrations.Http;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Hits the real GitHub releases API. Skips (never fails) when offline or rate-limited,
/// so an isolated CI runner stays green. Exercises the GET path of
/// <see cref="HttpClientFetcher"/>.
/// </summary>
public sealed class GitHubReleasesLiveTests
{
    // Harmony — the RimWorld modding dependency; a long-lived repo with many releases.
    private static readonly GitHubRepoRef Harmony = new("pardeike", "Harmony");

    [SkippableFact]
    public async Task Fetches_the_latest_release_of_a_known_repo()
    {
        using var fetcher = new HttpClientFetcher(TimeSpan.FromSeconds(15));
        var client = new GitHubReleasesClient(fetcher);

        GitHubRelease? latest;
        try
        {
            latest = await client.GetLatestReleaseAsync(Harmony);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Skip.If(true, $"GitHub API unreachable or rate-limited: {ex.Message}");
            return;
        }

        latest.Should().NotBeNull("Harmony has published releases");
        latest!.TagName.Should().NotBeNullOrWhiteSpace();
        latest.PublishedAtUtc.Should().NotBeNull();
    }
}
