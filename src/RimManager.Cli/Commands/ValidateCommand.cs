using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.ModDatabases;
using RimManager.Core.Validation;
using RimManager.Storage;

namespace RimManager.Cli.Commands;

/// <summary>The <c>validate</c> command: run the Tier-1 checks against the current active list.</summary>
internal static class ValidateCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var rulesOpt = new Option<string?>("--rules") { Description = "Path to a community rules snapshot (communityRules.json)." };

        var command = new Command("validate", "Check the active mod list for problems.");
        options.AddTo(command);
        command.Options.Add(rulesOpt);

        command.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var active = ScanWorkflow.ActiveMods(ctx);
            var known = ctx.ModsConfig?.KnownExpansions ?? [];
            var version = ctx.ModsConfig?.MajorMinor;

            // N7 · the Mlie databases, from the same caches the GUI reads (synced via
            // `replacements sync` / `knowngood sync`) — the GUI and the CLI must see
            // the same inputs, which is the parity rule the rules already follow.
            var knownGood = version is not null && File.Exists(AppPaths.KnownGoodCachePath(version))
                ? NoVersionWarningParser.Parse(File.ReadAllText(AppPaths.KnownGoodCachePath(version)))
                : KnownGoodDatabase.Empty;
            var replacements = File.Exists(AppPaths.ReplacementsCachePath)
                ? UseThisInsteadParser.Parse(File.ReadAllText(AppPaths.ReplacementsCachePath))
                : ReplacementDatabase.Empty;

            var report = new ModListValidator().Validate(
                active, known, version, RulesLoader.Load(parse.GetValue(rulesOpt)),
                inactive: null, knownGood, replacements.Replacements);

            // Missing active mods (in ModsConfig but not installed) are worth calling out too.
            var missing = ctx.ModsConfig is null ? 0
                : ctx.ModsConfig.ActiveMods.Count(id => !ctx.Scan.ById.ContainsKey(id));

            Print(report, missing, ctx.Scan.Warnings);
            return report.ErrorCount > 0 ? 1 : 0;
        });

        return command;
    }

    private static void Print(
        ValidationReport report, int missing,
        System.Collections.Immutable.ImmutableArray<ModWarning> scanWarnings)
    {
        var dupes = scanWarnings.Where(w => w.Code == "duplicate.packageId").ToList();

        if (report.IsClean && missing == 0 && dupes.Count == 0)
        {
            Console.WriteLine("No problems found. ✓");
            return;
        }

        foreach (var group in report.Issues.GroupBy(i => i.Severity).OrderByDescending(g => g.Key))
        {
            Console.WriteLine($"{group.Key}s ({group.Count()}):");
            foreach (var issue in group)
                Console.WriteLine($"  [{issue.Code}] {issue.Message}");
            Console.WriteLine();
        }

        if (missing > 0)
            Console.WriteLine($"{missing} active mod(s) are not installed (see `list`).");
        if (dupes.Count > 0)
        {
            Console.WriteLine($"{dupes.Count} duplicate packageId(s) across sources:");
            foreach (var d in dupes) Console.WriteLine($"  {d.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {report.ErrorCount} error(s), {report.WarningCount} warning(s).");
    }
}
