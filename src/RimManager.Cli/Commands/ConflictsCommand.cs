using System.CommandLine;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using RimManager.Storage;
using RimManager.Storage.Analysis;

namespace RimManager.Cli.Commands;

/// <summary>The <c>conflicts</c> command: Tier-2 deep conflict analysis (spec §4.5).</summary>
internal static class ConflictsCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var harmonyOpt = new Option<bool>("--harmony") { Description = "Only Harmony patch collisions." };
        var defsOpt = new Option<bool>("--defs") { Description = "Only Def override collisions." };
        var texturesOpt = new Option<bool>("--textures") { Description = "Only texture path collisions." };
        var patchesOpt = new Option<bool>("--patches") { Description = "Only XML patch collisions." };
        var topOpt = new Option<int>("--top") { Description = "Max conflicts to print per kind.", DefaultValueFactory = _ => 15 };

        var command = new Command("conflicts", "Detect Def / texture / XML-patch / Harmony conflicts in the active list.");
        options.AddTo(command);
        command.Options.Add(harmonyOpt);
        command.Options.Add(defsOpt);
        command.Options.Add(texturesOpt);
        command.Options.Add(patchesOpt);
        command.Options.Add(topOpt);

        command.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var active = (ctx.ModsConfig?.ActiveMods ?? [])
                .Where(id => ctx.Scan.ById.ContainsKey(id))
                .Select(id => ctx.Scan.ById[id])
                .ToList();
            var version = ctx.ModsConfig?.MajorMinor;

            // No flags -> run everything.
            bool all = !(parse.GetValue(harmonyOpt) || parse.GetValue(defsOpt)
                         || parse.GetValue(texturesOpt) || parse.GetValue(patchesOpt));
            var fs = new PhysicalFileSystem();
            var conflicts = new List<ModConflict>();

            if (all || parse.GetValue(defsOpt))
                conflicts.AddRange(DefCollisionAnalyzer.Analyze(active, fs, version));
            if (all || parse.GetValue(patchesOpt))
                conflicts.AddRange(PatchCollisionAnalyzer.Analyze(active, fs, version));
            if (all || parse.GetValue(texturesOpt))
                conflicts.AddRange(TextureCollisionAnalyzer.Analyze(active, fs, version));
            if (all || parse.GetValue(harmonyOpt))
            {
                var managed = FindManagedDir(ctx.Install.GameDir);
                if (managed is null) Console.WriteLine("(Game Managed dir not found — Harmony targets may resolve poorly.)");
                conflicts.AddRange(HarmonyAnalyzer.Analyze(active, fs, version, managed));
            }

            Print(conflicts, parse.GetValue(topOpt));
            return 0;
        });

        return command;
    }

    private static void Print(List<ModConflict> conflicts, int top)
    {
        Console.WriteLine();
        if (conflicts.Count == 0) { Console.WriteLine("No conflicts detected. ✓"); return; }

        foreach (var group in conflicts.GroupBy(c => c.Kind))
        {
            Console.WriteLine($"{group.Key} ({group.Count()}):");
            foreach (var c in group.Take(top))
            {
                var mods = string.Join(", ", c.Mods.Select(m => m.Display));
                Console.WriteLine($"  {c.Key}");
                Console.WriteLine($"    {mods}  →  winner: {c.Winner.Display}" + (c.Detail is null ? "" : $"  [{c.Detail}]"));
            }

            if (group.Count() > top) Console.WriteLine($"    … and {group.Count() - top} more.");
            Console.WriteLine();
        }

        Console.WriteLine($"Total: {conflicts.Count} conflicts.");
    }

    private static string? FindManagedDir(string gameDir)
    {
        string[] candidates =
        [
            Path.Combine("RimWorldWin64_Data", "Managed"),
            Path.Combine("RimWorldLinux_Data", "Managed"),
            Path.Combine("RimWorldMac.app", "Contents", "Resources", "Data", "Managed"),
            Path.Combine("Data", "Managed"),
        ];
        foreach (var rel in candidates)
        {
            var dir = Path.Combine(gameDir, rel);
            if (File.Exists(Path.Combine(dir, "Assembly-CSharp.dll"))) return dir;
        }

        return null;
    }
}
