using System.Collections.Immutable;
using FluentAssertions;
using RimManager.Core.Github;
using Xunit;

namespace RimManager.Core.Tests.Github;

public sealed class AppUpdateAdvisorTests
{
    private static GitHubRelease Release(
        string tag, bool prerelease = false, bool draft = false, params string[] assets) =>
        new()
        {
            TagName = tag,
            IsPrerelease = prerelease,
            IsDraft = draft,
            HtmlUrl = $"https://example.test/releases/{tag}",
            Assets = [.. assets.Select(a => new GitHubReleaseAsset
                { Name = a, DownloadUrl = $"https://example.test/{a}" })],
        };

    [Fact]
    public void A_newer_prerelease_is_offered_to_a_prerelease_user()
    {
        var advice = AppUpdateAdvisor.Advise("1.0.0-beta.2+50b66ae",
            [Release("v1.0.0-beta.3", prerelease: true)]);

        advice.Should().NotBeNull();
        advice!.Version.Should().Be("1.0.0-beta.3");
    }

    [Fact]
    public void A_prerelease_is_never_offered_to_a_stable_user()
    {
        AppUpdateAdvisor.Advise("1.0.0", [Release("v1.1.0-beta.1", prerelease: true)])
            .Should().BeNull("someone who chose stable did not choose betas");
    }

    [Fact]
    public void A_stable_release_is_offered_to_a_prerelease_user()
    {
        // 1.0.0 outranks its own betas: shipping the release IS the beta's upgrade.
        AppUpdateAdvisor.Advise("1.0.0-beta.2", [Release("v1.0.0")])!
            .Version.Should().Be("1.0.0");
    }

    [Fact]
    public void The_running_version_is_up_to_date_against_itself()
    {
        AppUpdateAdvisor.Advise("1.0.0-beta.2+abc1234", [Release("v1.0.0-beta.2", prerelease: true)])
            .Should().BeNull("build metadata does not make a build newer");
    }

    [Fact]
    public void Older_and_drafts_are_never_offered()
    {
        AppUpdateAdvisor.Advise("1.0.0-beta.2",
            [Release("v1.0.0-beta.1", prerelease: true), Release("v2.0.0", draft: true)])
            .Should().BeNull();
    }

    [Fact]
    public void The_newest_applicable_release_wins_regardless_of_list_order()
    {
        var advice = AppUpdateAdvisor.Advise("1.0.0-beta.1", [
            Release("v1.0.0-beta.2", prerelease: true),
            Release("v1.0.0-beta.10", prerelease: true),
            Release("v1.0.0-beta.9", prerelease: true),
        ]);

        advice!.Version.Should().Be("1.0.0-beta.10",
            "beta.10 outranks beta.9 numerically, not alphabetically");
    }

    [Fact]
    public void The_installer_asset_is_picked_by_the_release_workflows_naming()
    {
        var advice = AppUpdateAdvisor.Advise("1.0.0-beta.2", [
            Release("v1.0.0-beta.3", prerelease: true,
                assets: ["RimManager-1.0.0-beta.3-win-x64.zip",
                         "RimManager-Setup-1.0.0-beta.3.exe",
                         "RimManager-1.0.0-beta.3-linux-x64.tar.gz"]),
        ]);

        advice!.Installer!.Name.Should().Be("RimManager-Setup-1.0.0-beta.3.exe");
    }

    [Fact]
    public void A_release_without_an_installer_still_advises_with_the_page()
    {
        var advice = AppUpdateAdvisor.Advise("1.0.0-beta.2",
            [Release("v1.0.0-beta.3", prerelease: true, assets: ["notes.txt"])]);

        advice!.Installer.Should().BeNull();
        advice.PageUrl.Should().NotBeNullOrEmpty("the fallback is opening the release page");
    }

    [Fact]
    public void Garbage_versions_advise_nothing_rather_than_throwing()
    {
        AppUpdateAdvisor.Advise(null, [Release("v1.0.0")]).Should().BeNull();
        AppUpdateAdvisor.Advise("not-a-version", [Release("v1.0.0")]).Should().BeNull();
        AppUpdateAdvisor.Advise("1.0.0", [Release("nightly-build")]).Should().BeNull();
    }
}
