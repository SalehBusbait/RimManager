using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Storage.Repositories;

/// <summary>How many files, and how much disk, a captured settings snapshot holds.</summary>
public readonly record struct ModSettingsCapture(int Files, long Bytes)
{
    public bool IsEmpty => Files == 0;
}

/// <summary>
/// Captures and restores a modlist's in-game mod settings — the files RimWorld's
/// <c>Config</c> folder accumulates alongside <c>ModsConfig.xml</c>.
/// <para>
/// This is the one thing an instance could genuinely have isolated, and the reason the
/// owner flagged mod settings as the only real objection to removing instances. Settings
/// are global to the save-data folder, so two modlists that tune the same mod differently
/// otherwise overwrite each other silently.
/// </para>
/// <para>
/// Files are copied <b>wholesale and never parsed</b>. They are mod-authored XML, text and
/// occasionally caches, with no specification and no shared schema; anything cleverer than
/// a byte copy is a parser for a format that does not exist.
/// </para>
/// <para>
/// Restore <b>never deletes</b>. A file present in Config but absent from the incoming
/// snapshot is left alone, because it may belong to a mod both lists share whose settings
/// the incoming list simply never captured. Deleting it would destroy tuning that nothing
/// asked us to touch; the cost is that settings can linger, which the next capture folds
/// back into whichever list is current.
/// </para>
/// </summary>
public sealed class ModSettingsStore(IFileSystem fs, string? root = null)
{
    private readonly string _root = Path.Combine(root ?? AppPaths.Root, "modsettings");

    public string DirectoryFor(string modlistId) => Path.Combine(_root, modlistId);

    /// <summary>What is currently stored for a list, for the Settings card and the switch prompt.</summary>
    public ModSettingsCapture Stored(string modlistId)
    {
        var dir = DirectoryFor(modlistId);
        if (!fs.DirectoryExists(dir)) return default;

        var files = 0;
        long bytes = 0;

        foreach (var entry in fs.EnumerateEntries(dir).Where(e => !e.IsDirectory))
        {
            files++;
            bytes += entry.Size;
        }

        return new ModSettingsCapture(files, bytes);
    }

    /// <summary>
    /// Copies the live mod settings into this list's snapshot, replacing whatever was
    /// there. Called when switching AWAY from a list, so its tuning is preserved as it
    /// actually stands rather than as it stood when the list was made.
    /// </summary>
    /// <param name="progress">
    /// Ticked per file. The list is materialised first so the TOTAL is known before the
    /// first copy — a bar whose total grows as it runs is worse than no bar at all, which is
    /// the rule <see cref="ScanProgress"/> already states for the scan.
    /// </param>
    public async Task<ModSettingsCapture> CaptureAsync(
        string modlistId, string configDir, CancellationToken ct = default,
        IProgress<ScanProgress>? progress = null)
    {
        if (!fs.DirectoryExists(configDir)) return default;

        var target = DirectoryFor(modlistId);
        fs.CreateDirectory(target);

        // Write FIRST, prune after — never clear then fill.
        //
        // Clearing first is the obvious way to stop a stale file from a previous capture
        // being handed back as though it were current, and it was how this worked. But it
        // destroys the previous good capture before the new one exists: a failure part way
        // through — a locked file, a full disk, a cancellation — left the list with a
        // PARTIAL capture and no way back to the complete one, and the partial would be
        // restored later as if it were whole. Losing settings while trying to preserve
        // them is the worst possible trade.
        //
        // Additive-then-prune leaves a superset on failure, which is recoverable, and is
        // exactly as correct on success because the prune only runs once every write has.
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = 0;
        long bytes = 0;

        var sources = fs.EnumerateEntries(configDir)
            .Where(e => !e.IsDirectory)
            .Where(e => ModSettingsFiles.ShouldCapture(Path.GetFileName(e.FullPath)))
            .ToList();

        foreach (var entry in sources)
        {
            var name = Path.GetFileName(entry.FullPath);
            progress?.Report(new ScanProgress(files, sources.Count, name));

            var payload = await ReadAsync(entry.FullPath, ct).ConfigureAwait(false);
            await fs.AtomicWriteAsync(Path.Combine(target, name), payload, backup: false, ct)
                .ConfigureAwait(false);

            written.Add(name);
            files++;
            bytes += payload.Length;
        }

        progress?.Report(new ScanProgress(files, sources.Count, string.Empty));

        // Only now: anything left from a previous capture that this one did not produce.
        foreach (var stale in fs.EnumerateEntries(target)
                     .Where(e => !e.IsDirectory)
                     .Where(e => !written.Contains(Path.GetFileName(e.FullPath)))
                     .ToList())
        {
            fs.DeleteFile(stale.FullPath);
        }

        return new ModSettingsCapture(files, bytes);
    }

    /// <summary>
    /// Writes this list's captured settings back into the game's Config folder. Returns
    /// how many files were written; zero when the list has never captured anything, which
    /// is the normal case and not an error.
    /// </summary>
    /// <param name="progress">Ticked per file, on a total known up front. See CaptureAsync.</param>
    public async Task<int> RestoreAsync(
        string modlistId, string configDir, CancellationToken ct = default,
        IProgress<ScanProgress>? progress = null)
    {
        var source = DirectoryFor(modlistId);
        if (!fs.DirectoryExists(source) || !fs.DirectoryExists(configDir)) return 0;

        var written = 0;

        // Checked on the way OUT as well as in. A snapshot directory is a folder on disk
        // that anything could have dropped a file into, and restoring Prefs.xml would change
        // the player's resolution.
        var sources = fs.EnumerateEntries(source)
            .Where(e => !e.IsDirectory)
            .Where(e => ModSettingsFiles.ShouldCapture(Path.GetFileName(e.FullPath)))
            .ToList();

        foreach (var entry in sources)
        {
            var name = Path.GetFileName(entry.FullPath);
            progress?.Report(new ScanProgress(written, sources.Count, name));

            var payload = await ReadAsync(entry.FullPath, ct).ConfigureAwait(false);
            await fs.AtomicWriteAsync(Path.Combine(configDir, name), payload, backup: false, ct)
                .ConfigureAwait(false);

            written++;
        }

        progress?.Report(new ScanProgress(written, sources.Count, string.Empty));
        return written;
    }

    /// <summary>Drops a list's snapshot, when the list is deleted or capture is turned off.</summary>
    public void Forget(string modlistId)
    {
        var dir = DirectoryFor(modlistId);
        if (fs.DirectoryExists(dir)) fs.DeleteDirectory(dir, recursive: true);
    }

    private async Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken ct)
    {
        using var stream = fs.OpenRead(path);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
