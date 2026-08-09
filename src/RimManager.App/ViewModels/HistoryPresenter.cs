using System.Collections.Immutable;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>The History filter chips (<c>2d</c>).</summary>
public enum HistoryFilter
{
    All,
    AppliedOnly,
    Named,
}

/// <summary>One row of the History master list (<c>2d</c>).</summary>
public sealed record SnapshotEntry(
    int Number,
    string When,
    string Action,
    string Detail,
    string Change,
    string Size,
    bool IsApplied,
    bool IsNamed,
    ModlistSnapshot Snapshot)
{
    public string NumberText => Number.ToString();

    public bool HasDetail => Detail.Length > 0;

    /// <summary>A named state gets the ★ and is exempt from pruning.</summary>
    public bool IsProtected => Snapshot.IsProtected;
}

/// <summary>A group of diff lines in the detail panel, e.g. "moves", "added".</summary>
public sealed record HistoryChange(string Glyph, string From, string Name, string Delta, bool IsMove);

public sealed record HistoryDetail(
    string Title,
    string Paragraph,
    ImmutableArray<HistoryChange> Changes,
    int Hidden,
    string Mods,
    string Game,
    string Applied,
    string Rules)
{
    public static readonly HistoryDetail None =
        new(string.Empty, string.Empty, [], 0, string.Empty, string.Empty, string.Empty, string.Empty);

    public bool HasHidden => Hidden > 0;

    public string HiddenText => $"{Hidden} more moves";
}

/// <summary>
/// Avalonia-free presentation of the History tab (<c>2d</c>): the row projection, the
/// filter chips and the grouped diff. Kept out of the view-model so the wording and the
/// counts are testable — a "change" column that quietly disagrees with the diff beside
/// it is the sort of thing only a careful read catches.
/// </summary>
public static class HistoryPresenter
{
    /// <summary>
    /// How many change lines the detail panel shows before collapsing the rest. A sort
    /// moves most of the list; 71 rows of "moved" is not a diff, it is a wall.
    /// </summary>
    public const int MaxChangeLines = 8;

    /// <summary>
    /// Builds the rows, newest first, numbering oldest-to-newest so the numbers are
    /// stable as history grows. Each row's CHANGE column is the diff against the
    /// snapshot immediately before it, which is what "what did this step do" means.
    /// </summary>
    public static ImmutableArray<SnapshotEntry> BuildRows(
        IReadOnlyList<ModlistSnapshot> newestFirst,
        IReadOnlyDictionary<string, long> sizes,
        DateTimeOffset now)
    {
        var rows = ImmutableArray.CreateBuilder<SnapshotEntry>(newestFirst.Count);

        for (var i = 0; i < newestFirst.Count; i++)
        {
            var snapshot = newestFirst[i];
            var previous = i + 1 < newestFirst.Count ? newestFirst[i + 1] : null;
            var diff = previous is null
                ? null
                : ProfileDiff.Between(previous.State, snapshot.State);

            var (action, detail) = Describe(snapshot);
            rows.Add(new SnapshotEntry(
                newestFirst.Count - i,
                When(snapshot.TakenUtc, now),
                action,
                detail,
                Change(diff),
                Size(sizes.TryGetValue(snapshot.Id, out var bytes) ? bytes : null),
                IsApplied: snapshot.Reason.Contains("apply", StringComparison.OrdinalIgnoreCase),
                IsNamed: !string.IsNullOrWhiteSpace(snapshot.Name),
                snapshot));
        }

        return rows.ToImmutable();
    }

    public static ImmutableArray<SnapshotEntry> Filter(
        ImmutableArray<SnapshotEntry> rows, HistoryFilter filter) => filter switch
    {
        HistoryFilter.AppliedOnly => [.. rows.Where(r => r.IsApplied)],
        HistoryFilter.Named => [.. rows.Where(r => r.IsNamed)],
        _ => rows,
    };

    /// <summary>"12:06 today", "yesterday", "Jul 24" — recency, then a date.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;

        if (local.Date == today) return $"{local:HH:mm} today";
        if (local.Date == today.AddDays(-1)) return "yesterday";
        return local.Year == today.Year ? $"{local:MMM d}" : $"{local:MMM d yyyy}";
    }

    /// <summary>
    /// The action column: the user's name for the state if they gave one, else a
    /// plain-language reading of why the snapshot was taken.
    /// </summary>
    private static (string Action, string Detail) Describe(ModlistSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Name)) return (snapshot.Name!, "named");

        var reason = snapshot.Reason.ToLowerInvariant();

        // Matched by PREFIX, because some reasons carry a payload: the restore path
        // writes "restored <snapshotId>", and switching on the whole string put a raw
        // 27-character id in the widest column of the table.
        if (reason.StartsWith("restored", StringComparison.Ordinal))
            return ("Restored a state", "created as new state");

        if (reason.StartsWith("pre-restore", StringComparison.Ordinal))
            return ("Before restoring", "taken automatically");

        return reason switch
        {
            "apply" or "applied" => ("Applied to game", "ModsConfig.xml written"),
            "pre-sort" or "sort" => ("Sort", "topological + community rules"),
            "import" => ("Imported collection", string.Empty),
            "drag" => ("Reordered by hand", string.Empty),
            "activate" => ("Activated mods", "from Inactive"),
            "deactivate" => ("Deactivated mods", string.Empty),
            "separator" => ("Edited a separator", string.Empty),
            "manual" => ("Edited the load order", string.Empty),
            "" => ("Snapshot", string.Empty),
            var other => (char.ToUpperInvariant(other[0]) + other[1..], string.Empty),
        };
    }

    /// <summary>"+3 −1 · 4 moved", "±0 · 71 moved", or an em dash when nothing changed.</summary>
    public static string Change(ProfileDiff? diff)
    {
        if (diff is null) return "—";

        var added = diff.Added.IsDefaultOrEmpty ? 0 : diff.Added.Length;
        var removed = diff.Removed.IsDefaultOrEmpty ? 0 : diff.Removed.Length;
        var moved = diff.Moved.IsDefaultOrEmpty ? 0 : diff.Moved.Length;
        if (added == 0 && removed == 0 && moved == 0) return "—";

        var counts = added == 0 && removed == 0
            ? "±0"
            : $"{(added > 0 ? $"+{added}" : string.Empty)}{(removed > 0 ? $" −{removed}" : string.Empty)}".Trim();

        return moved > 0 ? $"{counts} · {moved} moved" : counts;
    }

    public static string Size(long? bytes) => bytes switch
    {
        null or <= 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    /// <summary>"48 snapshots · 3.1 MB" for the toolbar.</summary>
    public static string Total(int count, IReadOnlyDictionary<string, long> sizes)
    {
        var bytes = sizes.Values.Sum();
        return $"{count} snapshot{(count == 1 ? "" : "s")} · {Size(bytes)}";
    }

    /// <summary>
    /// The detail panel: a plain-language sentence about the step, then the change
    /// lines grouped by kind with moves first — a move carries a delta, which is the
    /// part a raw list of names cannot tell you.
    /// </summary>
    public static HistoryDetail BuildDetail(
        SnapshotEntry? entry,
        IReadOnlyList<ModlistSnapshot> newestFirst,
        IReadOnlyDictionary<ModId, string> names,
        string gameVersion,
        string rules,
        bool showAll = false)
    {
        if (entry is null) return HistoryDetail.None;

        var index = newestFirst.ToList().FindIndex(s => s.Id == entry.Snapshot.Id);
        var previous = index >= 0 && index + 1 < newestFirst.Count ? newestFirst[index + 1] : null;

        var changes = ImmutableArray.CreateBuilder<HistoryChange>();
        var hidden = 0;
        string paragraph;

        if (previous is null)
        {
            paragraph = "The first recorded state for this modlist — there is nothing before it to compare against.";
        }
        else
        {
            var diff = ProfileDiff.Between(previous.State, entry.Snapshot.State);
            paragraph = Narrate(diff);

            var moves = diff.Moved.IsDefaultOrEmpty ? [] : diff.Moved;
            var shown = showAll ? moves.Length : Math.Min(moves.Length, MaxChangeLines);
            hidden = moves.Length - shown;

            for (var i = 0; i < shown; i++)
            {
                var move = moves[i];
                var delta = move.ToIndex - move.FromIndex;
                changes.Add(new HistoryChange(
                    "↕",
                    $"{move.FromIndex + 1}→{move.ToIndex + 1}",
                    NameOf(move.Id, names),
                    delta > 0 ? $"▼{delta}" : $"▲{-delta}",
                    IsMove: true));
            }

            foreach (var id in diff.Added.IsDefaultOrEmpty ? [] : diff.Added)
                changes.Add(new HistoryChange("+", string.Empty, NameOf(id, names), "added", IsMove: false));

            foreach (var id in diff.Removed.IsDefaultOrEmpty ? [] : diff.Removed)
                changes.Add(new HistoryChange("−", string.Empty, NameOf(id, names), "removed", IsMove: false));
        }

        var mods = entry.Snapshot.State.Entries.Count(e => e.Kind == ModlistEntryKind.Mod);
        return new HistoryDetail(
            previous is null ? $"Snapshot #{entry.Number}" : $"#{entry.Number - 1} → #{entry.Number}",
            paragraph,
            changes.ToImmutable(),
            hidden,
            $"{mods} active",
            gameVersion,
            entry.IsApplied ? "yes — written to ModsConfig.xml" : "no — current draft",
            rules);
    }

    private static string Narrate(ProfileDiff diff)
    {
        if (diff.IsIdentical) return "Nothing changed between these two states.";

        var parts = new List<string>();
        var moved = diff.Moved.IsDefaultOrEmpty ? 0 : diff.Moved.Length;
        var added = diff.Added.IsDefaultOrEmpty ? 0 : diff.Added.Length;
        var removed = diff.Removed.IsDefaultOrEmpty ? 0 : diff.Removed.Length;

        if (moved > 0) parts.Add($"moved {moved} mod{(moved == 1 ? "" : "s")}");
        if (added > 0) parts.Add($"added {added}");
        if (removed > 0) parts.Add($"removed {removed}");

        var sentence = "This step " + string.Join(", ", parts) + ".";
        return added == 0 && removed == 0
            ? sentence + " No mod was added or removed."
            : sentence;
    }

    private static string NameOf(ModId id, IReadOnlyDictionary<ModId, string> names) =>
        names.TryGetValue(id, out var name) ? name : id.Display;
}
