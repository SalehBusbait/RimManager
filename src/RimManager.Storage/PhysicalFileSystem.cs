using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>
/// The real, disk-backed <see cref="IFileSystem"/>. This is the only place the
/// application performs file I/O; <c>RimManager.Core</c> depends on the interface.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private readonly IClock _clock;

    public PhysicalFileSystem(IClock? clock = null) => _clock = clock ?? SystemClock.Instance;

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileEntry? Stat(string path)
    {
        var file = new FileInfo(path);
        if (file.Exists)
        {
            return new FileEntry(file.FullName, IsDirectory: false, file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        }

        var dir = new DirectoryInfo(path);
        if (dir.Exists)
        {
            return new FileEntry(dir.FullName, IsDirectory: true, Size: 0,
                new DateTimeOffset(dir.LastWriteTimeUtc, TimeSpan.Zero));
        }

        return null;
    }

    public IEnumerable<FileEntry> EnumerateEntries(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var entry = Stat(path);
            if (entry is { } value)
            {
                yield return value;
            }
        }
    }

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) =>
        File.ReadAllTextAsync(path, ct);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive);
    }

    public async Task<string?> AtomicWriteAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        bool backup = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Path has no directory: {path}", nameof(path));
        Directory.CreateDirectory(directory);

        // 1. Write to a sibling temp file and flush to disk.
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        string? backupPath = null;
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(contents, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // 2. Back up the existing target before we clobber it.
            if (backup && File.Exists(fullPath))
            {
                var stamp = _clock.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
                backupPath = $"{fullPath}.{stamp}.bak";
                File.Copy(fullPath, backupPath, overwrite: true);
                PruneBackups(directory, Path.GetFileName(fullPath));
            }

            // 3. Move into place. Replace when the target already exists (atomic on
            //    the same volume); plain move when it does not.
            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return backupPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { /* best-effort cleanup of the temp file */ }
            }
        }
    }

    /// <summary>
    /// How many timestamped backups to keep per file. Constraint #2 requires a backup
    /// before every overwrite; it does not require keeping every one forever.
    /// </summary>
    public const int BackupsKept = 20;

    /// <summary>
    /// Drops all but the newest <see cref="BackupsKept"/> backups of one file.
    /// <para>
    /// Without this they grow without bound, and the surfaces that save on every edit make
    /// that fast: on a real install a few days of use left 55 <c>tags.json</c> backups, 50
    /// of <c>modmeta.json</c> and 46 of <c>instance.json</c> — the last because the
    /// last-used stamp rewrites it on every launch. Twenty is far more history than any
    /// recovery has ever needed, and the file names sort chronologically so "newest" is
    /// just the tail.
    /// </para>
    /// </summary>
    private static void PruneBackups(string directory, string fileName)
    {
        try
        {
            var stale = Directory
                .EnumerateFiles(directory, $"{fileName}.*.bak")
                .Order(StringComparer.Ordinal)   // the stamp is yyyyMMddTHHmmssZ, so this is chronological
                .SkipLast(BackupsKept)
                .ToList();

            foreach (var path in stale)
            {
                try { File.Delete(path); }
                catch (IOException) { /* someone else has it open; it will go next time */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruning is housekeeping. Failing it must never fail the write it follows —
            // the data is already safely on disk by the time we get here.
        }
    }
}
