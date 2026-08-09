using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using RimManager.Core.Abstractions;

namespace RimManager.Storage;

/// <summary>
/// Real <see cref="IPlatformEnvironment"/>: resolves Steam/GOG install roots and
/// RimWorld config directories per OS (paths from domain primer §3), including
/// Proton/Steam Deck layouts. All lists are best-guess candidates; the locators
/// verify them against the filesystem.
/// </summary>
public sealed class PlatformEnvironment : IPlatformEnvironment
{
    public OSPlatform Platform { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
        : OSPlatform.Linux;

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IReadOnlyList<string> SteamClientRoots => BuildSteamClientRoots();

    public IReadOnlyList<string> GogGameDirCandidates => BuildGogCandidates();

    public IReadOnlyList<string> ConfigDirectoryCandidates => BuildConfigCandidates();

    // --- Steam --------------------------------------------------------------

    private List<string> BuildSteamClientRoots()
    {
        var roots = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var reg = ReadSteamPathFromRegistry();
            if (reg is not null) roots.Add(reg);
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots.Add(Path.Combine(Home, "Library", "Application Support", "Steam"));
        }
        else // Linux
        {
            roots.Add(Path.Combine(Home, ".steam", "steam"));
            roots.Add(Path.Combine(Home, ".steam", "root"));
            roots.Add(Path.Combine(Home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        return Dedup(roots);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSteamPathFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string is { Length: > 0 } p
                ? p.Replace('/', '\\')
                : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    // --- GOG (best-effort) --------------------------------------------------

    private List<string> BuildGogCandidates()
    {
        var dirs = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            dirs.AddRange(ReadGogGameDirsFromRegistry());
            dirs.Add(@"C:\GOG Games\RimWorld");
            dirs.Add(@"C:\Program Files (x86)\GOG Galaxy\Games\RimWorld");
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.Add(Path.Combine(Home, "GOG Games", "RimWorld"));
            dirs.Add("/Applications/RimWorld.app/Contents/Resources");
        }
        else
        {
            dirs.Add(Path.Combine(Home, "GOG Games", "RimWorld", "game"));
            dirs.Add(Path.Combine(Home, "Games", "RimWorld"));
        }

        return Dedup(dirs);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> ReadGogGameDirsFromRegistry()
    {
        var found = new List<string>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var games = hive.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games")
                    ?? hive.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                if (games is null) continue;

                foreach (var id in games.GetSubKeyNames())
                {
                    using var game = games.OpenSubKey(id);
                    var name = game?.GetValue("gameName") as string;
                    if (name is null || !name.Contains("RimWorld", StringComparison.OrdinalIgnoreCase)) continue;
                    if (game!.GetValue("path") as string is { Length: > 0 } path) found.Add(path);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
            {
                // ignore this hive
            }
        }

        return found;
    }

    // --- Config directory ---------------------------------------------------

    private List<string> BuildConfigCandidates()
    {
        var dirs = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var localLow = Path.Combine(Home, "AppData", "LocalLow");
            dirs.Add(Path.Combine(localLow, "Ludeon Studios", "RimWorld by Ludeon Studios", "Config"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            dirs.Add(Path.Combine(Home, "Library", "Application Support", "RimWorld", "Config"));
        }
        else // Linux (native)
        {
            dirs.Add(Path.Combine(Home, ".config", "unity3d", "Ludeon Studios",
                "RimWorld by Ludeon Studios", "Config"));

            // Proton / Steam Deck: config lives inside the prefix under each Steam library.
            const string protonTail =
                "pfx/drive_c/users/steamuser/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config";
            foreach (var steamRoot in BuildSteamClientRoots())
            {
                dirs.Add(Path.Combine(steamRoot, "steamapps", "compatdata", "294100",
                    protonTail.Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        return Dedup(dirs);
    }

    private static List<string> Dedup(List<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (!string.IsNullOrWhiteSpace(p) && seen.Add(p)) result.Add(p);
        }

        return result;
    }
}
