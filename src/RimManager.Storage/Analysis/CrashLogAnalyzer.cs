using Mono.Cecil;
using RimManager.Core.Abstractions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Storage.Analysis;

/// <summary>
/// Maps a RimWorld crash log's stack frames back to the mods that own them
/// (spec §4.5). Reads each mod's assembly namespaces with Cecil (metadata only,
/// never executed) and ranks suspects by how often their namespaces appear.
/// </summary>
public static class CrashLogAnalyzer
{
    // Base game + common library roots that would otherwise dominate every log.
    private static readonly HashSet<string> VanillaRoots = new(StringComparer.Ordinal)
    {
        "RimWorld", "Verse", "UnityEngine", "Unity", "System", "Mono", "HarmonyLib",
        "Microsoft", "Newtonsoft", "Steamworks", "JetBrains", "Ionic", "ICSharpCode", "TMPro",
    };

    public static CrashReport Analyze(string log, IReadOnlyList<Mod> mods, IFileSystem fs, string? version)
    {
        var modNamespaces = new List<ModNamespace>();

        foreach (var mod in mods)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asmDir in AssemblyDirs(fs, mod, version))
            {
                foreach (var dll in fs.EnumerateEntries(asmDir)
                             .Where(e => !e.IsDirectory && e.FullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    CollectNamespaces(dll.FullPath, roots);
                }
            }

            foreach (var root in roots) modNamespaces.Add(new ModNamespace(root, mod.PackageId, mod.Name));
        }

        return CrashLogRanker.Rank(log, modNamespaces);
    }

    private static void CollectNamespaces(string dllPath, HashSet<string> roots)
    {
        ModuleDefinition module;
        try
        {
            module = ModuleDefinition.ReadModule(dllPath, new ReaderParameters(ReadingMode.Deferred));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentException)
        {
            return;
        }

        using (module)
        {
            foreach (var type in module.Types)
            {
                var root = RootNamespace(type.Namespace);
                if (root.Length > 0 && !VanillaRoots.Contains(root)) roots.Add(root);
            }
        }
    }

    private static string RootNamespace(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return string.Empty;
        var dot = ns.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? ns : ns[..dot];
    }

    private static IEnumerable<string> AssemblyDirs(IFileSystem fs, Mod mod, string? version)
    {
        foreach (var dir in ContentDetector.LoadedDirectories(fs, mod.RootPath, version))
        {
            var asm = Path.Combine(dir, "Assemblies");
            if (fs.DirectoryExists(asm)) yield return asm;
        }
    }
}
