using System.Collections.Immutable;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;

namespace RimManager.Cli.Commands;

/// <summary>Prints a current→proposed load-order diff. Shared by <c>sort</c> and <c>apply</c>.</summary>
internal static class OrderDiff
{
    /// <returns>The number of mods that would move.</returns>
    public static int Print(
        IReadOnlyList<ModId> current,
        ImmutableArray<ModId> newOrder,
        ImmutableDictionary<ModId, Mod> byId,
        ImmutableDictionary<ModId, Tier> tiers,
        bool full)
    {
        var oldPos = new Dictionary<ModId, int>(current.Count);
        for (int i = 0; i < current.Count; i++) oldPos[current[i]] = i;

        int moved = 0;
        for (int i = 0; i < newOrder.Length; i++)
        {
            var id = newOrder[i];
            var wasAt = oldPos.GetValueOrDefault(id, -1);
            bool changed = wasAt != i;
            if (changed) moved++;
            if (!full && !changed) continue;

            var name = byId.TryGetValue(id, out var m) ? m.Name : "(unknown)";
            var delta = changed ? $"{wasAt + 1,4} → {i + 1,-4}" : $"     {i + 1,-4}";
            var tier = Format.TierTag(tiers.GetValueOrDefault(id, Tier.Normal));
            Console.WriteLine($"  {(changed ? "~" : " ")} {delta}  [{tier,-7}]  {id.Display}  —  {name}");
        }

        return moved;
    }
}
