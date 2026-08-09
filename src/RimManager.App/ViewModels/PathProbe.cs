using System.IO;
using RimManager.Core.Abstractions;

namespace RimManager.App.ViewModels;

/// <summary>How a configured path came back when we looked at it.</summary>
public enum PathVerdict
{
    /// <summary>Nothing entered, and nothing needs to be — an optional path.</summary>
    NotSet,

    Ok,

    /// <summary>Present, but something about it will limit what the app can do.</summary>
    Warning,

    /// <summary>Absent or unusable. A required path in this state blocks Save.</summary>
    Missing,
}

/// <summary>
/// What a path check found, in the words <c>1c</c> uses. <paramref name="Action"/> is
/// the inline link offered alongside a problem ("Locate…"), or null.
/// </summary>
public sealed record PathCheck(PathVerdict Verdict, string Message, string? Action = null)
{
    public bool IsOk => Verdict == PathVerdict.Ok;
    public bool IsMissing => Verdict == PathVerdict.Missing;
    public bool IsWarning => Verdict == PathVerdict.Warning;
    public bool IsNotSet => Verdict == PathVerdict.NotSet;
    public bool HasAction => Action is not null;
}

/// <summary>
/// Per-field path validation for Settings ▸ Paths (<c>1c</c>): "RimManager validates
/// each path and shows what it found."
/// <para>
/// Every check reports <b>what it found</b>, not merely whether the folder exists —
/// a path that exists but holds the wrong thing is the failure mode that wastes an
/// afternoon, and "✓ folder exists" would hide it.
/// </para>
/// <para>
/// Pure over <see cref="IFileSystem"/>, so the wording and the verdicts are testable.
/// A validator that quietly said "ok" for an empty folder is exactly the kind of
/// mistake no screenshot catches.
/// </para>
/// </summary>
public static class PathProbe
{
    /// <summary>The DLC folders RimWorld ships, in release order.</summary>
    private static readonly string[] Dlc =
        ["Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey"];

    public static PathCheck Game(IFileSystem fs, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(PathVerdict.Missing, "Required — RimManager cannot scan without it.");

        if (!fs.DirectoryExists(path))
            return new PathCheck(PathVerdict.Missing, "Folder not found.", "Auto-detect");

        // Data/Core is what makes a folder a RimWorld install rather than a folder
        // that happens to be named RimWorld.
        var data = Path.Combine(path, "Data");
        if (!fs.DirectoryExists(Path.Combine(data, "Core")))
        {
            return new PathCheck(PathVerdict.Missing,
                "No Data/Core here — this is not a RimWorld install.", "Auto-detect");
        }

        var found = Dlc.Where(d => fs.DirectoryExists(Path.Combine(data, d))).ToArray();
        var version = ReadVersion(fs, path);

        var what = version is null ? "RimWorld" : $"RimWorld {version}";
        return new PathCheck(PathVerdict.Ok, found.Length == 0
            ? $"{what} · no DLC found"
            : $"{what} · {found.Length} DLC found ({string.Join(", ", found)})");
    }

    public static PathCheck Config(IFileSystem fs, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathCheck(PathVerdict.Missing,
                "Required — this is where ModsConfig.xml lives.", "Auto-detect");
        }

        if (!fs.DirectoryExists(path))
            return new PathCheck(PathVerdict.Missing, "Folder not found.", "Auto-detect");

        // An empty config folder is normal before RimWorld has ever run, so it is a
        // warning rather than an error — but it must not read as a clean bill.
        return fs.FileExists(Path.Combine(path, "ModsConfig.xml"))
            ? new PathCheck(PathVerdict.Ok, "ModsConfig.xml writable · backup on every Apply")
            : new PathCheck(PathVerdict.Warning,
                "No ModsConfig.xml yet — one will be written on the first Apply.");
    }

    /// <param name="trackedByGit">
    /// How many of them are git working trees. <c>1c</c> reports this on the local-mods
    /// line ("14 local mods · 2 tracked by git") because local mods are the only place a
    /// clone can be — the Workshop folder is Steam's, and a `.git` there is upload
    /// residue rather than a repository.
    /// </param>
    public static PathCheck LocalMods(IFileSystem fs, string? path, int trackedByGit = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(PathVerdict.NotSet, "Optional — defaults to <game>/Mods.");

        if (!fs.DirectoryExists(path))
            return new PathCheck(PathVerdict.Missing, "Folder not found.");

        var mods = CountMods(fs, path);
        if (mods == 0) return new PathCheck(PathVerdict.Warning, "No mods here — the folder is empty.");

        // Stated only when there is something to state: a permanent "· 0 tracked by git"
        // is noise on the installs that have never cloned anything, which is most.
        var git = trackedByGit > 0 ? $" · {trackedByGit} tracked by git" : string.Empty;
        return new PathCheck(PathVerdict.Ok, $"{mods} local mod{(mods == 1 ? "" : "s")}{git}");
    }

    public static PathCheck Workshop(IFileSystem fs, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathCheck(PathVerdict.NotSet,
                "Optional — set it to list your Steam Workshop mods.");
        }

        if (!fs.DirectoryExists(path))
        {
            return new PathCheck(PathVerdict.Missing,
                "Folder not found — Workshop mods will not be listed.", "Locate…");
        }

        // "installed", not "subscribed". This counts folders holding an About.xml; a
        // subscription with nothing downloaded is invisible to it, and reading the
        // account's subscriptions would need a logged-in Web API call we do not make.
        // Settings ▸ Integrations reports the same fact from Steam's own manifest, and
        // the two must not use different words for it.
        var mods = CountMods(fs, path);
        return mods == 0
            ? new PathCheck(PathVerdict.Warning, "No Workshop mods installed in this folder.")
            : new PathCheck(PathVerdict.Ok,
                $"{mods} Workshop mod{(mods == 1 ? "" : "s")} installed");
    }

    /// <summary>
    /// SteamCMD's own folder. No inline action: <c>1c</c> puts <b>Install for me</b> in
    /// the field row as a real button beside Browse, which is where it can actually do
    /// the install — an inline link in the verdict line had nowhere to report progress.
    /// </summary>
    public static PathCheck SteamCmd(IFileSystem fs, string? path)
    {
        // Nothing to report: the field's own label already says it is optional and what
        // it is for, and 1c leaves this line empty. A verdict line that restates the
        // label teaches the reader that these lines are decoration.
        if (string.IsNullOrWhiteSpace(path))
            return new PathCheck(PathVerdict.NotSet, string.Empty);

        return fs.DirectoryExists(path)
            ? new PathCheck(PathVerdict.Ok, "SteamCMD found · anonymous downloads, no login")
            : new PathCheck(PathVerdict.Missing, "Folder not found.");
    }

    /// <summary>
    /// A mod folder is one with an About/About.xml. Counting folders would count the
    /// stray archives and half-extracted downloads that live alongside them.
    /// </summary>
    private static int CountMods(IFileSystem fs, string root)
    {
        try
        {
            return fs.EnumerateEntries(root)
                .Where(e => e.IsDirectory)
                .Count(e => fs.FileExists(Path.Combine(e.FullPath, "About", "About.xml")));
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static string? ReadVersion(IFileSystem fs, string gameDir)
    {
        var file = Path.Combine(gameDir, "Version.txt");
        if (!fs.FileExists(file)) return null;

        try
        {
            var text = fs.ReadAllText(file).Trim();
            // RimWorld writes "1.6.4871 rev590"; the revision is noise here.
            var space = text.IndexOf(' ');
            return space > 0 ? text[..space] : text;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
