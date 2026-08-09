using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RimManager.App.Services;
using RimManager.App.Tests.Fakes;
using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Which installed mods count as git-tracked. This is the whole test: the discovery rule
/// decides how many processes a scan launches, whose remotes a fetch contacts, and which
/// rows claim uncommitted changes.
/// </summary>
public sealed class GitServiceTests
{
    /// <summary>A runner that records what was asked of it and answers nothing — the point
    /// of these tests is that git is never invoked in the first place.</summary>
    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            string? workingDirectory = null, CancellationToken ct = default)
        {
            Invocations.Add($"{fileName} {string.Join(' ', arguments)} @ {workingDirectory}");
            return Task.FromResult(new ProcessResult(1, string.Empty, "stub"));
        }
    }

    private static Mod ModAt(string root, ModSource source) => new()
    {
        PackageId = ModId.From($"test.{Path.GetFileName(root)}"),
        Name = Path.GetFileName(root),
        Source = source,
        RootPath = root,
    };

    private static GitService Service(IFileSystem fs, IProcessRunner runner) =>
        new(fs, runner, new ActivityLog(new FixedClockStub()));

    /// <summary>ActivityLog requires a clock — no default, by design.</summary>
    private sealed class FixedClockStub : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// The bug this exists to prevent. On the developer's real 405-mod Workshop library,
    /// <b>33 folders contain a `.git`</b> — mod authors publish their working directory.
    /// None of them is a repository the user manages: Steam owns that folder, every one
    /// reports dirty because Steam's copy differs from the author's commit, and some
    /// `.git` files point at the author's own machine.
    /// </summary>
    [Fact]
    public void A_git_folder_inside_a_workshop_mod_is_upload_residue_not_a_tracked_repo()
    {
        var fs = new StubFileSystem()
            .WithDirectory("/ws/1195427067", "/ws/1195427067/.git")
            .WithDirectory("/mods/MyMod", "/mods/MyMod/.git");

        var runner = new RecordingRunner();

        var tracked = Service(fs, runner).TrackedRepos(
        [
            ModAt("/ws/1195427067", ModSource.Workshop),
            ModAt("/mods/MyMod", ModSource.Local),
        ]);

        tracked.Select(m => m.RootPath).Should().Equal("/mods/MyMod");
        runner.Invocations.Should().BeEmpty(
            "discovery probes for .git; it must never cost a process launch per mod");
    }

    /// <summary>
    /// Core, DLC and Workshop are all folders the game or Steam owns. A `.git` in any of
    /// them is residue, not a repository the user works in. (The vault was a fourth and
    /// retired with O13.)
    /// </summary>
    [Theory]
    [InlineData(ModSource.Core)]
    [InlineData(ModSource.Dlc)]
    [InlineData(ModSource.Workshop)]
    public void Sources_the_user_does_not_manage_are_never_tracked(ModSource source)
    {
        var fs = new StubFileSystem().WithDirectory("/x/mod", "/x/mod/.git");

        Service(fs, new RecordingRunner())
            .TrackedRepos([ModAt("/x/mod", source)])
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(ModSource.Local)]
    [InlineData(ModSource.Git)]
    public void A_local_clone_is_tracked(ModSource source)
    {
        var fs = new StubFileSystem().WithDirectory("/mods/mine", "/mods/mine/.git");

        Service(fs, new RecordingRunner())
            .TrackedRepos([ModAt("/mods/mine", source)])
            .Should().HaveCount(1);
    }

    /// <summary>
    /// A worktree or submodule records `.git` as a FILE, not a directory. Both are
    /// accepted, so a developer using a worktree is not silently untracked.
    /// </summary>
    [Fact]
    public void A_dot_git_file_counts_as_a_working_tree()
    {
        var fs = new StubFileSystem()
            .WithDirectory("/mods/wt")
            .WithFile("/mods/wt/.git", "gitdir: /elsewhere/.git/worktrees/wt");

        Service(fs, new RecordingRunner())
            .TrackedRepos([ModAt("/mods/wt", ModSource.Local)])
            .Should().HaveCount(1);
    }

    [Fact]
    public void A_local_mod_with_no_dot_git_is_not_tracked()
    {
        var fs = new StubFileSystem().WithDirectory("/mods/plain");

        Service(fs, new RecordingRunner())
            .TrackedRepos([ModAt("/mods/plain", ModSource.Local)])
            .Should().BeEmpty();
    }

    /// <summary>
    /// Fetch is the only write git is ever allowed, and it must only ever reach repos the
    /// user manages — the Workshop case would have contacted 33 strangers' remotes.
    /// </summary>
    [Fact]
    public async Task Fetch_only_ever_runs_in_folders_discovery_returned()
    {
        var fs = new StubFileSystem().WithDirectory("/mods/mine", "/mods/mine/.git");
        var runner = new RecordingRunner();
        var service = Service(fs, runner);

        var tracked = service.TrackedRepos([
            ModAt("/mods/mine", ModSource.Local),
            ModAt("/ws/9", ModSource.Workshop),
        ]);
        await service.FetchAllAsync(tracked);

        runner.Invocations.Should().ContainSingle()
            .Which.Should().Contain("fetch").And.Contain("/mods/mine");
    }
}
