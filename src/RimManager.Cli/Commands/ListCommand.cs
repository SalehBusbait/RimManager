using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Scanning;

namespace RimManager.Cli.Commands;

/// <summary>The <c>list</c> command: scan and print the mod list.</summary>
internal static class ListCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var allOpt = new Option<bool>("--all") { Description = "List every installed mod instead of just the active load order." };

        var command = new Command("list", "List installed or active mods.");
        options.AddTo(command);
        command.Options.Add(allOpt);

        command.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            if (parse.GetValue(allOpt) || ctx.ModsConfig is null) PrintAll(ctx.Scan);
            else PrintActive(ctx.Scan, ctx.ModsConfig);

            PrintWarnings(ctx.Scan);
            return 0;
        });

        return command;
    }

    private static void PrintActive(ScanResult result, ModsConfig config)
    {
        Console.WriteLine($"Active load order ({config.ActiveMods.Length}):");
        int index = 1, missing = 0;
        foreach (var id in config.ActiveMods)
        {
            if (result.ById.TryGetValue(id, out var mod))
                Console.WriteLine($"  {index,4}  {Format.Flags(mod)}  {Format.SourceTag(mod.Source)}  {id.Display}  —  {mod.Name}");
            else
            {
                missing++;
                Console.WriteLine($"  {index,4}  {"·· ",-6}  {"---",-4}  {id.Display}  —  (not installed)");
            }

            index++;
        }

        var activeSet = config.ActiveMods.ToHashSet();
        var inactive = result.Mods.Count(m => !activeSet.Contains(m.PackageId));
        Console.WriteLine();
        Console.WriteLine($"{missing} active mods not installed · {inactive} installed but inactive.");
    }

    private static void PrintAll(ScanResult result)
    {
        Console.WriteLine($"All installed mods ({result.Mods.Length}):");
        foreach (var mod in result.Mods.OrderBy(m => m.PackageId.Value, StringComparer.Ordinal))
            Console.WriteLine($"  {Format.Flags(mod)}  {Format.SourceTag(mod.Source)}  {mod.PackageId.Display}  —  {mod.Name}");
    }

    private static void PrintWarnings(ScanResult result)
    {
        var notable = result.Warnings.Where(w => w.Severity != WarningSeverity.Info).ToList();
        var modErrors = result.Mods.Where(m => m.HasErrors).ToList();
        if (notable.Count == 0 && modErrors.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (var w in notable) Console.WriteLine($"  [{w.Severity}] {w.Code}: {w.Message}");
        foreach (var m in modErrors)
            foreach (var w in m.Warnings.Where(x => x.Severity == WarningSeverity.Error))
                Console.WriteLine($"  [{w.Severity}] {m.PackageId.Display}: {w.Message}");
    }
}
