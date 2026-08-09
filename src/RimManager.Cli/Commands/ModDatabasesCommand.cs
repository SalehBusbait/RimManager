using System.CommandLine;
using System.Text;
using RimManager.Core.ModDatabases;
using RimManager.Integrations.Http;
using RimManager.Storage;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>replacements</c> and <c>knowngood</c> commands (N7): Mlie's two mod
/// databases, each with the <c>rules</c> command's grammar — sync to a local cache,
/// report the cache, and check the real install against it.
/// </summary>
internal static class ModDatabasesCommand
{
    // ------------------------------- replacements ---------------------------

    public static Command BuildReplacements()
    {
        var command = new Command(
            "replacements", "Manage the UseThisInstead database of mod replacements.");
        command.Subcommands.Add(ReplacementsSync());
        command.Subcommands.Add(ReplacementsStatus());
        command.Subcommands.Add(ReplacementsCheck());
        return command;
    }

    private static Command ReplacementsSync()
    {
        var urlOpt = new Option<string?>("--url")
        {
            Description = $"Override the source URL (default: {UseThisInsteadClient.DefaultUrl}).",
        };

        var sync = new Command("sync", "Download the replacements database to the local cache.");
        sync.Options.Add(urlOpt);
        sync.SetAction(parse =>
        {
            var fs = new PhysicalFileSystem();
            using var fetcher = new HttpClientFetcher();

            try
            {
                var db = new UseThisInsteadClient(fetcher)
                    .FetchAsync(parse.GetValue(urlOpt)).GetAwaiter().GetResult();

                // Cached decompressed: the cache is ours, and a text file can be read
                // by a person; the gzip was the transport's business.
                fs.CreateDirectory(AppPaths.CacheDir);
                fs.AtomicWriteAsync(
                        AppPaths.ReplacementsCachePath, Encoding.UTF8.GetBytes(db.RawJson), backup: false)
                    .GetAwaiter().GetResult();

                Console.WriteLine($"Synced {db.Count} replacement rules"
                    + (db.PublishedUtc is { } p ? $" (database version {p.ToLocalTime():yyyy-MM-dd})" : "")
                    + $" → {AppPaths.ReplacementsCachePath}");
                return 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                or RimManager.Core.Abstractions.HttpFetchException)
            {
                Console.Error.WriteLine($"Replacements sync failed: {ex.Message}");
                return 1;
            }
        });
        return sync;
    }

    private static Command ReplacementsStatus()
    {
        var status = new Command("status", "Show the cached replacements database (path, entries, age).");
        status.SetAction(_ =>
        {
            var path = AppPaths.ReplacementsCachePath;
            if (!File.Exists(path))
            {
                Console.WriteLine("No cached replacements. Run `replacements sync` to download them.");
                return 0;
            }

            var db = UseThisInsteadParser.Parse(File.ReadAllText(path));
            var age = DateTime.Now - File.GetLastWriteTime(path);
            Console.WriteLine($"Cached replacements: {db.Count} rules at {path}");
            if (db.PublishedUtc is { } p) Console.WriteLine($"Database version: {p.ToLocalTime():yyyy-MM-dd}");
            Console.WriteLine($"Downloaded: {(int)age.TotalDays}d ago ({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}).");
            return 0;
        });
        return status;
    }

    private static Command ReplacementsCheck()
    {
        var options = CommonOptions.Create();
        var allOpt = new Option<bool>("--all")
        {
            Description = "Check every installed mod, not only the active list.",
        };

        var check = new Command("check", "List installed mods with a maintained replacement.");
        options.AddTo(check);
        check.Options.Add(allOpt);
        check.SetAction(parse =>
        {
            var path = AppPaths.ReplacementsCachePath;
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("No cached replacements. Run `replacements sync` first.");
                return 1;
            }

            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var db = UseThisInsteadParser.Parse(File.ReadAllText(path));
            var version = ctx.ModsConfig?.MajorMinor;

            var mods = parse.GetValue(allOpt)
                ? ctx.Scan.Mods.ToList()
                : (ctx.ModsConfig?.ActiveMods ?? [])
                    .Where(id => ctx.Scan.ById.ContainsKey(id))
                    .Select(id => ctx.Scan.ById[id])
                    .ToList();

            var found = 0;
            foreach (var mod in mods)
            {
                if (ReplacementMatcher.For(mod, db.Replacements, version) is not { } rule) continue;

                found++;
                Console.WriteLine($"  {mod.Name}  [{mod.PackageId.Display}]");
                Console.WriteLine($"    → {rule.NewName} by {rule.NewAuthor}"
                    + $"  (workshop {rule.NewWorkshopId}, supports {string.Join(", ", rule.NewVersions)})");
            }

            Console.WriteLine();
            Console.WriteLine(found == 0
                ? $"No replacements suggested for {mods.Count} mods against {db.Count} rules. ✓"
                : $"{found} of {mods.Count} mods have a maintained replacement ({db.Count} rules"
                  + (version is null ? ", version ungated" : $", gated to {version}") + ").");
            return 0;
        });
        return check;
    }

    // -------------------------------- knowngood -----------------------------

    public static Command BuildKnownGood()
    {
        var command = new Command(
            "knowngood", "Manage the NoVersionWarning list of mods known to work on a game version.");
        command.Subcommands.Add(KnownGoodSync());
        command.Subcommands.Add(KnownGoodStatus());
        command.Subcommands.Add(KnownGoodCheck());
        return command;
    }

    private static readonly Option<string> VersionOpt = new("--game-version")
    {
        Description = "Game version to fetch the list for (e.g. 1.6). Defaults to the installed game's.",
    };

    private static string? ResolveVersion(string? explicitVersion, CommonOptions options, System.CommandLine.ParseResult parse)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion)) return explicitVersion;

        // noCache threads through — the flag is advertised on sync/status, and a flag
        // that is parsed but ignored is a lie (found by the N11 audit).
        var ctx = ScanWorkflow.Run(
            parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
            parse.GetValue(options.ConfigDir), noCache: parse.GetValue(options.NoCache));
        return ctx?.ModsConfig?.MajorMinor;
    }

    private static Command KnownGoodSync()
    {
        var options = CommonOptions.Create();

        var sync = new Command("sync", "Download the known-good list for a game version to the local cache.");
        options.AddTo(sync);
        sync.Options.Add(VersionOpt);
        sync.SetAction(parse =>
        {
            var version = ResolveVersion(parse.GetValue(VersionOpt), options, parse);
            if (version is null)
            {
                Console.Error.WriteLine("Could not determine the game version — pass --game-version.");
                return 1;
            }

            var fs = new PhysicalFileSystem();
            using var fetcher = new HttpClientFetcher();

            try
            {
                var db = new NoVersionWarningClient(fetcher)
                    .FetchAsync(version).GetAwaiter().GetResult();

                if (db.Count == 0)
                {
                    Console.WriteLine($"No known-good list exists upstream for {version} (nothing cached).");
                    return 0;
                }

                fs.CreateDirectory(AppPaths.CacheDir);
                fs.AtomicWriteAsync(
                        AppPaths.KnownGoodCachePath(version), Encoding.UTF8.GetBytes(db.RawXml), backup: false)
                    .GetAwaiter().GetResult();

                Console.WriteLine($"Synced {db.Count} known-good packageIds for {version}"
                    + $" → {AppPaths.KnownGoodCachePath(version)}");
                return 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                or RimManager.Core.Abstractions.HttpFetchException)
            {
                Console.Error.WriteLine($"Known-good sync failed: {ex.Message}");
                return 1;
            }
        });
        return sync;
    }

    private static Command KnownGoodStatus()
    {
        var options = CommonOptions.Create();

        var status = new Command("status", "Show the cached known-good list (path, entries, age).");
        options.AddTo(status);
        status.Options.Add(VersionOpt);
        status.SetAction(parse =>
        {
            var version = ResolveVersion(parse.GetValue(VersionOpt), options, parse);
            if (version is null)
            {
                Console.Error.WriteLine("Could not determine the game version — pass --game-version.");
                return 1;
            }

            var path = AppPaths.KnownGoodCachePath(version);
            if (!File.Exists(path))
            {
                Console.WriteLine($"No cached known-good list for {version}. Run `knowngood sync`.");
                return 0;
            }

            var db = NoVersionWarningParser.Parse(File.ReadAllText(path));
            var age = DateTime.Now - File.GetLastWriteTime(path);
            Console.WriteLine($"Cached known-good list for {version}: {db.Count} packageIds at {path}");
            Console.WriteLine($"Downloaded: {(int)age.TotalDays}d ago ({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}).");
            return 0;
        });
        return status;
    }

    private static Command KnownGoodCheck()
    {
        var options = CommonOptions.Create();

        var check = new Command(
            "check", "List installed mods on the known-good list (their version warning is suppressible).");
        options.AddTo(check);
        check.SetAction(parse =>
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            var version = ctx.ModsConfig?.MajorMinor;
            if (version is null)
            {
                Console.Error.WriteLine("Could not determine the game version from ModsConfig.xml.");
                return 1;
            }

            var path = AppPaths.KnownGoodCachePath(version);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No cached known-good list for {version}. Run `knowngood sync` first.");
                return 1;
            }

            var db = NoVersionWarningParser.Parse(File.ReadAllText(path));
            var listed = ctx.Scan.Mods.Where(m => db.Contains(m.PackageId)).ToList();

            foreach (var mod in listed)
            {
                Console.WriteLine($"  {mod.Name}  [{mod.PackageId.Display}]");
            }

            Console.WriteLine();
            Console.WriteLine($"{listed.Count} of {ctx.Scan.Mods.Length} installed mods are on the "
                + $"known-good list for {version} ({db.Count} entries).");
            return 0;
        });
        return check;
    }
}
