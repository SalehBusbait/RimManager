using RimManager.Core.Abstractions;

namespace RimManager.Core.Locators;

/// <summary>
/// Aggregates all install-discovery strategies (Steam libraries, GOG candidates)
/// into a single ordered list of <see cref="InstallLayout"/>s, and resolves the
/// active config directory.
/// </summary>
public static class InstallLocator
{
    public static IReadOnlyList<InstallLayout> LocateAll(IPlatformEnvironment env, IFileSystem fs)
    {
        var installs = new List<InstallLayout>(SteamLibraryLocator.Locate(env, fs));

        foreach (var gogDir in env.GogGameDirCandidates)
        {
            if (!fs.DirectoryExists(gogDir)) continue;
            if (installs.Any(i => string.Equals(i.GameDir, gogDir, StringComparison.OrdinalIgnoreCase)))
                continue;

            // A plausible RimWorld dir has a Data/Core folder.
            if (!fs.DirectoryExists(Path.Combine(gogDir, "Data", "Core"))) continue;

            installs.Add(new InstallLayout { GameDir = gogDir, Kind = InstallKind.Gog });
        }

        return installs;
    }

    /// <summary>The first candidate config directory that actually contains <c>ModsConfig.xml</c>.</summary>
    public static string? LocateConfigDirectory(IPlatformEnvironment env, IFileSystem fs)
    {
        foreach (var dir in env.ConfigDirectoryCandidates)
        {
            if (fs.FileExists(Path.Combine(dir, "ModsConfig.xml"))) return dir;
        }

        return null;
    }
}
