using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using RimManager.Core.Validation;
using RimManager.Core.Writing;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>apply</c> command: write a new active order to ModsConfig.xml — sorted by
/// default — with a backup, refusing while RimWorld runs (spec §3).
/// </summary>
internal static class ApplyCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var rulesOpt = new Option<string?>("--rules") { Description = "Path to a community rules snapshot (communityRules.json)." };
        var noSortOpt = new Option<bool>("--no-sort") { Description = "Write the current order as-is instead of sorting first." };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "Show what would be written without writing." };

        var command = new Command("apply", "Write the (sorted) active order to ModsConfig.xml.");
        options.AddTo(command);
        command.Options.Add(rulesOpt);
        command.Options.Add(noSortOpt);
        command.Options.Add(dryRunOpt);

        command.SetAction((parse, ct) => RunAsync(parse, options, rulesOpt, noSortOpt, dryRunOpt, ct));
        return command;
    }

    private static async Task<int> RunAsync(
        ParseResult parse, CommonOptions options,
        Option<string?> rulesOpt, Option<bool> noSortOpt, Option<bool> dryRunOpt, CancellationToken ct)
    {
        var ctx = ScanWorkflow.Run(
            parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
            parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
        if (ctx is null) return 1;

        if (ctx.ModsConfig is null || ctx.ConfigDir is null)
        {
            Console.Error.WriteLine("No ModsConfig.xml found to update. Pass --config-dir.");
            return 1;
        }

        var active = ScanWorkflow.ActiveMods(ctx);
        var current = active.Select(m => m.PackageId).ToList();

        // Determine the new order.
        System.Collections.Immutable.ImmutableArray<Core.Domain.ModId> newOrder;
        System.Collections.Immutable.ImmutableDictionary<Core.Domain.ModId, Tier> tiers;
        if (parse.GetValue(noSortOpt))
        {
            newOrder = [.. current];
            tiers = System.Collections.Immutable.ImmutableDictionary<Core.Domain.ModId, Tier>.Empty;
        }
        else
        {
            var ruleSet = RuleGraphBuilder.Build(active, RulesLoader.Load(parse.GetValue(rulesOpt)));
            var result = new ModSorter().Sort(active, ruleSet);
            newOrder = result.Order;
            tiers = result.Tiers;
        }

        var moved = OrderDiff.Print(current, newOrder, ctx.Scan.ById, tiers, full: false);
        Console.WriteLine();
        if (moved == 0 && !parse.GetValue(noSortOpt))
            Console.WriteLine("Order already sorted.");
        else
            Console.WriteLine($"{moved} mod(s) would change position.");

        // Validate the order we're about to write.
        var report = new ModListValidator().Validate(
            active, ctx.ModsConfig.KnownExpansions, ctx.ModsConfig.MajorMinor);
        if (report.ErrorCount > 0 || report.WarningCount > 0)
            Console.WriteLine($"Validation: {report.ErrorCount} error(s), {report.WarningCount} warning(s) — run `validate` for detail.");

        if (parse.GetValue(dryRunOpt))
        {
            Console.WriteLine();
            Console.WriteLine("Dry run — nothing written.");
            return 0;
        }

        var newConfig = ApplyService.WithActiveOrder(ctx.ModsConfig, newOrder);
        var apply = new ApplyService(new PhysicalFileSystem(), new RimWorldProcessDetector());
        var modsConfigPath = Path.Combine(ctx.ConfigDir, "ModsConfig.xml");
        // O5 · the same backup folder the GUI uses. Two shells over one install writing
        // their backups to two different places would be the "GUI and CLI must see the
        // same rules" trap in its most literal form.
        var applyResult = await apply.ApplyAsync(modsConfigPath, newConfig, AppPaths.BackupsDir, ct);

        Console.WriteLine();
        Console.WriteLine(applyResult.Message);

        if (applyResult.Written) await StampMatchingModlistAsync(newOrder, ct);

        return applyResult.Written ? 0 : 1;
    }

    /// <summary>
    /// Records that <b>RimManager</b> wrote this order, if some modlist describes it.
    /// <para>
    /// Without this the GUI's drift detector reports <c>ChangedOutsideRimManager</c> after
    /// every CLI apply — accusing RimWorld of a write RimManager itself performed. The
    /// verdict exists to warn that the next Apply would destroy what the game wrote, so a
    /// false one is not cosmetic: it is the boy crying wolf in the one place the wolf
    /// matters.
    /// </para>
    /// <para>
    /// Only a list whose own order is <em>exactly</em> what was written gets stamped.
    /// <c>apply</c> sorts whatever <c>ModsConfig.xml</c> held and does not open a modlist at
    /// all, so there is often no list to credit — and inventing one would put a false
    /// applied-hash on a list the user never applied, which is the same lie pointing the
    /// other way. When nothing matches, nothing is claimed.
    /// </para>
    /// </summary>
    private static async Task StampMatchingModlistAsync(
        IReadOnlyList<ModId> written, CancellationToken ct)
    {
        try
        {
            var repo = new ModlistRepository(new PhysicalFileSystem());
            var hash = ModlistDrift.HashOrder(written);

            var match = repo.List().FirstOrDefault(
                l => ModlistDrift.HashOrder(l.State.ActiveModIds()) == hash);

            if (match is null) return;

            await repo.SaveAsync(
                match with { LastAppliedHash = hash, LastAppliedUtc = DateTimeOffset.UtcNow }, ct);

            Console.WriteLine($"Recorded against modlist '{match.Name}'.");
        }
        catch (Exception ex)
        {
            // Bookkeeping for another surface. It must never turn a successful write into
            // a failed command — the file is already on disk and correct.
            Console.Error.WriteLine($"(could not record the applied order: {ex.Message})");
        }
    }
}
