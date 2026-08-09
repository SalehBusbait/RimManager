using System.Collections.Concurrent;
using System.Text;
using RimManager.Core.Abstractions;

namespace RimManager.Core.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFileSystem"/> for testing the domain without touching
/// disk. Paths are normalized to '/' separators and compared case-sensitively;
/// that is enough for Core tests, which drive it with paths they control.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirs = new(StringComparer.Ordinal);
    private readonly IClock _clock;

    public InMemoryFileSystem(IClock clock) => _clock = clock;

    private static string Norm(string path) => path.Replace('\\', '/').TrimEnd('/');

    public void AddFile(string path, string contents)
    {
        var p = Norm(path);
        _files[p] = Encoding.UTF8.GetBytes(contents);
        EnsureParentDirs(p);
    }

    private void EnsureParentDirs(string path)
    {
        var dir = path;
        int slash;
        while ((slash = dir.LastIndexOf('/')) > 0)
        {
            dir = dir[..slash];
            _dirs.Add(dir);
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));

    public bool DirectoryExists(string path) => _dirs.Contains(Norm(path));

    public FileEntry? Stat(string path)
    {
        var p = Norm(path);
        if (_files.TryGetValue(p, out var bytes))
        {
            return new FileEntry(p, IsDirectory: false, bytes.Length, _clock.UtcNow);
        }

        return _dirs.Contains(p)
            ? new FileEntry(p, IsDirectory: true, Size: 0, _clock.UtcNow)
            : null;
    }

    public IEnumerable<FileEntry> EnumerateEntries(string directory)
    {
        var prefix = Norm(directory) + "/";
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var rest = file[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                if (seen.Add(file) && Stat(file) is { } fe) yield return fe;
            }
            else
            {
                var childDir = prefix + rest[..slash];
                if (seen.Add(childDir) && Stat(childDir) is { } de) yield return de;
            }
        }

        // Directories are listed from _dirs as well, not only inferred from the files
        // inside them. An EMPTY directory was invisible here while DirectoryExists happily
        // reported it — the double disagreeing with itself. It matters because an empty
        // folder is a real state: it is what a mod looks like for the seconds between Steam
        // creating it and the download landing, which is precisely what ModRootProbe has to
        // sit through without reporting a mod that cannot be read yet.
        foreach (var dir in _dirs.Where(d => d.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var rest = dir[prefix.Length..];
            var slash = rest.IndexOf('/');
            var child = slash < 0 ? dir : prefix + rest[..slash];

            if (seen.Add(child) && Stat(child) is { } de) yield return de;
        }
    }

    public Stream OpenRead(string path) =>
        new MemoryStream(_files[Norm(path)], writable: false);

    public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[Norm(path)]);

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) =>
        Task.FromResult(ReadAllText(path));

    public void CreateDirectory(string path) => _dirs.Add(Norm(path));

    public void DeleteFile(string path) => _files.TryRemove(Norm(path), out _);

    public void DeleteDirectory(string path, bool recursive)
    {
        var p = Norm(path);
        _dirs.Remove(p);
        var prefix = p + "/";
        foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _files.TryRemove(key, out _);
        _dirs.RemoveWhere(d => d.StartsWith(prefix, StringComparison.Ordinal));
    }

    public Task<string?> AtomicWriteAsync(
        string path, ReadOnlyMemory<byte> contents, bool backup = true, CancellationToken ct = default)
    {
        var p = Norm(path);
        string? backupPath = null;
        if (backup && _files.ContainsKey(p))
        {
            backupPath = $"{p}.{_clock.UtcNow.UtcTicks}.bak";
            _files[backupPath] = _files[p];
        }

        _files[p] = contents.ToArray();
        EnsureParentDirs(p);
        return Task.FromResult(backupPath);
    }
}
