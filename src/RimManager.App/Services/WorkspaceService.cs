using System.Collections.Concurrent;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Locators;
using RimManager.Core.Parsing;
using RimManager.Core.Scanning;
using RimManager.Core.Writing;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.App.Services;

/// <summary>The result of loading the install: its config and a fresh scan.</summary>
public sealed record WorkspaceSnapshot(ModsConfig? ModsConfig, ScanResult Scan);

/// <summary>
/// The App's bridge to the headless core: resolves the one install's paths, and
/// performs the (synchronous, CPU-bound) scan that the UI runs on a background thread.
/// </summary>
public sealed class WorkspaceService
{
    private readonly IFileSystem _fs;
    private readonly IPlatformEnvironment _env;
    private readonly InstallPathsRepository _paths;

    public WorkspaceService(IFileSystem fs, IPlatformEnvironment env)
    {
        _fs = fs;
        _env = env;
        _paths = new InstallPathsRepository(fs);
    }

    /// <summary>
    /// The one filesystem seam, for callers that need to probe paths rather than act
    /// on them — Settings ▸ Paths validates live and reports what it found.
    /// </summary>
    public IFileSystem FileSystem => _fs;

    /// <summary>Persists edited paths (e.g. from the Settings screen).</summary>
    public Task SavePathsAsync(InstallPaths paths) => _paths.SaveAsync(paths);

    /// <summary>The stored paths, or null before any setup has run.</summary>
    public InstallPaths? LoadPaths() => _paths.Load();

    /// <summary>
    /// Steam's own executable, or null when it is not installed — for the default launch
    /// command (<c>2g</c>).
    /// <para>
    /// Resolved from <see cref="IPlatformEnvironment.SteamClientRoots"/>, which already
    /// reads the registry on Windows. Assuming <c>steam</c> is on <c>PATH</c> is what the
    /// first version did, and that is only true on Linux — on Windows Steam installs to
    /// Program Files and adds nothing to <c>PATH</c>.
    /// </para>
    /// </summary>
    public string? LocateSteamExecutable()
    {
        // steam.sh before steam on Linux: the shell script is the supported entry point
        // and sets up the runtime the bare binary expects.
        string[] names = OperatingSystem.IsWindows()
            ? ["steam.exe"]
            : OperatingSystem.IsMacOS()
                ? ["Steam.app"]
                : ["steam.sh", "steam"];

        foreach (var root in _env.SteamClientRoots)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(root, name);
                if (_fs.FileExists(candidate) || _fs.DirectoryExists(candidate)) return candidate;
            }
        }

        return null;
    }

    /// <summary>Best-effort auto-detected paths for the first-run screen.</summary>
    public (string? Game, string? Config, string? Workshop) DetectPaths()
    {
        var install = InstallLocator.LocateAll(_env, _fs).FirstOrDefault();
        return (install?.GameDir, InstallLocator.LocateConfigDirectory(_env, _fs), install?.WorkshopDir);
    }

    /// <summary>Creates and persists the install's paths from explicit folders (first run).</summary>
    public async Task<InstallPaths> CreatePathsAsync(string gameDir, string? configDir, string? workshopDir)
    {
        var paths = new InstallPaths
        {
            GameDir = gameDir.Trim(),
            ConfigDir = configDir,
            WorkshopDir = workshopDir,
        };
        await _paths.SaveAsync(paths);
        return paths;
    }

    /// <summary>Per-mod tags, categories and notes, rooted at the app data dir.</summary>
    public MetadataRepository Metadata() => new(_fs);

    /// <summary>Layout, update snoozes and rule overrides (R1c), rooted at the app data dir.</summary>
    public WorkspaceStateRepository State() => new(_fs);

    /// <summary>Raw About.xml text for a mod, or null if unreadable.</summary>
    public string? ReadAboutXml(Mod mod)
    {
        var path = Path.Combine(mod.RootPath, "About", "About.xml");
        if (!_fs.FileExists(path)) return null;
        try { return _fs.ReadAllText(path); }
        catch (IOException) { return null; }
    }

    /// <summary>Path to a mod's preview image, or null if absent.</summary>
    public string? PreviewPath(Mod mod)
    {
        var path = Path.Combine(mod.RootPath, "About", "Preview.png");
        return _fs.FileExists(path) ? path : null;
    }

    private readonly ConcurrentDictionary<string, long> _folderSizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bytes on disk for one mod, walked off the UI thread and then remembered.
    /// <para>
    /// Keyed on the ROOT PATH, not the packageId: two installs of the same mod (a
    /// local copy shadowing a Workshop one) are different folders with different
    /// sizes, and the packageId cannot tell them apart. The cache lives for the
    /// session — a mod's folder changing size under a running RimManager means an
    /// update landed, and that is what the rescan is for.
    /// </para>
    /// </summary>
    public Task<long> FolderSizeAsync(Mod mod, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (_folderSizes.TryGetValue(mod.RootPath, out var known)) return Task.FromResult(known);

        return Task.Run(() =>
        {
            // A folder deleted or locked mid-walk is not worth an error path in the
            // info pane; the caller renders nothing and the next selection retries.
            try
            {
                var bytes = FolderSize.Bytes(_fs, mod.RootPath, ct);
                _folderSizes[mod.RootPath] = bytes;
                return bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0L;
            }
        }, ct);
    }

    /// <summary>Writes ModsConfig.xml (atomic + backup + running-game guard).</summary>
    public Task<ApplyResult> ApplyAsync(InstallPaths paths, ModsConfig config)
    {
        if (paths.ModsConfigPath is not { } target)
            return Task.FromResult(new ApplyResult(false, null, "No config directory is set."));

        var service = new ApplyService(_fs, new RimWorldProcessDetector());
        // O5 · into RimManager's own folder, not RimWorld's config directory. Backups
        // beside the file are backups inside a folder the game owns and Steam validates,
        // where they read as more config to every other tool that looks there.
        return service.ApplyAsync(target, config, AppPaths.BackupsDir);
    }

    /// <summary>Returns the stored paths, or auto-creates them from the detected install.</summary>
    public async Task<InstallPaths?> EnsurePathsAsync()
    {
        if (_paths.Load() is { } existing) return existing;

        var install = InstallLocator.LocateAll(_env, _fs).FirstOrDefault();
        if (install is null) return null;

        var paths = new InstallPaths
        {
            GameDir = install.GameDir,
            ConfigDir = InstallLocator.LocateConfigDirectory(_env, _fs),
            WorkshopDir = install.WorkshopDir,
        };
        await _paths.SaveAsync(paths);
        return paths;
    }

    /// <summary>Scans the install. CPU-bound and synchronous — call from a background thread.</summary>
    /// <param name="progress">Status-bar text.</param>
    /// <param name="scanProgress">Per-folder counts for the first-scan state (<c>2k</c>).</param>
    public WorkspaceSnapshot Load(
        InstallPaths paths,
        IProgress<string>? progress = null,
        IProgress<ScanProgress>? scanProgress = null)
    {
        progress?.Report("Reading config…");

        ModsConfig? modsConfig = null;
        if (paths.ModsConfigPath is { } configPath && _fs.FileExists(configPath))
            modsConfig = ModsConfigParser.Parse(_fs.ReadAllText(configPath));

        var roots = new List<ModSourceRoot>
        {
            new(paths.DataDir, ModSource.Core),
            new(paths.LocalModsDir, ModSource.Local),
        };
        if (paths.WorkshopDir is not null) roots.Add(new ModSourceRoot(paths.WorkshopDir, ModSource.Workshop));

        progress?.Report("Scanning mods…");
        // The same cache file the CLI uses (ScanWorkflow.OpenCache) — one cache, so one
        // CacheVersion bump reaches both shells.
        using var cache = SqliteModCache.Open(Path.Combine(AppPaths.CacheDir, "mods.db"));
        var scan = new ModScanner(_fs, cache).Scan(roots, modsConfig?.MajorMinor, scanProgress);

        progress?.Report($"Scanned {scan.Mods.Length} mods.");
        return new WorkspaceSnapshot(modsConfig, scan);
    }
}
