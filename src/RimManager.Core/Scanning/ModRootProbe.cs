using System.Collections.Immutable;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Scanning;

/// <summary>What appeared or disappeared in the mod roots since the last poll.</summary>
/// <param name="Added">Folder names now established as mods. Workshop ids, or local folder names.</param>
/// <param name="Removed">Folder names that were established and are now gone.</param>
/// <param name="Pending">
/// Folders present but not yet readable as mods — mid-install. Reported as a count so a
/// caller can say "and one still arriving" rather than pretending it is not there.
/// </param>
public sealed record ModRootChanges(
    ImmutableArray<string> Added,
    ImmutableArray<string> Removed,
    int Pending)
{
    public static readonly ModRootChanges None = new([], [], 0);

    /// <summary>True when there is something worth telling the user about.</summary>
    public bool Any => !Added.IsEmpty || !Removed.IsEmpty;
}

/// <summary>
/// Notices a mod folder appearing or disappearing, by listing the top level of each mod root
/// and diffing the names.
/// <para>
/// <b>Top level only, and that is the whole design.</b> A mod is a folder; a mod being
/// installed or removed is that folder arriving or going. Everything <em>inside</em> a mod
/// folder is invisible here on purpose — an update rewrites files in place, and updates are
/// the update checker's job, which answers them exactly (installed <c>timeupdated</c> against
/// the live <c>time_updated</c>) rather than by guessing from file writes.
/// </para>
/// <para>
/// <b>Polling rather than a <c>FileSystemWatcher</c>, and the reason is correctness.</b>
/// Measured on Windows with one continuous subscription at
/// <c>NotifyFilters.DirectoryName</c>: a folder built <em>in place</em> raises exactly one
/// <c>Created</c>, fired while the directory is still <b>empty</b>, and then nothing ever
/// again — the <c>About.xml</c> written half a second later is inside the new folder and so
/// outside the subscription by design. No debounce can rescue that; after the first event
/// there is nothing left to debounce against, and the mod stays invisible until someone
/// presses Refresh. Which is the bug this exists to fix. Polling re-examines every tick, so
/// the folder is picked up as soon as it is readable.
/// </para>
/// <para>
/// It is also nearly free and, unlike a watcher, testable: listing the real 558-entry Steam
/// root measures <b>0.13ms</b>, so two roots on a 2s timer cost about 0.007% of a core, and
/// the whole thing runs on <see cref="IFileSystem"/> — which has an in-memory double, so
/// every case below is a unit test rather than a thing you can only try by subscribing to a
/// mod and watching.
/// </para>
/// </summary>
/// <remarks>
/// A directory's mtime <em>is</em> a sufficient trigger — measured, it moves when a child
/// directory is added or removed and does not move for a file written inside a child, nor for
/// a directory created two levels down. It is deliberately <b>not</b> used: it would save
/// 0.128ms every two seconds, it is the first thing to behave differently on a network share
/// or a non-NTFS volume, and the in-memory double cannot model it — so the saving would be
/// bought with an untestable fast path. The measurement still earned its place: it is what
/// proves the top-level scope really does exclude updates.
/// </remarks>
public sealed class ModRootProbe
{
    /// <summary>
    /// How many consecutive polls a folder must look like a finished mod before it is
    /// reported. At a 2s tick this is a 2–4 second settle, which is what keeps a mod that is
    /// still being unzipped out of the notice.
    /// </summary>
    public const int ReadyPollsBeforeReporting = 2;

    /// <summary>
    /// And how many consecutive polls it must be gone. Two, because an update that deletes
    /// and recreates the folder would otherwise read as a removal followed by an addition of
    /// the same mod.
    /// </summary>
    public const int AbsentPollsBeforeReporting = 2;

    /// <summary>
    /// After this many polls without becoming readable, a folder stops being probed. Steam
    /// leaves failed downloads behind for ever, and a probe that never gives up is a probe
    /// that stats a dead folder until the app closes.
    /// </summary>
    public const int MaxPollsWaitingToBecomeReadable = 15;

    private readonly IFileSystem _fs;
    private readonly Dictionary<string, RootState> _roots = new(StringComparer.OrdinalIgnoreCase);

    public ModRootProbe(IFileSystem fs) => _fs = fs;

    /// <summary>
    /// Lists each root and reports what changed. A root seen for the first time is
    /// <b>baselined silently</b> — the first poll after startup must not announce all 558
    /// mods as new.
    /// </summary>
    public ModRootChanges Poll(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        // Roots that are no longer configured stop being tracked — the config folder can be
        // edited in Settings while the app runs, and remembering a path nobody watches any
        // more would report its contents as "removed" if it were ever re-added.
        foreach (var gone in _roots.Keys.Where(k => !roots.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            _roots.Remove(gone);

        var added = ImmutableArray.CreateBuilder<string>();
        var removed = ImmutableArray.CreateBuilder<string>();
        var pending = 0;

        foreach (var root in roots)
        {
            if (!_roots.TryGetValue(root, out var state))
            {
                _roots[root] = Baseline(root);
                continue;
            }

            Diff(root, state, added, removed);
            pending += state.WaitingToBecomeReadable.Count;
        }

        return added.Count == 0 && removed.Count == 0 && pending == 0
            ? ModRootChanges.None
            : new ModRootChanges(added.ToImmutable(), removed.ToImmutable(), pending);
    }

    /// <summary>
    /// Forgets everything and re-reads, reporting nothing.
    /// <para>
    /// Called after a rescan, so what the user has just been shown becomes the new baseline.
    /// Without it our own SteamCMD download or delete-from-disk would surface a moment later
    /// as somebody else's news — the app telling the user about the thing the app just did.
    /// </para>
    /// </summary>
    public void Rebaseline(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        _roots.Clear();
        foreach (var root in roots) _roots[root] = Baseline(root);
    }

    private RootState Baseline(string root)
    {
        var state = new RootState();

        foreach (var name in TopLevelFolders(root))
        {
            // A folder that is mid-install when we first look is NOT established — it goes
            // into the waiting set, so finishing it is still an event worth reporting. That
            // is the case of opening the app while a download is running.
            if (IsReadableMod(root, name)) state.Established.Add(name);
            else state.WaitingToBecomeReadable[name] = 0;
        }

        return state;
    }

    private void Diff(
        string root,
        RootState state,
        ImmutableArray<string>.Builder added,
        ImmutableArray<string>.Builder removed)
    {
        var present = TopLevelFolders(root).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in present)
        {
            if (state.Established.Contains(name))
            {
                state.Absences.Remove(name);   // it came back, or never left
                continue;
            }

            if (state.GivenUpOn.Contains(name)) continue;

            if (!IsReadableMod(root, name))
            {
                // Still arriving. Count the wait, and eventually stop looking.
                var waited = state.WaitingToBecomeReadable.GetValueOrDefault(name) + 1;
                if (waited >= MaxPollsWaitingToBecomeReadable)
                {
                    state.WaitingToBecomeReadable.Remove(name);
                    state.GivenUpOn.Add(name);
                }
                else
                {
                    state.WaitingToBecomeReadable[name] = waited;
                }

                state.ReadyPolls.Remove(name);
                continue;
            }

            state.WaitingToBecomeReadable.Remove(name);

            var ready = state.ReadyPolls.GetValueOrDefault(name) + 1;
            state.ReadyPolls[name] = ready;

            if (ready < ReadyPollsBeforeReporting) continue;

            state.ReadyPolls.Remove(name);
            state.Established.Add(name);
            added.Add(name);
        }

        // Anything established that is no longer on disk.
        foreach (var name in state.Established.ToList())
        {
            if (present.Contains(name)) continue;

            var absences = state.Absences.GetValueOrDefault(name) + 1;
            if (absences < AbsentPollsBeforeReporting)
            {
                state.Absences[name] = absences;
                continue;
            }

            state.Absences.Remove(name);
            state.Established.Remove(name);
            removed.Add(name);
        }

        // Folders that vanished before they were ever established leave no trace: a download
        // the user cancelled is not news.
        foreach (var name in state.WaitingToBecomeReadable.Keys.Where(n => !present.Contains(n)).ToList())
            state.WaitingToBecomeReadable.Remove(name);
        foreach (var name in state.ReadyPolls.Keys.Where(n => !present.Contains(n)).ToList())
            state.ReadyPolls.Remove(name);
        foreach (var name in state.GivenUpOn.Where(n => !present.Contains(n)).ToList())
            state.GivenUpOn.Remove(name);
    }

    private IEnumerable<string> TopLevelFolders(string root) =>
        _fs.EnumerateEntries(root)
            .Where(e => e.IsDirectory)
            .Select(e => Path.GetFileName(e.FullPath.TrimEnd('/', '\\')))
            .Where(n => !string.IsNullOrEmpty(n));

    /// <summary>
    /// A directory is not a mod until RimWorld could load it as one.
    /// <para>
    /// Existence of <c>About/About.xml</c> is the whole test, and it is measured rather than
    /// assumed: all 558 folders in the real Workshop root have one, so a folder without one
    /// is mid-install or garbage — never a legitimate mod. It is deliberately not reported as
    /// <em>invalid</em>; misclassifying a half-downloaded mod as broken is RimSort's most
    /// confusing user-facing bug, and the folder is simply not ready yet.
    /// </para>
    /// </summary>
    private bool IsReadableMod(string root, string name) =>
        _fs.FileExists(Path.Combine(root, name, "About", "About.xml"));

    private sealed class RootState
    {
        /// <summary>Folders already counted as installed mods.</summary>
        public HashSet<string> Established { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ready, but not yet for enough consecutive polls to be believed.</summary>
        public Dictionary<string, int> ReadyPolls { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Present but with no readable About.xml yet.</summary>
        public Dictionary<string, int> WaitingToBecomeReadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Waited too long. Left alone until the app rescans.</summary>
        public HashSet<string> GivenUpOn { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Established but currently missing, and for how many polls.</summary>
        public Dictionary<string, int> Absences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
