using System.CommandLine;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;
using RimManager.Integrations.Http;
using RimManager.Integrations.SteamCmd;
using RimManager.Storage;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>workshop info</c> command (Phase 6): looks up Steam Workshop metadata for
/// one or more published-file ids via the keyless <c>GetPublishedFileDetails</c>
/// endpoint. With no ids, it resolves the <c>PublishedFileId</c> of every active
/// Workshop mod in the current install — exercising the scan↔Workshop join.
/// </summary>
internal static class WorkshopCommand
{
    public static Command Build()
    {
        var options = CommonOptions.Create();
        var idsArg = new Argument<string[]>("ids")
        {
            Description = "Workshop published-file ids. If omitted, uses the active Workshop mods.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var info = new Command("info", "Fetch Steam Workshop metadata for published-file ids (keyless).");
        options.AddTo(info);
        info.Arguments.Add(idsArg);
        info.SetAction(parse => Run(parse, options, idsArg));

        var command = new Command("workshop", "Steam Workshop metadata and integration.");
        command.Subcommands.Add(info);
        command.Subcommands.Add(BuildUpdates());
        command.Subcommands.Add(BuildCollection());
        command.Subcommands.Add(BuildDownload());
        return command;
    }

    private static Command BuildDownload()
    {
        var options = CommonOptions.Create();
        var idsArg = new Argument<string[]>("ids")
        {
            Description = "Workshop published-file ids to download.",
            Arity = ArgumentArity.OneOrMore,
        };
        var steamcmdOpt = new Option<string?>("--steamcmd") { Description = "Path to a steamcmd executable (default: RimManager's private instance)." };
        var toOpt = new Option<string?>("--to") { Description = "Destination folder (default: <game>/Mods)." };
        var provisionOpt = new Option<bool>("--provision") { Description = "Provision RimManager's private SteamCMD if absent (~200MB first run)." };

        var download = new Command("download", "Download Workshop items anonymously via SteamCMD (no login) into the Mods folder.");
        options.AddTo(download);
        download.Arguments.Add(idsArg);
        download.Options.Add(steamcmdOpt);
        download.Options.Add(toOpt);
        download.Options.Add(provisionOpt);
        download.SetAction(parse => RunDownload(parse, options, idsArg, steamcmdOpt, toOpt, provisionOpt));
        return download;
    }

    private static int RunDownload(
        ParseResult parse, CommonOptions options, Argument<string[]> idsArg,
        Option<string?> steamcmdOpt, Option<string?> toOpt, Option<bool> provisionOpt)
    {
        var ids = (parse.GetValue(idsArg) ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) { Console.Error.WriteLine("No ids given."); return 1; }

        var exe = ResolveSteamCmdExe(parse.GetValue(steamcmdOpt), parse.GetValue(provisionOpt));
        if (exe is null) return 1;

        // Resolve the destination Mods folder.
        string modsDir;
        if (parse.GetValue(toOpt) is { } to) { modsDir = to; }
        else
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;
            modsDir = Path.Combine(ctx.Install.GameDir, "Mods");
        }

        return DownloadAndInstall(ids, exe, modsDir);
    }

    /// <summary>Finds a usable steamcmd (override → RimManager's private instance), provisioning
    /// on demand only when <paramref name="provision"/> is set. Prints guidance and returns null
    /// when none is available.</summary>
    private static string? ResolveSteamCmdExe(string? steamcmdOverride, bool provision)
    {
        var provisioner = new SteamCmdProvisioner(AppPaths.SteamCmdDir);
        var exe = steamcmdOverride ?? provisioner.ExePath;
        if (File.Exists(exe)) return exe;

        if (!provision)
        {
            Console.Error.WriteLine($"No SteamCMD at {exe}. Pass --steamcmd <exe>, or --provision to download it (~200MB first run).");
            return null;
        }

        Console.WriteLine("Provisioning RimManager's private SteamCMD (first run downloads ~200MB)…");
        return provisioner.EnsureProvisionedAsync().GetAwaiter().GetResult();
    }

    /// <summary>Downloads the ids anonymously and relocates each into <paramref name="modsDir"/>.
    /// Shared by `workshop download` and `workshop collection --download`.</summary>
    private static int DownloadAndInstall(IReadOnlyList<string> ids, string exe, string modsDir)
    {
        Console.WriteLine($"Downloading {ids.Count} item(s) anonymously via {Path.GetFileName(exe)}…");
        var outcomes = new WorkshopDownloadService().DownloadAndInstallAsync(
            ids, exe, modsDir, AppPaths.SteamCmdDownloadsDir,
            onLine: line => { if (line.Contains("Downloading item") || line.Contains("Success.") || line.Contains("ERROR!")) Console.WriteLine($"  {line.Trim()}"); })
            .GetAwaiter().GetResult();

        Console.WriteLine();
        var installed = 0;
        foreach (var o in outcomes)
        {
            if (o.Installed) { Console.WriteLine($"  ✓ {o.PublishedFileId} → {o.Path}"); installed++; }
            else Console.WriteLine($"  ✗ {o.PublishedFileId} — {o.Error}");
        }

        var failed = outcomes.Count - installed;
        Console.WriteLine($"\nInstalled {installed}/{ids.Count} item(s) into {modsDir}"
            + (failed > 0 ? $"; {failed} failed." : "."));
        return failed > 0 ? 1 : 0;
    }

    private static Command BuildCollection()
    {
        var options = CommonOptions.Create();
        var urlArg = new Argument<string>("url") { Description = "A Workshop collection URL or id." };
        var missingOpt = new Option<bool>("--missing") { Description = "Only list members that aren't installed." };

        var subscribeOpt = new Option<bool>("--subscribe")
        {
            Description = "Open the collection in the Steam client for one-click 'Subscribe to all' (uses your logged-in account).",
        };
        var downloadOpt = new Option<bool>("--download")
        {
            Description = "Download the missing members anonymously via SteamCMD into the Mods folder (no login).",
        };
        var steamcmdOpt = new Option<string?>("--steamcmd") { Description = "Path to a steamcmd executable (with --download)." };
        var provisionOpt = new Option<bool>("--provision") { Description = "Provision RimManager's private SteamCMD if absent (with --download; ~200MB first run)." };

        var collection = new Command("collection", "Resolve a Workshop collection and show installed vs missing members.");
        options.AddTo(collection);
        collection.Arguments.Add(urlArg);
        collection.Options.Add(missingOpt);
        collection.Options.Add(subscribeOpt);
        collection.Options.Add(downloadOpt);
        collection.Options.Add(steamcmdOpt);
        collection.Options.Add(provisionOpt);
        collection.SetAction(parse => RunCollection(parse, options, urlArg, missingOpt, subscribeOpt, downloadOpt, steamcmdOpt, provisionOpt));
        return collection;
    }

    private static int RunCollection(
        ParseResult parse, CommonOptions options, Argument<string> urlArg,
        Option<bool> missingOpt, Option<bool> subscribeOpt, Option<bool> downloadOpt,
        Option<string?> steamcmdOpt, Option<bool> provisionOpt)
    {
        if (!WorkshopUrl.TryGetId(parse.GetValue(urlArg), out var collectionId))
        {
            Console.Error.WriteLine("Could not find a Workshop id in that input. Pass a collection URL or a numeric id.");
            return 1;
        }

        var ctx = ScanWorkflow.Run(
            parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
            parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
        if (ctx is null) return 1;

        using var fetcher = new HttpClientFetcher();
        var client = new SteamWorkshopClient(fetcher);

        try
        {
            var collection = client.GetCollectionAsync(collectionId).GetAwaiter().GetResult();
            if (collection is null || !collection.IsOk || collection.MemberIds.IsDefaultOrEmpty)
            {
                Console.Error.WriteLine($"Collection {collectionId} did not resolve to any members "
                    + "(deleted, private, or an item rather than a collection?).");
                return 1;
            }

            // Names for every member — especially the missing ones with no local name.
            var metadata = client.GetByIdAsync(collection.MemberIds).GetAwaiter().GetResult();
            var installedByFileId = CollectionReconciler.IndexByFileId(ctx.Scan.Mods);
            var report = CollectionReconciler.Reconcile(collection.MemberIds, installedByFileId, metadata);

            PrintCollection(collectionId, report, parse.GetValue(missingOpt));

            if (parse.GetValue(downloadOpt))
            {
                return DownloadMissing(report, ctx.Install.GameDir,
                    parse.GetValue(steamcmdOpt), parse.GetValue(provisionOpt));
            }

            if (parse.GetValue(subscribeOpt))
            {
                return SubscribeViaClient(collectionId, report);
            }

            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Console.Error.WriteLine($"Collection lookup failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintCollection(string collectionId, CollectionReport report, bool missingOnly)
    {
        Console.WriteLine();
        Console.WriteLine($"Collection {collectionId}: {report.Members.Length} member(s), "
            + $"{report.InstalledCount} installed, {report.MissingCount} missing.");
        Console.WriteLine();

        var shown = missingOnly ? report.Missing : report.Members;
        foreach (var m in shown)
        {
            var mark = m.IsInstalled ? "✓" : (m.IsDelisted ? "✗" : "·");
            var note = m.IsInstalled ? "" : (m.IsDelisted ? "  (delisted)" : "  (not installed)");
            Console.WriteLine($"  {mark} {m.PublishedFileId}  {m.DisplayName}{note}");
        }

        if (report.MissingCount > 0 && !missingOnly)
        {
            Console.WriteLine();
            Console.WriteLine($"{report.MissingCount} member(s) not installed — add --download (SteamCMD, no login) or --subscribe (opens Steam).");
        }
    }

    /// <summary>Downloads a collection's missing members via SteamCMD into the game's Mods folder.</summary>
    private static int DownloadMissing(CollectionReport report, string gameDir, string? steamcmdOverride, bool provision)
    {
        Console.WriteLine();
        var missing = report.Missing.Select(m => m.PublishedFileId).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine("Every member is already installed — nothing to download.");
            return 0;
        }

        var exe = ResolveSteamCmdExe(steamcmdOverride, provision);
        if (exe is null) return 1;

        return DownloadAndInstall(missing, exe, Path.Combine(gameDir, "Mods"));
    }

    /// <summary>
    /// Hands the collection to the running Steam client via a steam:// deep-link. The
    /// client is already logged into an account that owns the game, so its native
    /// "Subscribe to all" downloads the missing members — no separate login, and
    /// RimManager never touches the user's credentials.
    /// </summary>
    private static int SubscribeViaClient(string collectionId, CollectionReport report)
    {
        Console.WriteLine();
        if (report.MissingCount == 0)
        {
            Console.WriteLine("Every member is already installed — nothing to subscribe.");
            return 0;
        }

        var steamUrl = SteamUrls.CommunityFilePage(collectionId);
        try
        {
            new ShellUriLauncher().Launch(steamUrl);
            Console.WriteLine($"Opened the collection in Steam ({steamUrl}).");
            Console.WriteLine($"Click \"Subscribe to all\" to download the {report.MissingCount} missing member(s); "
                + "already-installed members stay subscribed.");
            Console.WriteLine("Re-run `workshop collection <url>` afterwards to confirm they're installed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Couldn't open Steam automatically ({ex.Message}).");
            Console.Error.WriteLine($"Open it yourself: {steamUrl}");
            Console.Error.WriteLine($"…or in a browser: {SteamUrls.WebFilePage(collectionId)}");
            return 1;
        }
    }

    private static Command BuildUpdates()
    {
        var options = CommonOptions.Create();
        var allOpt = new Option<bool>("--all") { Description = "Check every installed Workshop mod, not just the active ones." };
        var outdatedOpt = new Option<bool>("--outdated") { Description = "Only list mods with an update available." };

        var updates = new Command("updates", "Check active Workshop mods for available updates (keyless).");
        options.AddTo(updates);
        updates.Options.Add(allOpt);
        updates.Options.Add(outdatedOpt);
        updates.SetAction(parse => RunUpdates(parse, options, allOpt, outdatedOpt));
        return updates;
    }

    private static int RunUpdates(
        ParseResult parse, CommonOptions options, Option<bool> allOpt, Option<bool> outdatedOpt)
    {
        var ctx = ScanWorkflow.Run(
            parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
            parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
        if (ctx is null) return 1;

        var mods = (parse.GetValue(allOpt) ? ctx.Scan.Mods : ScanWorkflow.ActiveMods(ctx))
            .Where(m => m.Source == ModSource.Workshop && m.PublishedFileId is not null)
            .ToList();
        if (mods.Count == 0)
        {
            Console.WriteLine("No Workshop mods with a PublishedFileId to check.");
            return 0;
        }

        // Installed state: Steam's own manifest, next to the Workshop content dir.
        var fs = new PhysicalFileSystem();
        var installed = WorkshopInstallState.Empty;
        var acfPath = LocateWorkshopManifest(ctx.Install.WorkshopDir);
        if (acfPath is not null && fs.FileExists(acfPath))
        {
            installed = WorkshopManifestParser.Parse(fs.ReadAllText(acfPath));
        }
        else
        {
            Console.WriteLine("(Steam workshop manifest not found — install times unknown; results will be 'not tracked'.)");
        }

        using var fetcher = new HttpClientFetcher();
        var client = new SteamWorkshopClient(fetcher);

        try
        {
            var remote = client.GetByIdAsync(mods.Select(m => m.PublishedFileId!)).GetAwaiter().GetResult();
            var statuses = UpdateChecker.Check(mods, installed, remote);
            PrintUpdates(statuses, parse.GetValue(outdatedOpt));
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Console.Error.WriteLine($"Update check failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The manifest lives at <c>&lt;library&gt;/steamapps/workshop/appworkshop_294100.acf</c>,
    /// i.e. two levels up from the content dir (<c>…/workshop/content/294100</c>).</summary>
    private static string? LocateWorkshopManifest(string? workshopContentDir)
    {
        if (workshopContentDir is null) return null;
        var workshopRoot = Path.GetDirectoryName(Path.GetDirectoryName(workshopContentDir.TrimEnd('/', '\\')));
        return workshopRoot is null
            ? null
            : Path.Combine(workshopRoot, $"appworkshop_{SteamWorkshopClient.RimWorldAppId}.acf");
    }

    private static void PrintUpdates(IReadOnlyList<ModUpdateStatus> statuses, bool outdatedOnly)
    {
        var updatable = statuses.Where(s => s.HasUpdate).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Console.WriteLine();
        if (updatable.Count == 0)
        {
            Console.WriteLine("All checked Workshop mods are up to date. ✓");
        }
        else
        {
            Console.WriteLine($"Updates available ({updatable.Count}):");
            foreach (var s in updatable)
            {
                Console.WriteLine($"  {s.Name}  ({s.PublishedFileId})");
                Console.WriteLine($"      installed {Date(s.InstalledUtc)}  →  workshop {Date(s.RemoteUtc)}");
            }
        }

        if (!outdatedOnly)
        {
            var delisted = statuses.Where(s => s.Status == UpdateStatus.Delisted).ToList();
            var untracked = statuses.Where(s => s.Status == UpdateStatus.NotTracked).ToList();
            if (delisted.Count > 0)
                Console.WriteLine($"\nDelisted / unavailable ({delisted.Count}): {string.Join(", ", delisted.Select(s => s.Name))}");
            if (untracked.Count > 0)
                Console.WriteLine($"\nNot tracked ({untracked.Count}): {string.Join(", ", untracked.Select(s => s.Name))}");
        }

        var upToDate = statuses.Count(s => s.Status == UpdateStatus.UpToDate);
        Console.WriteLine($"\n{statuses.Count} checked — {updatable.Count} updatable, {upToDate} up to date.");
    }

    private static string Date(DateTimeOffset? t) =>
        t is { } dt ? dt.ToLocalTime().ToString("yyyy-MM-dd") : "—";

    private static int Run(ParseResult parse, CommonOptions options, Argument<string[]> idsArg)
    {
        // Map each requested id to a display label (mod name when we know it, else the id).
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var explicitIds = parse.GetValue(idsArg) ?? [];

        List<string> ids;
        if (explicitIds.Length > 0)
        {
            ids = explicitIds.ToList();
        }
        else
        {
            var ctx = ScanWorkflow.Run(
                parse.GetValue(options.GameDir), parse.GetValue(options.WorkshopDir),
                parse.GetValue(options.ConfigDir), parse.GetValue(options.NoCache));
            if (ctx is null) return 1;

            ids = [];
            foreach (var mod in ScanWorkflow.ActiveMods(ctx))
            {
                if (mod.Source != ModSource.Workshop || mod.PublishedFileId is not { } fid) continue;
                ids.Add(fid);
                labels[fid] = mod.Name;
            }

            if (ids.Count == 0)
            {
                Console.WriteLine("No active Workshop mods with a PublishedFileId to look up.");
                return 0;
            }

            Console.WriteLine($"Looking up {ids.Count} active Workshop mod(s)…");
            Console.WriteLine();
        }

        using var fetcher = new HttpClientFetcher();
        var client = new SteamWorkshopClient(fetcher);

        try
        {
            var items = client.GetPublishedFileDetailsAsync(ids).GetAwaiter().GetResult();
            Print(items, labels);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Console.Error.WriteLine($"Workshop lookup failed: {ex.Message}");
            return 1;
        }
    }

    private static void Print(
        IReadOnlyList<WorkshopItem> items,
        IReadOnlyDictionary<string, string> labels)
    {
        foreach (var item in items)
        {
            var label = labels.TryGetValue(item.PublishedFileId, out var name) ? name : item.Title ?? "(untitled)";

            if (!item.IsOk)
            {
                Console.WriteLine($"{item.PublishedFileId}  {label}  —  {item.Result} (unavailable)");
                continue;
            }

            Console.WriteLine($"{item.PublishedFileId}  {item.Title ?? label}");
            Console.WriteLine($"    updated: {FormatTime(item.TimeUpdatedUtc)}   created: {FormatTime(item.TimeCreatedUtc)}");
            Console.WriteLine($"    size:    {FormatSize(item.FileSize)}   app: {item.ConsumerAppId}"
                + (item.Tags.IsDefaultOrEmpty ? "" : $"   tags: {string.Join(", ", item.Tags)}"));
            if (!item.Children.IsDefaultOrEmpty)
            {
                Console.WriteLine($"    requires {item.Children.Length} item(s): {string.Join(", ", item.Children)}");
            }
        }

        var missing = items.Count(i => !i.IsOk);
        Console.WriteLine();
        Console.WriteLine($"Resolved {items.Count - missing}/{items.Count} item(s)"
            + (missing > 0 ? $", {missing} unavailable." : "."));
    }

    private static string FormatTime(DateTimeOffset? t) =>
        t is { } dt ? dt.ToLocalTime().ToString("yyyy-MM-dd") : "—";

    private static string FormatSize(long? bytes)
    {
        if (bytes is not { } b || b <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB"];
        double size = b;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}
