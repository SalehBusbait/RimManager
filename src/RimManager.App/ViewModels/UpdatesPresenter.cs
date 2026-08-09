using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>The header checkbox's three states (<c>2b</c>).</summary>
public enum TriState
{
    None,
    Some,
    All,
}

/// <summary>
/// Avalonia-free presentation logic for the Updates panel (<c>2b</c>): ordering, the
/// summary line, the safe-set rule behind the tri-state header checkbox, and the
/// column formatting. Kept out of the view-model (like <see cref="RowFilter"/> /
/// <see cref="ActiveListOps"/>) so it is unit-tested without a UI.
/// </summary>
public static class UpdatesPresenter
{
    /// <summary>Display order: available updates first, then delisted, then not-tracked,
    /// then up-to-date; ties broken by mod name (case-insensitive).</summary>
    public static ImmutableArray<ModUpdateStatus> Order(IEnumerable<ModUpdateStatus> statuses) =>
        [.. statuses
            .OrderBy(Priority)
            .ThenBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)];

    private static int Priority(ModUpdateStatus s) => s.Status switch
    {
        UpdateStatus.UpdateAvailable => 0,
        UpdateStatus.Delisted => 1,
        UpdateStatus.NotTracked => 2,
        _ => 3, // UpToDate
    };

    /// <summary>One-line summary, e.g. "7 updates · 3 delisted · 189 up to date · 2 untracked".
    /// Omits any zero categories; "All up to date" when nothing needs attention.</summary>
    public static string Summarize(IReadOnlyCollection<ModUpdateStatus> statuses)
    {
        if (statuses.Count == 0) return "No Workshop mods to check.";

        int updates = statuses.Count(s => s.Status == UpdateStatus.UpdateAvailable);
        int delisted = statuses.Count(s => s.Status == UpdateStatus.Delisted);
        int upToDate = statuses.Count(s => s.Status == UpdateStatus.UpToDate);
        int untracked = statuses.Count(s => s.Status == UpdateStatus.NotTracked);

        var parts = new List<string>();
        if (updates > 0) parts.Add($"{updates} update{(updates == 1 ? "" : "s")}");
        if (delisted > 0) parts.Add($"{delisted} delisted");
        if (upToDate > 0) parts.Add($"{upToDate} up to date");
        if (untracked > 0) parts.Add($"{untracked} untracked");

        return updates == 0 && delisted == 0
            ? $"All up to date ({upToDate + untracked} checked)."
            : string.Join(" · ", parts);
    }

    /// <summary>
    /// Whether a status earns a row at all.
    /// <para>
    /// <c>2b</c>'s table is short — six rows against a header reading "Updates 7". It
    /// is a worklist, not an inventory: a real install checks ~365 mods and 344 of them
    /// have nothing to say, and burying the 21 that do under them is exactly the tab's
    /// job undone. The totals still get stated, in the summary line.
    /// </para>
    /// <para>
    /// Snoozed rows stay: the user asked to be quiet about them, not to lose them, and
    /// un-snoozing has to be reachable from somewhere.
    /// </para>
    /// </summary>
    public static bool IsWorthShowing(ModUpdateStatus status, bool isSnoozed) =>
        isSnoozed
        || status.Status is UpdateStatus.UpdateAvailable or UpdateStatus.Delisted;

    /// <summary>
    /// The header checkbox "only ever selects the safe set — never a pre-release, never
    /// a mod with uncommitted local edits" (<c>2b</c>). Those rows have to be ticked
    /// deliberately, one at a time.
    /// <para>
    /// The rule lives here, not in the checkbox handler, because the failure it guards
    /// against is silent: a "select all" that quietly swept up a release candidate or
    /// overwrote someone's uncommitted edits looks identical to one that did not.
    /// </para>
    /// </summary>
    public static bool IsSafeToBatch(UpdateRowViewModel row) =>
        row is { IsUpdate: true, IsSnoozed: false, IsPreRelease: false, HasLocalEdits: false };

    /// <summary>
    /// What the header checkbox shows: All when every safe row is ticked, None when
    /// none is, Some otherwise. A row the user ticked by hand that is <em>not</em> safe
    /// never makes the header read All — the header only ever speaks for the safe set.
    /// </summary>
    public static TriState HeaderState(IEnumerable<UpdateRowViewModel> rows)
    {
        var safe = rows.Where(IsSafeToBatch).ToList();
        if (safe.Count == 0) return TriState.None;

        var ticked = safe.Count(r => r.IsSelected);
        return ticked == 0 ? TriState.None
             : ticked == safe.Count ? TriState.All
             : TriState.Some;
    }

    /// <summary>"3 of 7 selected" — M counts only rows that could be updated at all.</summary>
    public static string SelectionSummary(IEnumerable<UpdateRowViewModel> rows)
    {
        var all = rows.ToList();
        return $"{all.Count(r => r.IsSelected)} of {all.Count(r => r.IsUpdate)} selected";
    }

    /// <summary>
    /// "2 days ago", "yesterday", "4 hours ago". A relative date is what the PUBLISHED
    /// column is for: the question it answers is "is this recent", not "what day was
    /// it" — the exact timestamp rides in the tooltip.
    /// </summary>
    public static string Published(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when) return "—";

        var span = now - when;
        if (span.TotalMinutes < 60) return "just now";
        if (span.TotalHours < 24)
        {
            var hours = (int)span.TotalHours;
            return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
        }

        var days = (int)span.TotalDays;
        return days switch
        {
            1 => "yesterday",
            < 7 => $"{days} days ago",
            < 14 => "a week ago",
            < 31 => $"{days / 7} weeks ago",
            < 365 => $"{days / 31} month{(days / 31 == 1 ? "" : "s")} ago",
            _ => $"{days / 365} year{(days / 365 == 1 ? "" : "s")} ago",
        };
    }

    /// <summary>Steam's own units: MB to one decimal, KB below that.</summary>
    public static string Size(long? bytes) => bytes switch
    {
        null or < 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>
    /// A version that names itself unfinished. Steam has no pre-release concept, so
    /// this reads the author's own suffix — the convention every package ecosystem
    /// uses, and the only signal there is.
    /// </summary>
    public static bool LooksPreRelease(string? version) =>
        version is not null
        && (version.Contains("-rc", StringComparison.OrdinalIgnoreCase)
            || version.Contains("-beta", StringComparison.OrdinalIgnoreCase)
            || version.Contains("-alpha", StringComparison.OrdinalIgnoreCase)
            || version.Contains("-pre", StringComparison.OrdinalIgnoreCase));
}
