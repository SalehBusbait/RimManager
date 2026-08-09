using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Storage.Persistence;

namespace RimManager.Storage.Repositories;

/// <summary>
/// The one install's paths, at <c>&lt;root&gt;/paths.json</c>. Replaces the per-instance
/// <c>instance.json</c>.
/// </summary>
public sealed class InstallPathsRepository(IFileSystem fs, string? root = null)
{
    private readonly string _file = Path.Combine(root ?? AppPaths.Root, "paths.json");
    private readonly JsonDocumentStore<InstallPaths> _store = new(fs);

    /// <summary>Null before first-run setup has picked a game folder.</summary>
    public InstallPaths? Load() => _store.Load(_file);

    public Task SaveAsync(InstallPaths paths, CancellationToken ct = default) =>
        _store.SaveAsync(_file, paths, ct: ct);
}
