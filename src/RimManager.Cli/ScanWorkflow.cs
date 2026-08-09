using System.Diagnostics;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Locators;
using RimManager.Core.Parsing;
using RimManager.Core.Scanning;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.Cli;

/// <summary>Resolved install + config + scan, shared by the read commands.</summary>
internal sealed record ScanContext(
    InstallLayout Install,
    string? ConfigDir,
    ModsConfig? ModsConfig,
    ScanResult Scan,
    long ScanMillis);

/// <summary>Shared setup: locate the install, read ModsConfig, scan mods, print the header.</summary>
internal static class ScanWorkflow
{
    public static ScanContext? Run(string? gameDir, string? workshopDir, string? configDir, bool noCache)
    {
        var fs = new PhysicalFileSystem();
        var env = new PlatformEnvironment();
        var saved = new InstallPathsRepository(fs).Load();

        var install = ResolveInstall(fs, env, saved, gameDir, workshopDir);
        if (install is null)
        {
            Console.Error.WriteLine("Could not locate a RimWorld installation. Pass --game-dir to specify one.");
            return null;
        }

        Console.WriteLine($"Game:     {install.GameDir}  ({install.Kind})");
        if (install.WorkshopDir is not null) Console.WriteLine($"Workshop: {install.WorkshopDir}");

        configDir ??= saved?.ConfigDir ?? InstallLocator.LocateConfigDirectory(env, fs);
        ModsConfig? modsConfig = null;
        if (configDir is not null)
        {
            var path = Path.Combine(configDir, "ModsConfig.xml");
            if (fs.FileExists(path))
            {
                modsConfig = ModsConfigParser.Parse(fs.ReadAllText(path));
                Console.WriteLine($"Config:   {configDir}");
                Console.WriteLine($"Version:  {modsConfig.Version}");
            }
        }

        using var cacheOwner = noCache ? null : OpenCache();
        IModCache cache = noCache ? NullModCache.Instance : cacheOwner!;
        var scanner = new ModScanner(fs, cache);

        var roots = install.ToSourceRoots().ToList();

        var sw = Stopwatch.StartNew();
        var scan = scanner.Scan(roots, modsConfig?.MajorMinor);
        sw.Stop();

        Console.WriteLine($"Scanned {scan.Mods.Length} mods in {sw.ElapsedMilliseconds} ms" +
            (noCache ? " (cache off)." : "."));
        Console.WriteLine();

        return new ScanContext(install, configDir, modsConfig, scan, sw.ElapsedMilliseconds);
    }

    /// <summary>The active mods as <see cref="Mod"/> objects in current load order (missing ones skipped).</summary>
    public static IReadOnlyList<Mod> ActiveMods(ScanContext ctx)
    {
        if (ctx.ModsConfig is null) return ctx.Scan.Mods;
        var ordered = new List<Mod>(ctx.ModsConfig.ActiveMods.Length);
        foreach (var id in ctx.ModsConfig.ActiveMods)
        {
            if (ctx.Scan.ById.TryGetValue(id, out var mod)) ordered.Add(mod);
        }

        return ordered;
    }

    /// <summary>
    /// Explicit flags beat the saved <c>paths.json</c>, which beats the locator. The
    /// middle step is what keeps the two shells honest with each other: a custom GameDir
    /// set in Settings used to be invisible here, so the CLI silently scanned the located
    /// Steam install instead — the GUI and the CLI must see the same install, the same
    /// way they must see the same rules. Manual is the accurate kind for saved paths:
    /// they were set by hand, not discovered.
    /// </summary>
    private static InstallLayout? ResolveInstall(
        PhysicalFileSystem fs, PlatformEnvironment env, InstallPaths? saved,
        string? gameDir, string? workshopDir)
    {
        if (gameDir is not null)
        {
            return new InstallLayout { GameDir = gameDir, Kind = InstallKind.Manual, WorkshopDir = workshopDir };
        }

        if (saved is not null)
        {
            return new InstallLayout
            {
                GameDir = saved.GameDir,
                Kind = InstallKind.Manual,
                WorkshopDir = workshopDir ?? saved.WorkshopDir,
            };
        }

        var install = InstallLocator.LocateAll(env, fs).FirstOrDefault();
        if (install is not null && workshopDir is not null) install = install with { WorkshopDir = workshopDir };
        return install;
    }

    private static SqliteModCache OpenCache() =>
        SqliteModCache.Open(Path.Combine(AppPaths.CacheDir, "mods.db"));
}
