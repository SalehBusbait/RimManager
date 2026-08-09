using System.CommandLine;
using System.Text;
using RimManager.Core.Rules;
using RimManager.Integrations.Http;
using RimManager.Storage;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>rules</c> command (Phase 6): syncs the live community load-order rules
/// database to a local cache, which <see cref="RulesLoader"/> then feeds to the sorter
/// — taking rules from snapshot-only to live.
/// </summary>
internal static class RulesCommand
{
    public static Command Build()
    {
        var command = new Command("rules", "Manage the community load-order rules database.");
        command.Subcommands.Add(BuildSync());
        command.Subcommands.Add(BuildStatus());
        return command;
    }

    private static Command BuildSync()
    {
        var urlOpt = new Option<string?>("--url") { Description = $"Override the source URL (default: {CommunityRulesClient.DefaultUrl})." };

        var sync = new Command("sync", "Download the community rules database to the local cache.");
        sync.Options.Add(urlOpt);
        sync.SetAction(parse => RunSync(parse.GetValue(urlOpt)));
        return sync;
    }

    private static Command BuildStatus()
    {
        var status = new Command("status", "Show the cached community rules database (path, entries, age).");
        status.SetAction(_ => RunStatus());
        return status;
    }

    private static int RunSync(string? url)
    {
        var fs = new PhysicalFileSystem();
        using var fetcher = new HttpClientFetcher();
        var client = new CommunityRulesClient(fetcher);

        try
        {
            var db = client.FetchAsync(url).GetAwaiter().GetResult();
            var bytes = Encoding.UTF8.GetBytes(db.RawJson);
            fs.CreateDirectory(AppPaths.CacheDir);
            fs.AtomicWriteAsync(AppPaths.CommunityRulesCachePath, bytes, backup: false).GetAwaiter().GetResult();

            Console.WriteLine($"Synced {db.RuleCount} rule entries"
                + (db.PublishedUtc is { } p ? $" (database published {p.ToLocalTime():yyyy-MM-dd})" : "")
                + $" → {AppPaths.CommunityRulesCachePath}");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Console.Error.WriteLine($"Rules sync failed: {ex.Message}");
            return 1;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"Downloaded rules were not valid JSON: {ex.Message}");
            return 1;
        }
    }

    private static int RunStatus()
    {
        var path = AppPaths.CommunityRulesCachePath;
        if (!File.Exists(path))
        {
            Console.WriteLine("No cached community rules. Run `rules sync` to download them.");
            return 0;
        }

        var db = CommunityRulesClient.Build(File.ReadAllText(path));
        var age = DateTime.Now - File.GetLastWriteTime(path);
        Console.WriteLine($"Cached rules: {db.RuleCount} entries at {path}");
        if (db.PublishedUtc is { } p) Console.WriteLine($"Database published: {p.ToLocalTime():yyyy-MM-dd}");
        Console.WriteLine($"Downloaded: {(int)age.TotalDays}d ago ({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}).");
        return 0;
    }
}
