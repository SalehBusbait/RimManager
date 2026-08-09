using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;

namespace RimManager.Core.Git;

/// <summary>
/// What RimManager knows about a git-tracked mod.
/// </summary>
/// <param name="ShortSha">Abbreviated HEAD, e.g. <c>4a1c</c> — shown as the version
/// (<c>git@4a1c</c>) because a git mod has no <c>modVersion</c> worth trusting.</param>
/// <param name="IsDirty">Working tree has uncommitted changes — the <c>⎇</c> row status.</param>
/// <param name="Branch">Current branch, or null when detached.</param>
/// <param name="CommitsBehind">
/// How far behind the upstream, when one is configured and has been fetched. Null
/// means "unknown", which is different from zero and must not render as up-to-date.
/// </param>
public sealed record GitStatus(
    string ShortSha,
    bool IsDirty,
    string? Branch = null,
    int? CommitsBehind = null)
{
    /// <summary>The version string a git-sourced row shows (`1a`, row `git@4a1c`).</summary>
    public string VersionText => $"git@{ShortSha}";
}

/// <summary>
/// Reads git state for mods installed from a repository rather than the Workshop
/// (Settings ▸ Integrations ▸ Git; the <c>⎇</c> status glyph; git rows in Updates).
/// <para>
/// Every call is <b>read-only</b> apart from <see cref="FetchAsync"/>, and even that
/// only fetches — RimManager never pulls, merges, checks out or resets. A mod folder
/// under git is usually one the user is editing themselves, and silently moving their
/// working tree would be the worst thing this feature could do. Updating a git mod is
/// left to the user's own tooling.
/// </para>
/// <para>
/// Pure orchestration: all process execution goes through <see cref="IProcessRunner"/>
/// and all path probing through <see cref="IFileSystem"/>, so <c>Core</c> still
/// performs no I/O of its own.
/// </para>
/// </summary>
public sealed class GitClient
{
    private readonly IProcessRunner _runner;
    private readonly IFileSystem _fs;
    private readonly IActivityLog _log;
    private readonly string _gitPath;

    public GitClient(
        IProcessRunner runner,
        IFileSystem fs,
        IActivityLog? log = null,
        string gitPath = "git")
    {
        _runner = runner;
        _fs = fs;
        _log = log ?? NullActivityLog.Instance;
        _gitPath = gitPath;
    }

    /// <summary>
    /// Whether a mod folder is a git working tree. Checked by probing for
    /// <c>.git</c> rather than by running git, so the common case — a few hundred
    /// Workshop folders, none of them repositories — costs no process launches.
    /// <c>.git</c> is a directory in a normal clone and a file in a worktree or
    /// submodule, so both are accepted.
    /// </summary>
    public bool IsRepository(string modRoot)
    {
        var dotGit = Path.Combine(modRoot, ".git");
        return _fs.DirectoryExists(dotGit) || _fs.FileExists(dotGit);
    }

    /// <summary>
    /// Reads the version of git on PATH, for the Integrations page ("git 2.45.2").
    /// Null when git is not installed — which is a normal state, not an error: the
    /// design says local and GOG installs are first-class and every integration is
    /// optional.
    /// </summary>
    public async Task<string?> VersionAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(null, ct, "--version").ConfigureAwait(false);
        if (!result.Succeeded) return null;

        // "git version 2.45.2" -> "2.45.2"
        var text = result.StandardOutput.Trim();
        var index = text.LastIndexOf(' ');
        return index >= 0 && index < text.Length - 1 ? text[(index + 1)..] : text;
    }

    /// <summary>
    /// Reads the status of one repository, or null when the folder is not a
    /// repository or git is unavailable.
    /// </summary>
    public async Task<GitStatus?> StatusAsync(string modRoot, CancellationToken ct = default)
    {
        if (!IsRepository(modRoot)) return null;

        var head = await RunAsync(modRoot, ct, "rev-parse", "--short", "HEAD").ConfigureAwait(false);
        if (!head.Succeeded)
        {
            // A fresh repo with no commits, or no git binary. Either way there is
            // nothing to report; a warning here would fire on every scan.
            _log.Write(LogLevel.Debug, LogSubsystem.Git, $"no HEAD in {modRoot}: {head.StandardError}");
            return null;
        }

        var sha = head.StandardOutput.Trim();

        // --porcelain is the stable, script-facing format; the human one is not
        // guaranteed across versions or locales.
        var status = await RunAsync(modRoot, ct, "status", "--porcelain").ConfigureAwait(false);
        var dirty = status.Succeeded && status.OutputLines.Count > 0;

        var branchResult = await RunAsync(modRoot, ct, "rev-parse", "--abbrev-ref", "HEAD")
            .ConfigureAwait(false);
        var branch = branchResult.Succeeded ? branchResult.StandardOutput.Trim() : null;
        if (branch == "HEAD") branch = null; // detached

        var behind = await CommitsBehindAsync(modRoot, ct).ConfigureAwait(false);

        _log.Write(LogLevel.Debug, LogSubsystem.Git,
            $"{modRoot}: {sha}{(dirty ? " (dirty)" : "")}{(behind is > 0 ? $" {behind} behind" : "")}");

        return new GitStatus(sha, dirty, branch, behind);
    }

    /// <summary>
    /// Commits behind the configured upstream, or null when there is no upstream.
    /// Null and zero are deliberately different: "no upstream configured" must not
    /// render as "up to date".
    /// </summary>
    public async Task<int?> CommitsBehindAsync(string modRoot, CancellationToken ct = default)
    {
        var result = await RunAsync(modRoot, ct, "rev-list", "--count", "HEAD..@{u}").ConfigureAwait(false);
        if (!result.Succeeded) return null;

        return int.TryParse(result.StandardOutput.Trim(), out var count) ? count : null;
    }

    /// <summary>
    /// Fetches from the remote — Settings ▸ Integrations ▸ "Fetch tracked repos on
    /// startup" (off by default, because it is a network call on every launch).
    /// <para>
    /// Fetch only. It updates remote-tracking refs and touches nothing in the working
    /// tree, so a user's uncommitted edits to a mod they are developing are never at
    /// risk.
    /// </para>
    /// </summary>
    public async Task<bool> FetchAsync(string modRoot, CancellationToken ct = default)
    {
        if (!IsRepository(modRoot)) return false;

        var result = await RunAsync(modRoot, ct, "fetch", "--quiet").ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Offline is the common case and degrades per-feature, never globally (2k).
            _log.Write(LogLevel.Warn, LogSubsystem.Git,
                $"fetch failed for {modRoot}: {result.StandardError.Trim()}");
        }

        return result.Succeeded;
    }

    private Task<ProcessResult> RunAsync(string? workingDirectory, CancellationToken ct, params string[] args) =>
        _runner.RunAsync(_gitPath, args, workingDirectory, ct);
}
