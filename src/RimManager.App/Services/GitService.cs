using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;
using RimManager.Core.Domain;
using RimManager.Core.Git;

namespace RimManager.App.Services;

/// <summary>
/// The App's bridge to <see cref="GitClient"/>: which installed mods are git working
/// trees, what state each is in, and what to print on Settings ▸ Integrations.
/// <para>
/// Read-only apart from <see cref="FetchAllAsync"/>, and that only updates
/// remote-tracking refs — the guarantee <see cref="GitClient"/> makes, restated here
/// because this is the layer that decides when git runs at all.
/// </para>
/// </summary>
public sealed class GitService
{
    private readonly GitClient _client;
    private readonly IFileSystem _fs;

    public GitService(IFileSystem fs, IProcessRunner runner, ActivityLog log)
    {
        _fs = fs;
        _client = new GitClient(runner, fs, log);
    }

    /// <summary>
    /// Reads git's version, or null when it is not installed. Not an error: a git mod
    /// is a developer's working copy, and most installs have none.
    /// </summary>
    public Task<string?> VersionAsync(CancellationToken ct = default) => _client.VersionAsync(ct);

    /// <summary>
    /// The mods that are git working trees the <b>user</b> manages. Probes for
    /// <c>.git</c> rather than running git, so folders that are not repositories cost no
    /// process launches — the reason this can run on every scan.
    /// </summary>
    public ImmutableArray<Mod> TrackedRepos(IEnumerable<Mod> mods) =>
        [.. mods.Where(m => IsUserManaged(m.Source) && _client.IsRepository(m.RootPath))];

    /// <summary>
    /// Whether a mod lives somewhere the user could have cloned it. <b>Only local mods
    /// count as git-tracked</b>, and that restriction is load-bearing rather than tidy.
    /// <para>
    /// A `.git` inside a Workshop folder is upload residue: mod authors publish their
    /// working directory, and on a real 405-mod library <b>33 of them</b> ship one. Those
    /// are not repositories the user is developing — Steam owns that folder and rewrites
    /// it. Treating them as tracked went wrong three ways at once:
    /// </para>
    /// <list type="bullet">
    ///   <item>every one reports <b>dirty</b>, because Steam's copy differs from the
    ///   author's commit — so the ⎇ "uncommitted local changes" glyph lit up on thirty
    ///   Workshop mods the user had never touched;</item>
    ///   <item>a `.git` <i>file</i> points at the author's own machine
    ///   (<c>C:/gits/HFM.git</c>), so it is not a repository here at all;</item>
    ///   <item>four git invocations each, at ~200ms, is ~6.5 seconds of process launches
    ///   added to every scan — and "fetch on startup" would have contacted 33 upstreams
    ///   belonging to other people.</item>
    /// </list>
    /// <para>
    /// The vault (<see cref="ModSource.Pinned"/>) is excluded for the same reason: it
    /// holds copies RimManager made, so any `.git` in it is residue too.
    /// </para>
    /// </summary>
    private static bool IsUserManaged(ModSource source) =>
        source is ModSource.Local or ModSource.Git;

    /// <summary>Reads each tracked repo's state, keyed by packageId. One repo failing drops
    /// that entry rather than the batch.</summary>
    public async Task<ImmutableDictionary<ModId, GitStatus>> StatusesAsync(
        IReadOnlyList<Mod> repos, CancellationToken ct = default)
    {
        var builder = ImmutableDictionary.CreateBuilder<ModId, GitStatus>();
        foreach (var mod in repos)
        {
            if (ct.IsCancellationRequested) break;

            var status = await _client.StatusAsync(mod.RootPath, ct).ConfigureAwait(false);
            if (status is not null) builder[mod.PackageId] = status;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Fetches every tracked repo — Settings ▸ Integrations ▸ "Fetch tracked repos on
    /// startup", off by default because it is a network call per repo on every launch.
    /// Returns how many succeeded; offline degrades this feature and nothing else.
    /// </summary>
    public async Task<int> FetchAllAsync(IReadOnlyList<Mod> repos, CancellationToken ct = default)
    {
        var fetched = 0;
        foreach (var mod in repos)
        {
            if (ct.IsCancellationRequested) break;
            if (await _client.FetchAsync(mod.RootPath, ct).ConfigureAwait(false)) fetched++;
        }

        return fetched;
    }

    /// <summary>
    /// Where the git binary is, for the Integrations card's second line. Resolved from
    /// <c>PATH</c> ourselves rather than by shelling out to <c>where</c>/<c>which</c>:
    /// one more process launch to learn a path we can look up, and two more code paths
    /// to get wrong per platform.
    /// </summary>
    public string? ResolveGitPath() =>
        FindOnPath(Environment.GetEnvironmentVariable("PATH"), _fs.FileExists);

    /// <summary>
    /// Pure: the first existing git executable on a <c>PATH</c> string. Separated out
    /// because "did we split PATH the way this platform writes it" is worth a test and
    /// needs neither a filesystem nor a git install.
    /// </summary>
    public static string? FindOnPath(string? path, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // Windows writes git.exe; every other platform writes git. Trying both on both
        // costs one extra FileExists and removes a platform branch.
        string[] names = ["git.exe", "git"];

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = entry.Trim().Trim('"');
            if (dir.Length == 0) continue;

            foreach (var name in names)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; }  // an unusable PATH entry

                if (exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
