using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.Core.Analysis;

/// <summary>A mod implicated by a crash log, and how many stack references point at it.</summary>
public sealed record CrashSuspect(ModId PackageId, string DisplayName, int Hits, ImmutableArray<string> Namespaces);

public sealed record CrashReport(ImmutableArray<CrashSuspect> Suspects)
{
    public static readonly CrashReport Empty = new([]);
}

/// <summary>One mod's assembly root namespace (vanilla/library namespaces excluded upstream).</summary>
public sealed record ModNamespace(string Namespace, ModId PackageId, string DisplayName);

/// <summary>
/// Pure ranking of crash-log suspects: counts how often each mod's assembly
/// namespaces appear in the log's stack frames (spec §4.5 crash-log analysis).
/// Building the namespace map needs Cecil (in Storage); the ranking is testable here.
/// </summary>
public static class CrashLogRanker
{
    public static CrashReport Rank(string log, IEnumerable<ModNamespace> modNamespaces)
    {
        var hits = new Dictionary<ModId, (string Display, int Count, HashSet<string> Namespaces)>();

        foreach (var mn in modNamespaces)
        {
            // A namespace is "used" in a frame as "Namespace." — the trailing dot cuts false hits.
            var count = CountOccurrences(log, mn.Namespace + ".");
            if (count == 0) continue;

            if (hits.TryGetValue(mn.PackageId, out var current))
            {
                current.Count += count;
                current.Namespaces.Add(mn.Namespace);
                hits[mn.PackageId] = current;
            }
            else
            {
                hits[mn.PackageId] = (mn.DisplayName, count, [mn.Namespace]);
            }
        }

        var suspects = hits
            .Select(kv => new CrashSuspect(kv.Key, kv.Value.Display, kv.Value.Count,
                [.. kv.Value.Namespaces.OrderBy(n => n, StringComparer.Ordinal)]))
            .OrderByDescending(s => s.Hits)
            .ThenBy(s => s.PackageId.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        return new CrashReport(suspects);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
