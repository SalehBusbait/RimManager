using Mono.Cecil;
using RimManager.Core.Abstractions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Storage.Analysis;

/// <summary>
/// Detects mods that Harmony-patch the same method (spec §4.5, Tier 2 — the
/// differentiator). Reads <c>Assemblies/*.dll</c> with Mono.Cecil in read-only
/// deferred mode; mod code is never executed and never <c>Assembly.Load</c>ed.
/// </summary>
/// <remarks>
/// The Phase 0 spike established what's needed to make this work: reading Harmony
/// attribute arguments forces Cecil to resolve enum types (<c>MethodType</c>) from
/// <c>0Harmony.dll</c> / <c>Assembly-CSharp.dll</c>, so the resolver must see the
/// game's <c>Managed</c> dir plus each mod's <c>Assemblies</c> dir — and a resolve
/// failure on one class must be skipped, never abort the scan.
/// </remarks>
public static class HarmonyAnalyzer
{
    /// <param name="onModDone">Ticked once per mod, for the load state's fraction.</param>
    public static IReadOnlyList<ModConflict> Analyze(
        IReadOnlyList<Mod> orderedActive, IFileSystem fs, string? version, string? gameManagedDir,
        Action<Mod>? onModDone = null)
    {
        var byMethod = new Dictionary<string, List<(ModId Mod, string Kind)>>(StringComparer.Ordinal);

        var resolver = new DefaultAssemblyResolver();
        if (!string.IsNullOrEmpty(gameManagedDir) && Directory.Exists(gameManagedDir))
            resolver.AddSearchDirectory(gameManagedDir);

        foreach (var mod in orderedActive)
        {
            foreach (var asmDir in AssemblyDirs(fs, mod, version))
            {
                resolver.AddSearchDirectory(asmDir);
                foreach (var dll in fs.EnumerateEntries(asmDir)
                             .Where(e => !e.IsDirectory && e.FullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    InspectDll(dll.FullPath, resolver, mod.PackageId, byMethod);
                }
            }

            onModDone?.Invoke(mod);
        }

        var conflicts = new List<ModConflict>();
        foreach (var (method, entries) in byMethod)
        {
            var mods = new List<ModId>();
            foreach (var e in entries)
                if (!mods.Contains(e.Mod)) mods.Add(e.Mod);
            if (mods.Count < 2) continue;

            var kinds = string.Join(", ", entries.Select(e => e.Kind).Distinct(StringComparer.Ordinal));
            var transpiler = entries.Any(e => e.Kind.Contains("transpiler", StringComparison.Ordinal));
            var detail = (transpiler ? "⚠ transpiler present — " : "") + $"{kinds}; all patches apply";
            conflicts.Add(new ModConflict(ConflictKind.HarmonyPatch, method, [.. mods], mods[^1], detail));
        }

        return conflicts
            .OrderByDescending(c => c.Mods.Length)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> AssemblyDirs(IFileSystem fs, Mod mod, string? version)
    {
        foreach (var dir in ContentDetector.LoadedDirectories(fs, mod.RootPath, version))
        {
            var asm = Path.Combine(dir, "Assemblies");
            if (fs.DirectoryExists(asm)) yield return asm;
        }
    }

    private static void InspectDll(
        string path, IAssemblyResolver resolver, ModId mod, Dictionary<string, List<(ModId, string)>> byMethod)
    {
        ModuleDefinition module;
        try
        {
            module = ModuleDefinition.ReadModule(path,
                new ReaderParameters(ReadingMode.Deferred) { AssemblyResolver = resolver });
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentException)
        {
            return; // not a managed/readable assembly — skip
        }

        using (module)
        {
            foreach (var type in AllTypes(module))
            {
                try
                {
                    if (ResolveTarget(type) is not { } target) continue;
                    var kind = PatchKind(type);
                    if (!byMethod.TryGetValue(target, out var list)) byMethod[target] = list = [];
                    list.Add((mod, kind));
                }
                catch (Exception ex) when (ex is AssemblyResolutionException or ArgumentException)
                {
                    // One class we couldn't resolve — skip it, keep scanning.
                }
            }
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            yield return type;
            foreach (var nested in type.NestedTypes) yield return nested;
        }
    }

    /// <summary>Resolves the fully-qualified target method a Harmony patch class targets, or null.</summary>
    private static string? ResolveTarget(TypeDefinition type)
    {
        var attrs = type.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToList();
        if (attrs.Count == 0) return null;

        string? declaringType = null;
        string? methodName = null;
        bool constructor = false;

        foreach (var attr in attrs)
        {
            if (!attr.HasConstructorArguments) continue;
            foreach (var arg in attr.ConstructorArguments)
            {
                if (arg.Value is TypeReference tr) declaringType = tr.FullName;
                else if (arg.Value is string s && methodName is null) methodName = s;
                else if (arg.Type.Name == "MethodType" && arg.Value is int mt && mt == 3) constructor = true;
            }
        }

        if (declaringType is null) return null;
        if (methodName is not null) return $"{declaringType}::{methodName}";
        if (constructor) return $"{declaringType}::.ctor";
        return null; // typeof-only / dynamic TargetMethod — not statically resolvable
    }

    private static string PatchKind(TypeDefinition type)
    {
        bool Has(string method, string attribute) => type.Methods.Any(m =>
            m.Name == method || m.CustomAttributes.Any(a => a.AttributeType.Name == attribute));

        if (Has("Transpiler", "HarmonyTranspiler")) return "transpiler";
        bool prefix = Has("Prefix", "HarmonyPrefix");
        bool postfix = Has("Postfix", "HarmonyPostfix");
        return (prefix, postfix) switch
        {
            (true, true) => "prefix+postfix",
            (false, true) => "postfix",
            (true, false) => "prefix",
            _ => "patch",
        };
    }
}
