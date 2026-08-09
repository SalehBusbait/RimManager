using FluentAssertions;
using RimManager.Core.Domain;
using RimManager.Core.Locators;
using RimManager.Core.Parsing;
using RimManager.Core.Scanning;
using RimManager.Core.Sorting;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

public sealed class SortingLiveTests
{
    [SkippableFact]
    public void Community_rules_fixture_parses_and_feeds_the_builder()
    {
        var root = Fixtures.Root();
        Skip.If(root is null, "No /fixtures present.");
        var rulesPath = Path.Combine(root!, "community", "communityRules.json");
        Skip.IfNot(File.Exists(rulesPath), "No community rules fixture.");

        var rules = CommunityRulesParser.Parse(File.ReadAllText(rulesPath));
        rules.Rules[ModId.From("vanillaexpanded.vfecore")].LoadAfter[0].PackageId
            .Should().Be(ModId.From("brrainz.harmony"));
        rules.Rules[ModId.From("some.patchmod")].LoadBottom.Should().BeTrue();
    }

    [SkippableFact]
    public void Sorting_the_real_active_list_is_idempotent()
    {
        var fs = new PhysicalFileSystem();
        var env = new PlatformEnvironment();

        var install = InstallLocator.LocateAll(env, fs).FirstOrDefault();
        Skip.If(install is null, "No RimWorld install detected.");
        var configDir = InstallLocator.LocateConfigDirectory(env, fs);
        Skip.If(configDir is null, "No config dir detected.");

        var config = ModsConfigParser.Parse(File.ReadAllText(Path.Combine(configDir!, "ModsConfig.xml")));
        var scan = new ModScanner(fs).Scan(install!.ToSourceRoots(), config.MajorMinor);

        var active = config.ActiveMods
            .Where(id => scan.ById.ContainsKey(id))
            .Select(id => scan.ById[id])
            .ToList();
        Skip.If(active.Count < 20, "Not enough active mods to be meaningful.");

        var sorter = new ModSorter();
        var ruleSet = RuleGraphBuilder.Build(active);
        var firstOrder = sorter.Sort(active, ruleSet).Order;

        // Re-sort the already-sorted order: must be a no-op on the real 200+ mod list.
        var reordered = firstOrder.Select(id => scan.ById[id]).ToList();
        var secondOrder = sorter.Sort(reordered, RuleGraphBuilder.Build(reordered)).Order;

        secondOrder.Should().Equal(firstOrder, "sorting the real list must converge in one pass");

        // And every applied edge must be respected in the final order.
        var result = sorter.Sort(active, ruleSet);
        foreach (var edge in result.AppliedEdges)
        {
            result.PositionOf(edge.Before).Should().BeLessThan(result.PositionOf(edge.After));
        }
    }
}
