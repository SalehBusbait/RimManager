namespace RimManager.Core.Abstractions;

/// <summary>
/// The single I/O seam for the whole application. <c>RimManager.Core</c> never
/// touches disk directly (engineering constraint #1); everything goes through
/// this interface so the domain is testable with an in-memory double.
/// </summary>
/// <remarks>
/// The shape is deliberately built around the scanner's hot path: cheap stat
/// (<see cref="Stat"/>, <see cref="EnumerateEntries"/>) so a 1,500-mod cold scan
/// can be keyed on <c>(path, mtime, size)</c> without opening files, plus an
/// atomic-write primitive so the "temp file + backup + move" rule for
/// <c>ModsConfig.xml</c> (constraint #2) lives in one audited place.
/// </remarks>
public interface IFileSystem
{
    // --- existence / stat -------------------------------------------------

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Cheap stat, or <c>null</c> if the entry does not exist.</summary>
    FileEntry? Stat(string path);

    /// <summary>
    /// Enumerates immediate children of <paramref name="directory"/>. Returns an
    /// empty sequence if the directory does not exist (never throws for absence).
    /// </summary>
    IEnumerable<FileEntry> EnumerateEntries(string directory);

    // --- read -------------------------------------------------------------

    Stream OpenRead(string path);

    string ReadAllText(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken ct = default);

    // --- write ------------------------------------------------------------

    void CreateDirectory(string path);

    /// <summary>Deletes a file if it exists; a no-op if it does not.</summary>
    void DeleteFile(string path);

    /// <summary>Deletes a directory if it exists; a no-op if it does not.</summary>
    void DeleteDirectory(string path, bool recursive);

    /// <summary>
    /// Atomically replaces <paramref name="path"/> with <paramref name="contents"/>:
    /// write to a sibling temp file, fsync, optionally back up the existing file,
    /// then move into place. A partially written file must never be observable at
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="backup">
    /// When true and the target already exists, the previous contents are copied
    /// to a timestamped backup before replacement (constraint #2).
    /// </param>
    /// <returns>The path of the backup written, or null if none was created.</returns>
    Task<string?> AtomicWriteAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        bool backup = true,
        CancellationToken ct = default);
}
