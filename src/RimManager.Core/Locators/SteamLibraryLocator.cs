using RimManager.Core.Abstractions;
using RimManager.Core.Parsing;

namespace RimManager.Core.Locators;

/// <summary>
/// Finds RimWorld Steam installs by parsing <c>libraryfolders.vdf</c> and checking
/// which library owns app <see cref="RimWorldAppId"/>. Pure logic over
/// <see cref="IFileSystem"/> + <see cref="IPlatformEnvironment"/>.
/// </summary>
public static class SteamLibraryLocator
{
    /// <summary>RimWorld's Steam AppID.</summary>
    public const string RimWorldAppId = "294100";

    public static IReadOnlyList<InstallLayout> Locate(IPlatformEnvironment env, IFileSystem fs)
    {
        var results = new List<InstallLayout>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamRoot in env.SteamClientRoots)
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!fs.FileExists(vdfPath)) continue;

            VdfNode root;
            try
            {
                root = VdfParser.Parse(fs.ReadAllText(vdfPath));
            }
            catch (IOException)
            {
                continue;
            }

            var libraries = root["libraryfolders"] ?? root;
            foreach (var (_, library) in libraries.Children)
            {
                var libraryPath = library["path"]?.Value;
                if (string.IsNullOrWhiteSpace(libraryPath)) continue;

                var apps = library["apps"];
                if (apps is null || !apps.Children.ContainsKey(RimWorldAppId)) continue;

                var gameDir = Path.Combine(libraryPath, "steamapps", "common", "RimWorld");
                if (!fs.DirectoryExists(gameDir)) continue;
                if (!seen.Add(gameDir)) continue;

                var workshopDir = Path.Combine(libraryPath, "steamapps", "workshop", "content", RimWorldAppId);

                results.Add(new InstallLayout
                {
                    GameDir = gameDir,
                    Kind = InstallKind.Steam,
                    WorkshopDir = fs.DirectoryExists(workshopDir) ? workshopDir : null,
                });
            }
        }

        return results;
    }
}
