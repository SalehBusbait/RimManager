using RimManager.Core.Abstractions;

namespace RimManager.Core.Scanning;

/// <summary>
/// Adds up what a mod occupies on disk.
/// <para>
/// Deliberately NOT part of the scan. N10 measured a warm scan of 564 mods at 0.8s
/// by never opening a file — the scanner stats <c>About.xml</c> and stops — and a
/// recursive walk of every mod root would be a different order of cost paid on every
/// startup for a number almost nobody looks at. This runs for ONE mod, when that mod
/// is selected, and the caller caches the answer.
/// </para>
/// </summary>
public static class FolderSize
{
    /// <summary>
    /// Total bytes under <paramref name="root"/>, including every subdirectory.
    /// Returns 0 for a path that does not exist.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive, and it remembers where it has been: a
    /// directory junction pointing at an ancestor is a loop, and on Windows those
    /// exist in the wild (Steam library moves leave them behind). A recursive walk
    /// would spin until the stack ran out.
    /// </remarks>
    public static long Bytes(IFileSystem fs, string root, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        if (string.IsNullOrWhiteSpace(root) || !fs.DirectoryExists(root)) return 0;

        var total = 0L;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var entry in fs.EnumerateEntries(pending.Pop()))
            {
                if (!entry.IsDirectory) { total += entry.Size; continue; }
                if (seen.Add(entry.FullPath)) pending.Push(entry.FullPath);
            }
        }

        return total;
    }
}
