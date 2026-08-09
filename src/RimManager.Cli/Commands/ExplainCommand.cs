using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>explain</c> command: for one mod, show exactly which rules put it where
/// in the sorted order, with each rule's source and comment (spec §4.4).
/// </summary>
internal static class ExplainCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var rulesOpt = new Option<string?>("--rules") { Description = "Path to a community rules snapshot (communityRules.json)." };
        var packageArg = new Argument<string>("packageId") { Description = "The packageId to explain." };

        var command = new Command("explain", "Explain why a mod sorts where it does.");
        options.AddTo(command);
        command.Options.Add(rulesOpt);
        command.Arguments.Add(packageArg);

        command.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            if (!ModId.TryFrom(parse.GetValue(packageArg), out var target))
            {
                Console.Error.WriteLine("Invalid packageId.");
                return 1;
            }

            var active = ScanWorkflow.ActiveMods(ctx);
            if (!active.Any(m => m.PackageId == target))
            {
                Console.Error.WriteLine($"'{target.Display}' is not in the active mod list.");
                return 1;
            }

            var ruleSet = RuleGraphBuilder.Build(active, RulesLoader.Load(parse.GetValue(rulesOpt)));
            var result = new ModSorter().Sort(active, ruleSet);
            Print(result.Explain(target), ctx.Scan.ById);
            return 0;
        });

        return command;
    }

    private static void Print(ModExplanation ex, System.Collections.Immutable.ImmutableDictionary<ModId, Mod> byId)
    {
        var name = byId.TryGetValue(ex.Id, out var m) ? m.Name : ex.Id.Display;
        Console.WriteLine($"{ex.Id.Display}  —  {name}");
        Console.WriteLine($"  Position : {ex.Position + 1}");
        Console.WriteLine($"  Tier     : {Format.TierTag(ex.Tier)}");

        Console.WriteLine();
        if (ex.LoadsAfter.IsDefaultOrEmpty)
            Console.WriteLine("  Loads after: (nothing — no rules place another mod before it)");
        else
        {
            Console.WriteLine("  Loads after:");
            foreach (var link in ex.LoadsAfter)
                Console.WriteLine($"    ← {link.Other.Display}   [{Format.RuleText(link.Provenance)}]");
        }

        Console.WriteLine();
        if (ex.LoadsBefore.IsDefaultOrEmpty)
            Console.WriteLine("  Loads before: (nothing)");
        else
        {
            Console.WriteLine("  Loads before:");
            foreach (var link in ex.LoadsBefore)
                Console.WriteLine($"    → {link.Other.Display}   [{Format.RuleText(link.Provenance)}]");
        }

        if (!ex.IgnoredForTier.IsDefaultOrEmpty)
        {
            Console.WriteLine();
            Console.WriteLine("  Ignored (would violate hard tiering):");
            foreach (var e in ex.IgnoredForTier)
                Console.WriteLine($"    · {e.Before.Display} → {e.After.Display}  [{Format.RuleText(e.Provenance)}]");
        }
    }
}
