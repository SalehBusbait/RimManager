using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Sharing;
using RimManager.Core.Writing;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.Cli.Commands;

/// <summary>The <c>export</c> and <c>import</c> commands (spec §4.7 sharing).</summary>
internal static class ShareCommands
{
    public static Command Export()
    {
        var formatOpt = new Option<string>("--format") { Description = "rwlist | modsconfig | markdown | csv.", DefaultValueFactory = _ => "rwlist" };
        var outOpt = new Option<string>("--out") { Description = "Output file path.", Required = true };
        var modlistOpt = new Option<string?>("--modlist") { Description = "Export a saved modlist (by name or id) instead of the current active list." };
        var nameOpt = new Option<string?>("--name") { Description = "List name." };
        var authorOpt = new Option<string?>("--author") { Description = "List author." };
        var descOpt = new Option<string?>("--description") { Description = "List description." };

        var cmd = new Command("export", "Export the modlist to a shareable file.")
        { formatOpt, outOpt, modlistOpt, nameOpt, authorOpt, descOpt };

        cmd.SetAction(async (parse, ct) =>
        {
            var metadata = new MetadataRepository(new PhysicalFileSystem());
            var scan = ScanWorkflow.Run(null, null, null, noCache: false);
            if (scan is null) return 1;

            // N11: --profile became --modlist. A saved modlist carries separators and
            // per-entry identity; the bare active order from the game file carries
            // neither, which is exactly the difference the flag exists to offer.
            ModlistState state;
            var modlistName = parse.GetValue(modlistOpt);
            if (modlistName is not null)
            {
                var repo = new ModlistRepository(new PhysicalFileSystem());
                var modlist = repo.Get(modlistName) ?? repo.FindByName(modlistName);
                if (modlist is null) { Console.Error.WriteLine("Modlist not found."); return 1; }
                state = modlist.State;
            }
            else
            {
                var active = scan.ModsConfig?.ActiveMods ?? [];
                state = ModlistState.Empty.WithEntries(active.Select(id => ModlistEntry.Mod(id)));
            }

            var metaById = metadata.LoadModMetadata().Entries
                .ToDictionary(kv => ModId.From(kv.Key), kv => kv.Value);

            var info = new RwListInfo(
                parse.GetValue(nameOpt) ?? modlistName ?? "Modlist",
                parse.GetValue(authorOpt),
                parse.GetValue(descOpt),
                scan.ModsConfig?.MajorMinor,
                scan.ModsConfig?.KnownExpansions)
            { CreatedUtc = SystemClock.Instance.UtcNow };

            var list = RwListBuilder.Build(state, scan.Scan.ById, metaById,
                metadata.LoadTags().Tags, metadata.LoadCategories().Categories, info);

            var text = parse.GetValue(formatOpt)?.ToLowerInvariant() switch
            {
                "modsconfig" => RwListExport.ToModsConfig(list),
                "markdown" or "md" => RwListExport.ToMarkdown(list),
                "csv" => RwListExport.ToCsv(list),
                _ => RwListExport.ToRwList(list),
            };

            var outPath = parse.GetValue(outOpt)!;
            await File.WriteAllTextAsync(outPath, text, ct);
            Console.WriteLine($"Exported {list.Mods.Count()} mods to {outPath}.");
            return 0;
        });

        return cmd;
    }

    public static Command Import()
    {
        var fileArg = new Argument<string>("file") { Description = "A .rwlist or ModsConfig.xml to import." };
        var applyOpt = new Option<bool>("--apply") { Description = "Write the installed mods from the list to the game (backup + guard)." };

        var cmd = new Command("import", "Import a modlist and reconcile it against what's installed.") { fileArg, applyOpt };

        cmd.SetAction(async (parse, ct) =>
        {
            var path = parse.GetValue(fileArg)!;
            if (!File.Exists(path)) { Console.Error.WriteLine($"File not found: {path}"); return 1; }

            var list = RwListImport.Load(await File.ReadAllTextAsync(path, ct), out var checksumValid);
            var scan = ScanWorkflow.Run(null, null, null, noCache: false);
            if (scan is null) return 1;

            if (!checksumValid) Console.WriteLine("⚠ Checksum mismatch — the .rwlist may be corrupt or edited.");

            var report = ImportReconciler.Reconcile(list, scan.Scan.ById);
            Console.WriteLine();
            Console.WriteLine($"{report.InstalledCount} installed · {report.MissingCount} missing · {report.VersionMismatchCount} version-mismatch.");

            foreach (var item in report.Items.Where(i => i.Status == ImportStatus.Missing))
                Console.WriteLine($"  missing:  {item.PackageId.Display}  —  {item.DisplayName}");
            foreach (var item in report.Items.Where(i => i.Status == ImportStatus.VersionMismatch))
                Console.WriteLine($"  version:  {item.PackageId.Display}  list={item.ListedVersion}  installed={item.InstalledVersion}");

            if (parse.GetValue(applyOpt))
            {
                if (scan.ModsConfig is null || scan.ConfigDir is null)
                {
                    Console.Error.WriteLine("No ModsConfig to apply to.");
                    return 1;
                }

                var order = report.Items
                    .Where(i => i.Status != ImportStatus.Missing)
                    .Select(i => i.PackageId);
                var newConfig = ApplyService.WithActiveOrder(scan.ModsConfig, order);
                var apply = new ApplyService(new PhysicalFileSystem(), new RimWorldProcessDetector());
                var result = await apply.ApplyAsync(
                    Path.Combine(scan.ConfigDir, "ModsConfig.xml"), newConfig, AppPaths.BackupsDir, ct);
                Console.WriteLine();
                Console.WriteLine(result.Message);
            }

            return 0;
        });

        return cmd;
    }
}
