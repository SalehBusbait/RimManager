using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>sort</c> command: compute the proposed load order and show it as a diff
/// against the current order (preview only — writing is Phase 3's <c>apply</c>).
/// </summary>
internal static class SortCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var rulesOpt = new Option<string?>("--rules") { Description = "Path to a community rules snapshot (communityRules.json)." };
        var fullOpt = new Option<bool>("--full") { Description = "Print the entire proposed order, not just the changes." };
        var suppressOpt = new Option<string[]>("--suppress")
        {
            Description = "Drop an ordering edge, as before:after (repeatable). "
                        + "This is the CLI twin of \"Drop a different edge\" in the Warnings panel.",
            AllowMultipleArgumentsPerToken = true,
        };

        var command = new Command("sort", "Compute the sorted load order (preview).");
        options.AddTo(command);
        command.Options.Add(rulesOpt);
        command.Options.Add(fullOpt);
        command.Options.Add(suppressOpt);

        command.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var active = ScanWorkflow.ActiveMods(ctx);
            var rules = RulesLoader.Load(parse.GetValue(rulesOpt));
            var ruleSet = RuleGraphBuilder.Build(active, rules);

            if (!TryParseSuppressions(parse.GetValue(suppressOpt), out var suppressions)) return 1;
            var result = new ModSorter().Sort(active, ruleSet, suppressions);

            var current = active.Select(m => m.PackageId).ToList();
            var moved = OrderDiff.Print(current, result.Order, ctx.Scan.ById, result.Tiers, parse.GetValue(fullOpt));
            Console.WriteLine();
            Console.WriteLine(moved == 0
                ? "Already sorted — no changes."
                : $"{moved} of {result.Order.Length} mods would move." + (parse.GetValue(fullOpt) ? "" : "  (use --full to see all.)"));

            PrintCyclesAndDrops(result);
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Parses <c>--suppress before:after</c> into an <see cref="EdgeSuppressions"/>.
    /// A malformed pair is a hard error rather than a silent skip: quietly ignoring
    /// it would show a sort the user did not ask for.
    /// </summary>
    private static bool TryParseSuppressions(string[]? raw, out EdgeSuppressions suppressions)
    {
        suppressions = EdgeSuppressions.Empty;
        if (raw is null || raw.Length == 0) return true;

        foreach (var pair in raw)
        {
            var parts = pair.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !ModId.TryFrom(parts[0], out var before)
                || !ModId.TryFrom(parts[1], out var after))
            {
                Console.Error.WriteLine($"Invalid --suppress value '{pair}'. Expected before:after, e.g. a.mod:b.mod");
                return false;
            }

            suppressions = suppressions.With(before, after, "--suppress");
        }

        return true;
    }

    private static void PrintCyclesAndDrops(SortResult result)
    {
        if (!result.SuppressedByUser.IsDefaultOrEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"{result.SuppressedByUser.Length} rule(s) suppressed by you:");
            foreach (var d in result.SuppressedByUser)
                Console.WriteLine($"  · {d.Edge.Before.Display} → {d.Edge.After.Display} ({Format.RuleText(d.Edge.Provenance)})");
        }

        if (!result.DroppedForTier.IsDefaultOrEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"{result.DroppedForTier.Length} rule(s) ignored for violating hard tiering:");
            foreach (var d in result.DroppedForTier.Take(10))
                Console.WriteLine($"  · {d.Edge.Before.Display} → {d.Edge.After.Display} ({Format.RuleText(d.Edge.Provenance)})");
        }

        if (result.HasCycles)
        {
            Console.WriteLine();
            Console.WriteLine($"{result.Cycles.Length} dependency cycle(s) detected and broken:");
            foreach (var broken in result.BrokenEdges)
            {
                var path = string.Join(" → ", broken.Cycle.Select(c => c.Display)) + " → " + broken.Cycle[0].Display;
                Console.WriteLine($"  · cycle: {path}");
                Console.WriteLine($"    broke: {broken.Edge.Before.Display} → {broken.Edge.After.Display} ({Format.RuleText(broken.Edge.Provenance)})");
            }
        }
    }
}
