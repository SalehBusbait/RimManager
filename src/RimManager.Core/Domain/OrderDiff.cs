using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>One mod in a different place: 1-based positions in each order.</summary>
public sealed record OrderMove(ModId Id, int YoursPosition, int TheirsPosition);

/// <summary>A mod only the incoming order has, and where it lands (1-based).</summary>
public sealed record OrderInsert(ModId Id, int TheirsPosition);

/// <summary>A mod only the on-screen order has, and where it sat (1-based).</summary>
public sealed record OrderRemove(ModId Id, int YoursPosition);

/// <summary>
/// The difference between two load orders, <b>anchored by packageId over the longest
/// common subsequence</b> (S-ORDERDIFF): unchanged rows are the LCS, rows present in
/// both orders but outside it are moves, one-sided rows are inserts or removes. An
/// insert at the top is therefore ONE insert — never "547 moves", which is what an
/// index-inequality diff reports and why the spec discredits that shape by name.
/// <para>
/// Deliberately separate from <see cref="ProfileDiff"/>, whose positional "moved" is
/// exactly the discredited comparison: History uses it for per-mod position labels
/// where that is tolerable; a review dialog asking the user to judge what the game
/// did cannot.
/// </para>
/// </summary>
public sealed record OrderDiff(
    ImmutableArray<OrderInsert> Inserted,
    ImmutableArray<OrderRemove> Removed,
    ImmutableArray<OrderMove> Moved,
    int UnchangedCount)
{
    public bool IsIdentical =>
        Inserted.IsDefaultOrEmpty && Removed.IsDefaultOrEmpty && Moved.IsDefaultOrEmpty;

    /// <summary>
    /// <paramref name="yours"/> is the list on screen; <paramref name="theirs"/> is
    /// what the game holds. Duplicate ids keep their first occurrence — a corrupt
    /// ModsConfig must degrade to a smaller diff, never a crash.
    /// </summary>
    public static OrderDiff Between(IReadOnlyList<ModId> yours, IReadOnlyList<ModId> theirs)
    {
        ArgumentNullException.ThrowIfNull(yours);
        ArgumentNullException.ThrowIfNull(theirs);

        var yoursOrder = Dedupe(yours);
        var theirsOrder = Dedupe(theirs);
        var yoursPos = Positions(yoursOrder);
        var theirsPos = Positions(theirsOrder);

        var inserted = theirsOrder.Where(id => !yoursPos.ContainsKey(id))
            .Select(id => new OrderInsert(id, theirsPos[id]))
            .ToImmutableArray();
        var removed = yoursOrder.Where(id => !theirsPos.ContainsKey(id))
            .Select(id => new OrderRemove(id, yoursPos[id]))
            .ToImmutableArray();

        // The LCS runs over the COMMON subset only — one-sided rows can never anchor
        // anything, and filtering first keeps the DP quadratic in the common count.
        var yoursCommon = yoursOrder.Where(id => theirsPos.ContainsKey(id)).ToList();
        var theirsCommon = theirsOrder.Where(id => yoursPos.ContainsKey(id)).ToList();
        var anchored = LongestCommonSubsequence(yoursCommon, theirsCommon);

        var moved = theirsCommon
            .Where(id => !anchored.Contains(id))
            .Select(id => new OrderMove(id, yoursPos[id], theirsPos[id]))
            .ToImmutableArray();

        return new OrderDiff(inserted, removed, moved, anchored.Count);
    }

    private static List<ModId> Dedupe(IReadOnlyList<ModId> order)
    {
        var seen = new HashSet<ModId>();
        var result = new List<ModId>(order.Count);
        foreach (var id in order)
        {
            if (seen.Add(id)) result.Add(id);
        }
        return result;
    }

    /// <summary>1-based, matching the “yours #4 → theirs #1” grammar on screen.</summary>
    private static Dictionary<ModId, int> Positions(List<ModId> order)
    {
        var positions = new Dictionary<ModId, int>(order.Count);
        for (var i = 0; i < order.Count; i++) positions[order[i]] = i + 1;
        return positions;
    }

    /// <summary>Classic O(n·m) DP with a deterministic backtrack.</summary>
    private static HashSet<ModId> LongestCommonSubsequence(List<ModId> a, List<ModId> b)
    {
        var lengths = new int[a.Count + 1, b.Count + 1];
        for (var i = 0; i < a.Count; i++)
        {
            for (var j = 0; j < b.Count; j++)
            {
                lengths[i + 1, j + 1] = a[i] == b[j]
                    ? lengths[i, j] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var anchored = new HashSet<ModId>();
        var (x, y) = (a.Count, b.Count);
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                anchored.Add(a[x - 1]);
                x--;
                y--;
            }
            else if (lengths[x - 1, y] >= lengths[x, y - 1]) x--;
            else y--;
        }

        return anchored;
    }
}
