using FluentAssertions;
using RimManager.Core.Github;
using Xunit;

namespace RimManager.Core.Tests.Github;

public sealed class GitHubReleaseParserTests
{
    private const string SingleRelease = """
        {
          "tag_name": "v2.3.3",
          "name": "Harmony 2.3.3",
          "draft": false,
          "prerelease": false,
          "published_at": "2024-02-15T10:30:00Z",
          "html_url": "https://github.com/pardeike/Harmony/releases/tag/v2.3.3",
          "assets": [
            { "name": "Harmony.zip", "browser_download_url": "https://example/Harmony.zip",
              "size": 524288, "content_type": "application/zip" },
            { "name": "no-url", "size": 10 }
          ]
        }
        """;

    [Fact]
    public void Parses_single_release_with_assets_and_timestamp()
    {
        var release = GitHubReleaseParser.ParseSingle(SingleRelease)!;

        release.TagName.Should().Be("v2.3.3");
        release.DisplayName.Should().Be("Harmony 2.3.3");
        release.IsPrerelease.Should().BeFalse();
        release.IsDraft.Should().BeFalse();
        release.PublishedAtUtc.Should().Be(new DateTimeOffset(2024, 2, 15, 10, 30, 0, TimeSpan.Zero));
        release.HtmlUrl.Should().Be("https://github.com/pardeike/Harmony/releases/tag/v2.3.3");

        // The asset with no download url is skipped.
        release.Assets.Should().ContainSingle();
        release.Assets[0].Name.Should().Be("Harmony.zip");
        release.Assets[0].Size.Should().Be(524288);
        release.Assets[0].ContentType.Should().Be("application/zip");
    }

    [Fact]
    public void Parse_handles_an_array_of_releases()
    {
        const string json = """
            [
              { "tag_name": "v2.0", "prerelease": true, "published_at": "2023-01-01T00:00:00Z" },
              { "tag_name": "v1.0", "published_at": "2022-01-01T00:00:00Z" }
            ]
            """;

        var releases = GitHubReleaseParser.Parse(json);
        releases.Select(r => r.TagName).Should().Equal("v2.0", "v1.0");
        releases[0].IsPrerelease.Should().BeTrue();
    }

    [Fact]
    public void Name_falls_back_to_tag_when_absent()
    {
        var release = GitHubReleaseParser.ParseSingle("""{ "tag_name": "v9" }""")!;
        release.DisplayName.Should().Be("v9");
        release.PublishedAtUtc.Should().BeNull();
        release.Assets.Should().BeEmpty();
    }

    [Fact]
    public void Release_without_tag_is_rejected()
    {
        GitHubReleaseParser.ParseSingle("""{ "name": "no tag" }""").Should().BeNull();
        GitHubReleaseParser.Parse("""[ { "name": "no tag" }, { "tag_name": "v1" } ]""")
            .Select(r => r.TagName).Should().Equal("v1");
    }
}
