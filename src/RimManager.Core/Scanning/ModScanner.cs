using System.Collections.Immutable;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Parsing;

namespace RimManager.Core.Scanning;

/// <summary>
/// Scans one or more <see cref="ModSourceRoot"/>s into a deduped
/// <see cref="ScanResult"/>. Pure with respect to the outside world — all I/O
/// goes through <see cref="IFileSystem"/>, all caching through
/// <see cref="IModCache"/> — so it is fully testable in memory.
/// </summary>
public sealed class ModScanner
{
    private readonly IFileSystem _fs;
    private readonly IModCache _cache;

    public ModScanner(IFileSystem fs, IModCache? cache = null)
    {
        _fs = fs;
        _cache = cache ?? NullModCache.Instance;
    }

    /// <summary>
    /// Scans <paramref name="roots"/>. <paramref name="activeMajorMinor"/> (e.g.
    /// <c>1.6</c>) scopes per-version content detection when known.
    /// </summary>
    /// <param name="progress">
    /// Optional per-folder progress for the first-scan state (<c>2k</c>). Every root is
    /// enumerated before the first folder is read so the total is right from the first
    /// report; the cost is one directory listing that the scan was going to do anyway.
    /// </param>
    public ScanResult Scan(
        IEnumerable<ModSourceRoot> roots,
        string? activeMajorMinor = null,
        IProgress<ScanProgress>? progress = null)
    {
        var found = new List<Mod>();
        var warnings = ImmutableArray.CreateBuilder<ModWarning>();

        var work = new List<(ModSourceRoot Root, string[] Folders)>();
        foreach (var root in roots)
        {
            if (!_fs.DirectoryExists(root.Path))
            {
                continue;
            }

            work.Add((root, [.. _fs.EnumerateEntries(root.Path).Where(e => e.IsDirectory).Select(e => e.FullPath)]));
        }

        var total = work.Sum(w => w.Folders.Length);
        var done = 0;
        progress?.Report(new ScanProgress(0, total, work.Count == 0 ? string.Empty : work[0].Root.Path));

        foreach (var (root, folders) in work)
        {
            foreach (var folder in folders)
            {
                var mod = ScanModFolder(folder, root.Source, activeMajorMinor, warnings);
                if (mod is not null) found.Add(mod);

                done++;
                // Every 8th folder, plus the last of every root. A report per folder is
                // ~550 marshalled callbacks on a real install to move a bar by 0.2%.
                if (progress is not null && (done % 8 == 0 || done == total))
                    progress.Report(new ScanProgress(done, total, root.Path));
            }
        }

        var deduped = Dedupe(found, warnings);
        _cache.Flush();
        return new ScanResult(deduped, warnings.ToImmutable());
    }

    private Mod? ScanModFolder(
        string modRoot, ModSource source, string? activeMajorMinor,
        ImmutableArray<ModWarning>.Builder scanWarnings)
    {
        var aboutPath = FindAboutXml(modRoot);
        if (aboutPath is null)
        {
            // A folder with no About.xml isn't a mod (could be a stray dir). Note it, move on.
            scanWarnings.Add(new ModWarning(WarningSeverity.Info, "scan.no-about",
                $"Folder has no About/About.xml: {modRoot}"));
            return null;
        }

        var stat = _fs.Stat(aboutPath);
        if (stat is { } s && _cache.TryGet(aboutPath, s) is { } cached)
        {
            return cached;
        }

        AboutMetadata meta;
        try
        {
            meta = AboutXmlParser.Parse(_fs.ReadAllText(aboutPath));
        }
        catch (IOException ex)
        {
            scanWarnings.Add(new ModWarning(WarningSeverity.Error, "scan.read-failed",
                $"Could not read {aboutPath}: {ex.Message}"));
            return null;
        }

        // A missing packageId is surfaced by the parser; fall back to the folder
        // name so the broken mod still appears rather than vanishing.
        var folderName = Path.GetFileName(modRoot.TrimEnd('/', '\\'));
        var packageId = ModId.From(meta.PackageId ?? $"unknown.{folderName}");

        var content = ContentDetector.Detect(_fs, modRoot, activeMajorMinor);
        var publishedFileId = ReadPublishedFileId(aboutPath);
        var refinedSource = RefineSource(source, packageId, modRoot);

        var mod = new Mod
        {
            PackageId = packageId,
            // Ludeon's own About.xml files carry no <name> at all, so the base game
            // and every expansion would otherwise render as their packageId — six
            // rows of "Ludeon.RimWorld.Anomaly" anchoring every load order. Only
            // Ludeon's ids resolve here; everything else falls through as before.
            Name = meta.Name ?? KnownMods.DisplayName(packageId) ?? packageId.Display,
            Authors = meta.Authors,
            Description = meta.Description,
            SupportedVersions = meta.SupportedVersions,
            ModVersion = meta.ModVersion,
            Dependencies = meta.Dependencies,
            LoadAfter = ToIds(meta.LoadAfter),
            LoadBefore = ToIds(meta.LoadBefore),
            ForceLoadAfter = ToIds(meta.ForceLoadAfter),
            ForceLoadBefore = ToIds(meta.ForceLoadBefore),
            IncompatibleWith = ToIds(meta.IncompatibleWith),
            Source = refinedSource,
            RootPath = modRoot,
            PublishedFileId = publishedFileId,
            Content = content,
            Warnings = meta.Warnings,
            AboutLastWriteUtc = stat?.LastWriteUtc ?? default,
            AboutSize = stat?.Size ?? 0,
        };

        if (stat is { } st)
        {
            _cache.Put(aboutPath, st, mod);
        }

        return mod;
    }

    /// <summary>
    /// A root's tag is a starting point, not the answer. Two refinements:
    /// <list type="bullet">
    ///   <item>a <c>Core</c>-tagged root is Core for the base game and Dlc otherwise;</item>
    ///   <item>a <c>Local</c> mod that is a git clone is <see cref="ModSource.Git"/>.</item>
    /// </list>
    /// <para>
    /// The second exists because <c>ModSource.Git</c> had <b>no producer at all</b>: the
    /// scanner is handed one root per source, and there is no "git root" — a clone lives
    /// in <c>&lt;game&gt;/Mods</c> in exactly the same place a hand-copied folder does.
    /// Git-ness is a property of the folder, so it can only be decided here. Every
    /// consumer already existed — the precedence table, the row badge, the source
    /// filter, the <c>.rwlist</c> mapping — reading a value nothing ever wrote.
    /// </para>
    /// <para>
    /// <b>Local roots only</b>, and this restriction is load-bearing rather than
    /// cautious. A <c>.git</c> inside a Workshop folder is upload residue from the
    /// author's own machine: 33 of a real 405-mod Workshop library have one, none of
    /// them a repository the user owns. The vault is excluded for the same reason — it
    /// holds copies RimManager itself made. This is the same rule <c>GitService</c>
    /// already applies before it will run a git command.
    /// </para>
    /// <para>
    /// A <c>.git</c> DIRECTORY, never a file. A file is a worktree or submodule pointer,
    /// and the one time this project met one it pointed at <c>C:/gits/HFM.git</c> on
    /// someone else's disk. Measured on a real 55-folder Mods directory: 39 <c>.git</c>
    /// directories, zero <c>.git</c> files.
    /// </para>
    /// </summary>
    private ModSource RefineSource(ModSource source, ModId packageId, string modRoot)
    {
        if (source == ModSource.Core)
            return packageId.Value == "ludeon.rimworld" ? ModSource.Core : ModSource.Dlc;

        // One cheap directory probe per local mod — tens of folders, not hundreds, and
        // no process is launched. Reading git STATE is GitService's job and still costs
        // what it costs; this only decides which folders are worth asking about.
        if (source == ModSource.Local && _fs.DirectoryExists(Path.Combine(modRoot, ".git")))
            return ModSource.Git;

        return source;
    }

    private static ImmutableArray<ModId> ToIds(ImmutableArray<string> raw)
    {
        if (raw.IsDefaultOrEmpty) return [];
        var ids = ImmutableArray.CreateBuilder<ModId>(raw.Length);
        foreach (var s in raw)
        {
            if (ModId.TryFrom(s, out var id)) ids.Add(id);
        }

        return ids.ToImmutable();
    }

    private string? ReadPublishedFileId(string aboutPath)
    {
        var dir = Path.GetDirectoryName(aboutPath);
        if (dir is null) return null;
        var pfid = Path.Combine(dir, "PublishedFileId.txt");
        if (!_fs.FileExists(pfid)) return null;
        try
        {
            var text = _fs.ReadAllText(pfid).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Finds <c>About/About.xml</c>, tolerating case differences on case-sensitive filesystems.</summary>
    private string? FindAboutXml(string modRoot)
    {
        var direct = Path.Combine(modRoot, "About", "About.xml");
        if (_fs.FileExists(direct)) return direct;

        // Case-insensitive fallback (Linux): find the About dir, then About.xml within.
        var aboutDir = _fs.EnumerateEntries(modRoot).FirstOrDefault(e =>
            e.IsDirectory &&
            string.Equals(Path.GetFileName(e.FullPath.TrimEnd('/', '\\')), "About", StringComparison.OrdinalIgnoreCase));
        if (aboutDir.FullPath is null) return null;

        var aboutFile = _fs.EnumerateEntries(aboutDir.FullPath).FirstOrDefault(e =>
            !e.IsDirectory &&
            string.Equals(Path.GetFileName(e.FullPath), "About.xml", StringComparison.OrdinalIgnoreCase));
        return aboutFile.FullPath;
    }

    /// <summary>
    /// Collapses duplicate packageIds (e.g. the same mod in Workshop and Local),
    /// keeping the highest-precedence source and warning about the collision so the
    /// user can override later (spec §3).
    /// </summary>
    private static ImmutableArray<Mod> Dedupe(List<Mod> mods, ImmutableArray<ModWarning>.Builder warnings)
    {
        var byId = new Dictionary<ModId, Mod>();
        foreach (var mod in mods)
        {
            if (!byId.TryGetValue(mod.PackageId, out var existing))
            {
                byId[mod.PackageId] = mod;
                continue;
            }

            var winner = Precedence(mod.Source) >= Precedence(existing.Source) ? mod : existing;
            var loser = ReferenceEquals(winner, mod) ? existing : mod;
            byId[mod.PackageId] = winner;

            warnings.Add(new ModWarning(WarningSeverity.Warning, "duplicate.packageId",
                $"Duplicate packageId '{mod.PackageId.Display}': using {winner.Source} " +
                $"({winner.RootPath}), ignoring {loser.Source} ({loser.RootPath}).",
                mod.PackageId));
        }

        return [.. byId.Values];
    }

    private static int Precedence(ModSource source) => source switch
    {
        ModSource.Local => 4,
        ModSource.Git => 3,
        ModSource.Workshop => 2,
        ModSource.Dlc => 1,
        ModSource.Core => 1,
        _ => 0,
    };
}
