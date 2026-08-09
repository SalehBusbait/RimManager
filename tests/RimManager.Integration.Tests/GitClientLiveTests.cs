using FluentAssertions;
using RimManager.Core.Git;
using RimManager.Integrations.Processes;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Drives <see cref="GitClient"/> through the real <see cref="SystemProcessRunner"/>
/// against a real repository — this one. The unit tests prove the command lines are
/// right; these prove the process plumbing, argument passing and output parsing hold
/// against an actual git binary.
/// <para>
/// Every test skips cleanly when git is unavailable, so a bare clone on a machine
/// without git stays green.
/// </para>
/// </summary>
public sealed class GitClientLiveTests
{
    private static GitClient Client() => new(new SystemProcessRunner(), new PhysicalFileSystem());

    /// <summary>This repository — walked up to from the test assembly.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }

        return string.Empty;
    }

    private static async Task<bool> GitAvailableAsync() => await Client().VersionAsync() is not null;

    [SkippableFact]
    public async Task Reads_the_installed_git_version()
    {
        var version = await Client().VersionAsync();
        Skip.If(version is null, "git is not installed or not on PATH");

        // "2.45.2" — a version, not the whole "git version 2.45.2" line.
        version.Should().MatchRegex(@"^\d+\.\d+");
    }

    [SkippableFact]
    public async Task Detects_this_repository_and_reads_its_head()
    {
        Skip.IfNot(await GitAvailableAsync(), "git is not installed or not on PATH");

        var root = RepoRoot();
        Skip.If(string.IsNullOrEmpty(root), "not running inside a git checkout");

        var client = Client();
        client.IsRepository(root).Should().BeTrue();

        var status = await client.StatusAsync(root);

        status.Should().NotBeNull();
        status!.ShortSha.Should().MatchRegex("^[0-9a-f]{4,}$");
        status.VersionText.Should().StartWith("git@");
    }

    [SkippableFact]
    public async Task A_folder_that_is_not_a_repository_reports_nothing()
    {
        Skip.IfNot(await GitAvailableAsync(), "git is not installed or not on PATH");

        var temp = Path.Combine(Path.GetTempPath(), "rimmanager-notgit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);

        try
        {
            var client = Client();
            client.IsRepository(temp).Should().BeFalse();
            (await client.StatusAsync(temp)).Should().BeNull();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// A missing binary must degrade, not throw: git being absent is a normal state,
    /// and 2k requires per-feature degradation rather than a global error.
    /// </summary>
    [Fact]
    public async Task A_missing_git_binary_degrades_instead_of_throwing()
    {
        var client = new GitClient(
            new SystemProcessRunner(), new PhysicalFileSystem(),
            gitPath: "definitely-not-a-real-git-binary-" + Guid.NewGuid().ToString("N")[..6]);

        (await client.VersionAsync()).Should().BeNull();
    }
}
