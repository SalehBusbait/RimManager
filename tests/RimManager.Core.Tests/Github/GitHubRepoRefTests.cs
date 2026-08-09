using FluentAssertions;
using RimManager.Core.Github;
using Xunit;

namespace RimManager.Core.Tests.Github;

public sealed class GitHubRepoRefTests
{
    [Theory]
    [InlineData("pardeike/Harmony", "pardeike", "Harmony")]
    [InlineData("https://github.com/pardeike/Harmony", "pardeike", "Harmony")]
    [InlineData("http://github.com/pardeike/Harmony", "pardeike", "Harmony")]
    [InlineData("github.com/pardeike/Harmony", "pardeike", "Harmony")]
    [InlineData("https://github.com/UnlimitedHugs/RimworldHugsLib/releases", "UnlimitedHugs", "RimworldHugsLib")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("https://github.com/owner/repo?tab=readme#x", "owner", "repo")]
    [InlineData("owner/my.dotted.repo", "owner", "my.dotted.repo")]
    public void Parses_owner_and_repo(string input, string owner, string repo)
    {
        GitHubRepoRef.TryParse(input, out var parsed).Should().BeTrue();
        parsed!.Owner.Should().Be(owner);
        parsed.Repo.Should().Be(repo);
        parsed.ToString().Should().Be($"{owner}/{repo}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("justowner")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://example.com/owner/repo")]
    public void Rejects_non_github_or_incomplete(string? input)
    {
        GitHubRepoRef.TryParse(input, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }
}
