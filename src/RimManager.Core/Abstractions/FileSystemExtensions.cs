namespace RimManager.Core.Abstractions;

public static class FileSystemExtensions
{
    /// <summary>Enumerates every file under <paramref name="directory"/> recursively (empty if absent).</summary>
    public static IEnumerable<FileEntry> EnumerateFilesRecursive(this IFileSystem fs, string directory)
    {
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var entry in fs.EnumerateEntries(dir))
            {
                if (entry.IsDirectory) stack.Push(entry.FullPath);
                else yield return entry;
            }
        }
    }
}
