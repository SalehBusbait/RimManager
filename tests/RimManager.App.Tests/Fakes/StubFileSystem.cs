using RimManager.Core.Abstractions;

namespace RimManager.App.Tests.Fakes;

/// <summary>
/// A read-only <see cref="IFileSystem"/> stub for path probing.
/// <para>
/// Deliberately not the Core tests' full <c>InMemoryFileSystem</c>: referencing one
/// test project from another to borrow a fake couples their lifetimes, and the probe
/// only ever asks four questions. Every write member throws, so a probe that started
/// writing would fail loudly rather than silently touch a user's disk.
/// </para>
/// </summary>
public sealed class StubFileSystem : IFileSystem
{
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public StubFileSystem WithDirectory(params string[] paths)
    {
        foreach (var path in paths) _dirs.Add(Normalize(path));
        return this;
    }

    public StubFileSystem WithFile(string path, string contents = "")
    {
        var full = Normalize(path);
        _files[full] = contents;

        // EVERY ancestor, not just the immediate parent: a real filesystem has them
        // all, and registering one level made "/ws/1234/About" exist while
        // "/ws/1234" did not — so enumerating "/ws" found nothing.
        for (var dir = Path.GetDirectoryName(full); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            _dirs.Add(dir);

        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _dirs.Contains(Normalize(path));

    public FileEntry? Stat(string path) => null;

    public IEnumerable<FileEntry> EnumerateEntries(string directory)
    {
        var root = Normalize(directory);
        return _dirs
            .Where(d => d.Length > root.Length
                        && d.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !d[(root.Length + 1)..].Contains(Path.DirectorySeparatorChar))
            .Select(d => new FileEntry(d, IsDirectory: true, Size: 0, LastWriteUtc: default));
    }

    public string ReadAllText(string path) =>
        _files.TryGetValue(Normalize(path), out var text)
            ? text
            : throw new FileNotFoundException(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(ReadAllText(path));

    public Stream OpenRead(string path) => throw new NotSupportedException();
    public void CreateDirectory(string path) => throw new NotSupportedException();
    public void DeleteFile(string path) => throw new NotSupportedException();
    public void DeleteDirectory(string path, bool recursive) => throw new NotSupportedException();

    public Task<string?> AtomicWriteAsync(
        string path, ReadOnlyMemory<byte> contents, bool backup = true, CancellationToken ct = default) =>
        throw new NotSupportedException("A path probe must never write.");

    private static string Normalize(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
}
