using System.Text.RegularExpressions;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;

namespace RimManager.Core.Scanning;

/// <summary>
/// Derives <see cref="ContentFlags"/> from a mod folder by checking which content
/// subfolders exist. Cheap (file-existence only) so it stays inside the warm-scan
/// budget.
/// </summary>
public static partial class ContentDetector
{
    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex VersionFolderRegex();

    /// <summary>
    /// Detects content across the folders a mod actually loads for the active game
    /// version. If the mod ships <c>LoadFolders.xml</c> (~38% do) that is
    /// authoritative; otherwise we union the mod root with per-version subfolders
    /// (domain primer §3). <paramref name="activeMajorMinor"/> (e.g. <c>1.6</c>)
    /// scopes both paths; when null, all versions/subfolders are considered.
    /// </summary>
    public static ContentFlags Detect(IFileSystem fs, string modRoot, string? activeMajorMinor = null)
    {
        var flags = ContentFlags.None;
        foreach (var dir in ContentDirectories(fs, modRoot, activeMajorMinor))
        {
            flags |= DetectIn(fs, dir);
        }

        // NF-10 · a `.rwlist` payload, at the ROOT only — that is the Workshop-item
        // shape RimManager defines, and version subfolders never carry one. A fact
        // like the others; whether it makes the mod a LIST ITEM is Mod's call,
        // where content wins over payload.
        if (fs.EnumerateEntries(modRoot).Any(e =>
                !e.IsDirectory && e.FullPath.EndsWith(".rwlist", StringComparison.OrdinalIgnoreCase)))
        {
            flags |= ContentFlags.RwList;
        }

        return flags;
    }

    /// <summary>
    /// The absolute directories whose content applies for the active version (root
    /// and/or version subfolders, resolved through <c>LoadFolders.xml</c>). Conflict
    /// analysis uses this to look only where a mod's Defs/Textures actually load.
    /// </summary>
    public static IReadOnlyList<string> LoadedDirectories(IFileSystem fs, string modRoot, string? activeMajorMinor) =>
        ContentDirectories(fs, modRoot, activeMajorMinor).ToList();

    /// <summary>The absolute directories whose content applies for the active version.</summary>
    private static IEnumerable<string> ContentDirectories(IFileSystem fs, string modRoot, string? activeMajorMinor)
    {
        var loadFoldersPath = Path.Combine(modRoot, "LoadFolders.xml");
        if (fs.FileExists(loadFoldersPath))
        {
            var loadFolders = LoadFolders.Parse(fs.ReadAllText(loadFoldersPath));

            var relative =
                activeMajorMinor is not null && loadFolders.HasVersion(activeMajorMinor)
                    ? loadFolders.FoldersFor(activeMajorMinor)
                    : loadFolders.AllFolders();

            if (relative.Length > 0)
            {
                foreach (var rel in relative)
                {
                    yield return ResolveLoadFolder(modRoot, rel);
                }

                yield break; // LoadFolders.xml is authoritative
            }
            // No entry for this version -> fall through to the heuristic below.
        }

        // Heuristic: root plus version-named subfolders.
        yield return modRoot;
        foreach (var versionDir in VersionSubfolders(fs, modRoot, activeMajorMinor))
        {
            yield return versionDir;
        }
    }

    private static string ResolveLoadFolder(string modRoot, string relative)
    {
        var trimmed = relative.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (trimmed is "" or "." or "/" or "\\") return modRoot;
        return Path.Combine(modRoot, trimmed.TrimStart(Path.DirectorySeparatorChar));
    }

    private static IEnumerable<string> VersionSubfolders(IFileSystem fs, string modRoot, string? activeMajorMinor)
    {
        foreach (var entry in fs.EnumerateEntries(modRoot))
        {
            if (!entry.IsDirectory) continue;
            var name = Path.GetFileName(entry.FullPath.TrimEnd('/', '\\'));
            if (!VersionFolderRegex().IsMatch(name)) continue;
            if (activeMajorMinor is not null && !string.Equals(name, activeMajorMinor, StringComparison.Ordinal))
                continue;
            yield return entry.FullPath;
        }
    }

    private static ContentFlags DetectIn(IFileSystem fs, string baseDir)
    {
        var flags = ContentFlags.None;

        if (fs.DirectoryExists(Path.Combine(baseDir, "Defs"))) flags |= ContentFlags.Defs;
        if (fs.DirectoryExists(Path.Combine(baseDir, "Patches"))) flags |= ContentFlags.Patches;
        if (fs.DirectoryExists(Path.Combine(baseDir, "Textures"))) flags |= ContentFlags.Textures;
        if (fs.DirectoryExists(Path.Combine(baseDir, "Sounds"))) flags |= ContentFlags.Sounds;
        if (fs.DirectoryExists(Path.Combine(baseDir, "Languages"))) flags |= ContentFlags.Languages;
        if (fs.DirectoryExists(Path.Combine(baseDir, "Sources"))) flags |= ContentFlags.Sources;

        var assemblies = Path.Combine(baseDir, "Assemblies");
        if (fs.DirectoryExists(assemblies)
            && fs.EnumerateEntries(assemblies).Any(e =>
                !e.IsDirectory && e.FullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            flags |= ContentFlags.Assemblies;
        }

        return flags;
    }
}
