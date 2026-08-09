using RimManager.Core.Parsing;
using RimManager.Core.Sorting;
using RimManager.Storage;

namespace RimManager.Cli;

/// <summary>
/// Loads community load-order rules for the sorter. Precedence: an explicit
/// <c>--rules &lt;file&gt;</c>, else the locally synced database (see
/// <c>rules sync</c>), else none (About-only sort).
/// </summary>
internal static class RulesLoader
{
    public static LoadOrderRules Load(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Rules file not found: {path} — sorting with About.xml rules only.");
                return LoadOrderRules.Empty;
            }

            var rules = CommunityRulesParser.Parse(File.ReadAllText(path));
            Console.WriteLine($"Community rules: {rules.Rules.Count} entries from {path}.");
            Console.WriteLine();
            return rules;
        }

        // Fall back to the synced database if the user has run `rules sync`.
        var cachePath = AppPaths.CommunityRulesCachePath;
        if (File.Exists(cachePath))
        {
            var rules = CommunityRulesParser.Parse(File.ReadAllText(cachePath));
            var age = (int)(DateTime.Now - File.GetLastWriteTime(cachePath)).TotalDays;
            Console.WriteLine($"Community rules: {rules.Rules.Count} entries from synced database ({age}d old; `rules sync` to refresh).");
            Console.WriteLine();
            return rules;
        }

        Console.WriteLine("Community rules: none (run `rules sync`, or pass --rules <communityRules.json>).");
        Console.WriteLine();
        return LoadOrderRules.Empty;
    }
}
