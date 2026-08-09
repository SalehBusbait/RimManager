using System.CommandLine;
using RimManager.Core.Github;
using RimManager.Integrations.Http;

namespace RimManager.Cli.Commands;

/// <summary>
/// The <c>github</c> command (Phase 6): reads releases for mods distributed on GitHub
/// rather than the Workshop. Exercises the GET half of the network seam.
/// </summary>
internal static class GithubCommand
{
    public static Command Build()
    {
        var repoArg = new Argument<string>("repo") { Description = "A GitHub repo URL or owner/repo." };
        var allOpt = new Option<bool>("--all") { Description = "List recent releases instead of just the latest." };

        var releases = new Command("releases", "Show GitHub release info for a mod's repository.");
        releases.Arguments.Add(repoArg);
        releases.Options.Add(allOpt);
        releases.SetAction(parse => Run(parse, repoArg, allOpt));

        var command = new Command("github", "GitHub release metadata for off-Workshop mods.");
        command.Subcommands.Add(releases);
        return command;
    }

    private static int Run(ParseResult parse, Argument<string> repoArg, Option<bool> allOpt)
    {
        if (!GitHubRepoRef.TryParse(parse.GetValue(repoArg), out var repo))
        {
            Console.Error.WriteLine("Could not parse a GitHub owner/repo from that input.");
            return 1;
        }

        using var fetcher = new HttpClientFetcher();
        var client = new GitHubReleasesClient(fetcher);

        try
        {
            if (parse.GetValue(allOpt))
            {
                var releases = client.GetReleasesAsync(repo).GetAwaiter().GetResult();
                if (releases.IsDefaultOrEmpty)
                {
                    Console.WriteLine($"{repo}: no releases.");
                    return 0;
                }

                Console.WriteLine($"{repo}: {releases.Length} recent release(s)");
                foreach (var r in releases) PrintRelease(r, brief: true);
            }
            else
            {
                var latest = client.GetLatestReleaseAsync(repo).GetAwaiter().GetResult();
                if (latest is null)
                {
                    Console.WriteLine($"{repo}: no published release.");
                    return 0;
                }

                Console.WriteLine($"{repo} — latest release:");
                PrintRelease(latest, brief: false);
            }

            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or RimManager.Core.Abstractions.HttpFetchException)
        {
            Console.Error.WriteLine($"GitHub lookup failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintRelease(GitHubRelease r, bool brief)
    {
        var flags = (r.IsPrerelease ? " [prerelease]" : "") + (r.IsDraft ? " [draft]" : "");
        Console.WriteLine($"  {r.TagName}  {r.DisplayName}{flags}   {Date(r.PublishedAtUtc)}");

        if (brief) return;

        if (r.HtmlUrl is not null) Console.WriteLine($"    {r.HtmlUrl}");
        foreach (var a in r.Assets)
        {
            Console.WriteLine($"    asset: {a.Name}  ({FormatSize(a.Size)})");
        }
    }

    private static string Date(DateTimeOffset? t) =>
        t is { } dt ? dt.ToLocalTime().ToString("yyyy-MM-dd") : "—";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}
