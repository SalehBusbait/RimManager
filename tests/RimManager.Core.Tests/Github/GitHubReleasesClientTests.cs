using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Core.Github;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Github;

public sealed class GitHubReleasesClientTests
{
    private static readonly GitHubRepoRef Repo = new("pardeike", "Harmony");

    [Fact]
    public async Task GetLatestRelease_hits_the_latest_endpoint_and_parses()
    {
        string? requested = null;
        var fetcher = new FakeHttpFetcher
        {
            GetResponder = url => { requested = url; return """{ "tag_name": "v2.3.3", "name": "Harmony" }"""; },
        };
        var client = new GitHubReleasesClient(fetcher);

        var release = await client.GetLatestReleaseAsync(Repo);

        requested.Should().Be($"{GitHubReleasesClient.ApiBase}/repos/pardeike/Harmony/releases/latest");
        release!.TagName.Should().Be("v2.3.3");
    }

    [Fact]
    public async Task GetLatestRelease_maps_404_to_null()
    {
        var fetcher = new FakeHttpFetcher
        {
            GetResponder = url => throw new HttpFetchException(url, 404, "Not Found"),
        };
        var client = new GitHubReleasesClient(fetcher);

        (await client.GetLatestReleaseAsync(Repo)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestRelease_does_not_swallow_non_404_errors()
    {
        var fetcher = new FakeHttpFetcher
        {
            GetResponder = url => throw new HttpFetchException(url, 500, "Server Error"),
        };
        var client = new GitHubReleasesClient(fetcher);

        await FluentActions.Awaiting(() => client.GetLatestReleaseAsync(Repo))
            .Should().ThrowAsync<HttpFetchException>();
    }

    [Fact]
    public async Task GetReleases_requests_per_page_and_maps_404_to_empty()
    {
        string? requested = null;
        var fetcher = new FakeHttpFetcher
        {
            GetResponder = url => { requested = url; return """[ { "tag_name": "v1" } ]"""; },
        };
        var client = new GitHubReleasesClient(fetcher);

        var releases = await client.GetReleasesAsync(Repo, perPage: 5);

        requested.Should().Contain("/releases?per_page=5");
        releases.Should().ContainSingle();

        var notFound = new GitHubReleasesClient(new FakeHttpFetcher
        {
            GetResponder = url => throw new HttpFetchException(url, 404, "Not Found"),
        });
        (await notFound.GetReleasesAsync(Repo)).Should().BeEmpty();
    }
}
