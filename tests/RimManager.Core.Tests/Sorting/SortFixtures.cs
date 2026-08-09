using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Core.Tests.Sorting;

/// <summary>Helpers for building mods and rule sets in sorter tests.</summary>
internal static class SortFixtures
{
    public static Mod Mod(
        string id,
        ModSource source = ModSource.Workshop,
        string[]? loadAfter = null,
        string[]? loadBefore = null,
        string[]? forceLoadAfter = null,
        string[]? forceLoadBefore = null,
        string[]? dependencies = null,
        string[]? incompatibleWith = null,
        string[]? supportedVersions = null)
        => new()
        {
            PackageId = ModId.From(id),
            Name = id,
            Source = source,
            RootPath = "/" + id,
            LoadAfter = ToIds(loadAfter),
            LoadBefore = ToIds(loadBefore),
            ForceLoadAfter = ToIds(forceLoadAfter),
            ForceLoadBefore = ToIds(forceLoadBefore),
            IncompatibleWith = ToIds(incompatibleWith),
            SupportedVersions = supportedVersions is null ? [] : [.. supportedVersions],
            Dependencies = dependencies is null ? []
                : [.. dependencies.Select(d => new ModDependency(ModId.From(d), d))],
        };

    private static ImmutableArray<ModId> ToIds(string[]? ids) =>
        ids is null ? [] : [.. ids.Select(ModId.From)];

    /// <summary>A rule set of plain About-level edges (before → after), no tier hints.</summary>
    public static RuleSet Edges(params (string before, string after)[] edges)
    {
        var built = edges.Select(e => new OrderingEdge(
            ModId.From(e.before), ModId.From(e.after),
            new RuleProvenance(RuleSource.About, RuleType.LoadAfter))).ToImmutableArray();

        return new RuleSet(built,
            ImmutableDictionary<ModId, RuleProvenance>.Empty,
            ImmutableDictionary<ModId, RuleProvenance>.Empty);
    }

    public static RuleSet NoRules() => Edges();

    public static List<string> Order(SortResult r) => r.Order.Select(id => id.Value).ToList();
}
