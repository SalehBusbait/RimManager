using FluentAssertions;
using RimManager.Core.Abstractions;
using RimManager.Core.Git;
using RimManager.Core.Tests.Fakes;
using Xunit;

namespace RimManager.Core.Tests.Git;

/// <summary>
/// Records every command it is asked to run and replays canned output, so "did we
/// build the right git command line" is testable without git, a network, or a real
/// repository on disk.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessResult> _responses = new(StringComparer.Ordinal);

    public List<(string File, string Args, string? Cwd)> Calls { get; } = [];

    /// <summary>Canned result for a command, keyed on its joined arguments.</summary>
    public FakeProcessRunner Returns(string args, int exitCode, string stdout = "", string stderr = "")
    {
        _responses[args] = new ProcessResult(exitCode, stdout, stderr);
        return this;
    }

    public Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var args = string.Join(' ', arguments);
        Calls.Add((fileName, args, workingDirectory));

        return Task.FromResult(_responses.TryGetValue(args, out var result)
            ? result
            : new ProcessResult(1, string.Empty, $"no canned response for '{args}'"));
    }
}

public sealed class GitClientTests
{
    private const string Repo = "/mods/StartupImpact";

    private static (GitClient client, FakeProcessRunner runner, InMemoryFileSystem fs) Build(bool isRepo = true)
    {
        var fs = new InMemoryFileSystem(new FixedClock(DateTimeOffset.UnixEpoch));
        // Writing a file inside .git is what makes .git exist as a directory —
        // exactly the shape of a real clone.
        if (isRepo) fs.AddFile(Repo + "/.git/HEAD", "ref: refs/heads/main");

        var runner = new FakeProcessRunner();
        return (new GitClient(runner, fs), runner, fs);
    }

    // --- repository detection ------------------------------------------------

    /// <summary>
    /// Probed by looking for .git, not by running git: the common case is a few
    /// hundred Workshop folders and none of them are repositories, so this must not
    /// cost a process launch each.
    /// </summary>
    [Fact]
    public void IsRepository_is_true_for_a_normal_clone()
    {
        var (client, runner, _) = Build();

        client.IsRepository(Repo).Should().BeTrue();
        runner.Calls.Should().BeEmpty("detection must not launch a process");
    }

    [Fact]
    public void IsRepository_is_false_for_a_plain_folder()
    {
        var (client, _, _) = Build(isRepo: false);

        client.IsRepository(Repo).Should().BeFalse();
    }

    /// <summary>In a worktree or submodule, .git is a FILE pointing elsewhere.</summary>
    [Fact]
    public void IsRepository_accepts_a_git_file_as_well_as_a_directory()
    {
        var fs = new InMemoryFileSystem(new FixedClock(DateTimeOffset.UnixEpoch));
        fs.AddFile(Repo + "/.git", "gitdir: ../.git/worktrees/x");

        new GitClient(new FakeProcessRunner(), fs).IsRepository(Repo).Should().BeTrue();
    }

    // --- status --------------------------------------------------------------

    [Fact]
    public async Task Status_reads_short_sha_branch_and_clean_tree()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 0, "4a1c2b0")
              .Returns("status --porcelain", 0, "")
              .Returns("rev-parse --abbrev-ref HEAD", 0, "main")
              .Returns("rev-list --count HEAD..@{u}", 0, "0");

        var status = await client.StatusAsync(Repo);

        status.Should().NotBeNull();
        status!.ShortSha.Should().Be("4a1c2b0");
        status.IsDirty.Should().BeFalse();
        status.Branch.Should().Be("main");
        status.CommitsBehind.Should().Be(0);
    }

    /// <summary>The ⎇ row status: any porcelain output at all means uncommitted work.</summary>
    [Fact]
    public async Task Status_reports_a_dirty_working_tree()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 0, "4a1c2b0")
              .Returns("status --porcelain", 0, " M About/About.xml\n?? Notes.txt")
              .Returns("rev-parse --abbrev-ref HEAD", 0, "main")
              .Returns("rev-list --count HEAD..@{u}", 1, stderr: "no upstream");

        (await client.StatusAsync(Repo))!.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Status_reports_a_detached_head_as_no_branch()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 0, "4a1c2b0")
              .Returns("status --porcelain", 0, "")
              .Returns("rev-parse --abbrev-ref HEAD", 0, "HEAD");

        (await client.StatusAsync(Repo))!.Branch.Should().BeNull();
    }

    /// <summary>
    /// Null and zero are different: "no upstream configured" must not render as
    /// "up to date".
    /// </summary>
    [Fact]
    public async Task CommitsBehind_is_null_when_there_is_no_upstream()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 0, "4a1c2b0")
              .Returns("status --porcelain", 0, "")
              .Returns("rev-parse --abbrev-ref HEAD", 0, "main")
              .Returns("rev-list --count HEAD..@{u}", 128, stderr: "no upstream configured");

        (await client.StatusAsync(Repo))!.CommitsBehind.Should().BeNull();
    }

    [Fact]
    public async Task Status_is_null_for_a_folder_that_is_not_a_repository()
    {
        var (client, runner, _) = Build(isRepo: false);

        (await client.StatusAsync(Repo)).Should().BeNull();
        runner.Calls.Should().BeEmpty();
    }

    /// <summary>A repository with no commits yet must not throw or invent a sha.</summary>
    [Fact]
    public async Task Status_is_null_when_there_is_no_HEAD()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 128, stderr: "unknown revision");

        (await client.StatusAsync(Repo)).Should().BeNull();
    }

    [Fact]
    public async Task Commands_run_inside_the_mod_folder()
    {
        var (client, runner, _) = Build();
        runner.Returns("rev-parse --short HEAD", 0, "4a1c2b0")
              .Returns("status --porcelain", 0, "")
              .Returns("rev-parse --abbrev-ref HEAD", 0, "main")
              .Returns("rev-list --count HEAD..@{u}", 0, "0");

        await client.StatusAsync(Repo);

        // Getting the working directory wrong is the most common way these commands
        // silently answer about a different tree.
        runner.Calls.Should().OnlyContain(c => c.Cwd == Repo);
        runner.Calls.Should().OnlyContain(c => c.File == "git");
    }

    // --- version -------------------------------------------------------------

    [Fact]
    public async Task Version_extracts_the_number_from_git_version_output()
    {
        var (client, runner, _) = Build();
        runner.Returns("--version", 0, "git version 2.45.2");

        (await client.VersionAsync()).Should().Be("2.45.2");
    }

    /// <summary>
    /// git not installed is a NORMAL state, not an error — every integration is
    /// optional and local/GOG installs are first-class.
    /// </summary>
    [Fact]
    public async Task Version_is_null_when_git_is_unavailable()
    {
        var (client, runner, _) = Build();
        runner.Returns("--version", -1, stderr: "not found");

        (await client.VersionAsync()).Should().BeNull();
    }

    // --- fetch ---------------------------------------------------------------

    /// <summary>
    /// Fetch only. RimManager never pulls, merges, checks out or resets: a git mod
    /// folder is usually one the user is editing, and moving their working tree out
    /// from under them would be the worst thing this feature could do.
    /// </summary>
    [Fact]
    public async Task Fetch_only_ever_runs_fetch()
    {
        var (client, runner, _) = Build();
        runner.Returns("fetch --quiet", 0);

        (await client.FetchAsync(Repo)).Should().BeTrue();

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Args.Should().Be("fetch --quiet");
        runner.Calls.Should().NotContain(c =>
            c.Args.Contains("pull") || c.Args.Contains("merge")
            || c.Args.Contains("checkout") || c.Args.Contains("reset"));
    }

    /// <summary>Offline degrades per-feature, never globally (2k) — a failed fetch
    /// returns false rather than throwing.</summary>
    [Fact]
    public async Task Fetch_returns_false_when_offline()
    {
        var (client, runner, _) = Build();
        runner.Returns("fetch --quiet", 128, stderr: "could not resolve host");

        (await client.FetchAsync(Repo)).Should().BeFalse();
    }

    [Fact]
    public async Task Fetch_does_nothing_for_a_non_repository()
    {
        var (client, runner, _) = Build(isRepo: false);

        (await client.FetchAsync(Repo)).Should().BeFalse();
        runner.Calls.Should().BeEmpty();
    }

    // --- presentation --------------------------------------------------------

    [Fact]
    public void Version_text_is_the_git_at_sha_form_shown_in_the_row()
        => new GitStatus("4a1c", false).VersionText.Should().Be("git@4a1c");
}
