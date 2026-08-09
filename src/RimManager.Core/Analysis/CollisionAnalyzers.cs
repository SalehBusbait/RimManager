using System.Xml.Linq;
using RimManager.Core.Abstractions;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Core.Analysis;

/// <summary>Shared collision detection: group a per-mod key stream by key, flag any key ≥ 2 mods claim.</summary>
internal static class Collisions
{
    public static IEnumerable<ModConflict> Detect(
        ConflictKind kind, IReadOnlyList<Mod> orderedActive, Func<Mod, IEnumerable<string>> keysOf,
        Action<Mod>? onModDone = null) =>
        Detect(kind, orderedActive, mod => keysOf(mod).Select(k => new Claim(k)), onModDone);

    /// <summary>
    /// One mod's claim on a key, optionally carrying where it came from and the
    /// markup itself — the raw material for the two-up XML diff (<c>3c</c>).
    /// </summary>
    public readonly record struct Claim(string Key, string? SourceFile = null, string? Xml = null);

    /// <param name="onModDone">
    /// Ticked once per mod, so the load state can show a real fraction rather than a moving
    /// stripe. Every cheap analyzer funnels through this loop, so one hook serves all three.
    /// </param>
    public static IEnumerable<ModConflict> Detect(
        ConflictKind kind, IReadOnlyList<Mod> orderedActive, Func<Mod, IEnumerable<Claim>> claimsOf,
        Action<Mod>? onModDone = null)
    {
        var byKey = new Dictionary<string, List<ConflictProvider>>(StringComparer.Ordinal);

        foreach (var mod in orderedActive)
        {
            // DistinctBy on the key: a mod defining the same def twice in one folder
            // is its own problem, not a conflict with anybody else.
            foreach (var claim in claimsOf(mod).DistinctBy(c => c.Key, StringComparer.Ordinal))
            {
                if (!byKey.TryGetValue(claim.Key, out var list)) byKey[claim.Key] = list = [];
                list.Add(new ConflictProvider(mod.PackageId, claim.SourceFile, claim.Xml));
            }

            onModDone?.Invoke(mod);
        }

        return byKey
            .Where(kv => kv.Value.Count >= 2)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ModConflict(
                kind,
                kv.Key,
                [.. kv.Value.Select(p => p.ModId)],
                kv.Value[^1].ModId,
                Detail: null,
                Providers: [.. kv.Value]));
    }

    public static IEnumerable<FileEntry> XmlFilesUnder(IFileSystem fs, string modRoot, string subfolder, string? version)
    {
        foreach (var dir in ContentDetector.LoadedDirectories(fs, modRoot, version))
        {
            var target = Path.Combine(dir, subfolder);
            foreach (var file in fs.EnumerateFilesRecursive(target)
                         .Where(e => e.FullPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                yield return file;
            }
        }
    }
}

/// <summary>Flags mods that define the same <c>(DefType, defName)</c> — the later one overrides.</summary>
public static class DefCollisionAnalyzer
{
    public static IEnumerable<ModConflict> Analyze(
        IReadOnlyList<Mod> orderedActive, IFileSystem fs, string? version, Action<Mod>? onModDone = null) =>
        Collisions.Detect(ConflictKind.DefOverride, orderedActive, mod => Claims(mod, fs, version), onModDone);

    private static IEnumerable<Collisions.Claim> Claims(Mod mod, IFileSystem fs, string? version)
    {
        foreach (var file in Collisions.XmlFilesUnder(fs, mod.RootPath, "Defs", version))
        {
            XDocument doc;
            try { doc = XDocument.Parse(fs.ReadAllText(file.FullPath)); }
            catch (System.Xml.XmlException) { continue; }
            catch (IOException) { continue; }

            foreach (var def in doc.Root?.Elements() ?? [])
            {
                var name = def.Element("defName")?.Value.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                // Retain the element itself, not the file: a Defs file runs to
                // thousands of lines and only this element is contested (3c).
                yield return new Collisions.Claim(
                    $"{def.Name.LocalName}/{name}", file.FullPath, def.ToString());
            }
        }
    }
}

/// <summary>Flags mods shipping the same relative texture path — the later one wins.</summary>
public static class TextureCollisionAnalyzer
{
    public static IEnumerable<ModConflict> Analyze(
        IReadOnlyList<Mod> orderedActive, IFileSystem fs, string? version, Action<Mod>? onModDone = null) =>
        Collisions.Detect(ConflictKind.TextureCollision, orderedActive, mod => Keys(mod, fs, version), onModDone);

    private static IEnumerable<string> Keys(Mod mod, IFileSystem fs, string? version)
    {
        foreach (var dir in ContentDetector.LoadedDirectories(fs, mod.RootPath, version))
        {
            var texDir = Path.Combine(dir, "Textures");
            var prefixLen = texDir.Length;
            foreach (var file in fs.EnumerateFilesRecursive(texDir))
            {
                var rel = file.FullPath.Length > prefixLen ? file.FullPath[prefixLen..] : file.FullPath;
                yield return rel.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            }
        }
    }
}

/// <summary>Flags mods whose <c>Patches</c> target the same XML node (same PatchOperation xpath).</summary>
public static class PatchCollisionAnalyzer
{
    public static IEnumerable<ModConflict> Analyze(
        IReadOnlyList<Mod> orderedActive, IFileSystem fs, string? version, Action<Mod>? onModDone = null) =>
        Collisions.Detect(ConflictKind.PatchCollision, orderedActive, mod => Keys(mod, fs, version), onModDone);

    private static IEnumerable<string> Keys(Mod mod, IFileSystem fs, string? version)
    {
        foreach (var file in Collisions.XmlFilesUnder(fs, mod.RootPath, "Patches", version))
        {
            XDocument doc;
            try { doc = XDocument.Parse(fs.ReadAllText(file.FullPath)); }
            catch (System.Xml.XmlException) { continue; }
            catch (IOException) { continue; }

            foreach (var xpath in doc.Descendants("xpath"))
            {
                var value = xpath.Value.Trim();
                if (value.Length > 0) yield return value;
            }
        }
    }
}
