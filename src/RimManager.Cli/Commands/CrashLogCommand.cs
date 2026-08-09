using System.CommandLine;
using RimManager.Storage;
using RimManager.Storage.Analysis;

namespace RimManager.Cli.Commands;

/// <summary>The <c>crashlog</c> command: attribute a RimWorld crash log to suspect mods (spec §4.5).</summary>
internal static class CrashLogCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var fileArg = new Argument<string>("logFile") { Description = "A RimWorld log file (or crash paste) to analyze." };
        var topOpt = new Option<int>("--top") { Description = "Max suspects to print.", DefaultValueFactory = _ => 15 };

        var command = new Command("crashlog", "Map a crash log's stack frames back to suspect mods.");
        options.AddTo(command);
        command.Arguments.Add(fileArg);
        command.Options.Add(topOpt);

        command.SetAction(parse =>
        {
            var path = parse.GetValue(fileArg)!;
            if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var active = (ctx.ModsConfig?.ActiveMods ?? [])
                .Where(id => ctx.Scan.ById.ContainsKey(id))
                .Select(id => ctx.Scan.ById[id])
                .ToList();

            var report = CrashLogAnalyzer.Analyze(
                File.ReadAllText(path), active, new PhysicalFileSystem(), ctx.ModsConfig?.MajorMinor);

            Console.WriteLine();
            if (report.Suspects.Length == 0)
            {
                Console.WriteLine("No active-mod namespaces found in the log.");
                return 0;
            }

            Console.WriteLine($"Suspect mods (by stack-frame references), most-referenced first:");
            foreach (var suspect in report.Suspects.Take(parse.GetValue(topOpt)))
            {
                Console.WriteLine($"  {suspect.Hits,4}×  {suspect.PackageId.Display}  —  {suspect.DisplayName}  ({string.Join(", ", suspect.Namespaces)})");
            }

            return 0;
        });

        return command;
    }
}
