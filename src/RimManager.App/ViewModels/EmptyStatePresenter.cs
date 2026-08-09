using System.Collections.Generic;
using System.Linq;

namespace RimManager.App.ViewModels;

/// <summary>
/// Which empty state a list should show, if any (3e). Three shapes, never a bare
/// "No items":
/// <list type="bullet">
/// <item><b>Success</b> — everything is active / nothing is wrong. Confirms what was
/// checked and offers a re-check.</item>
/// <item><b>NotYet</b> — nothing to show because a step has not run. States the COST
/// of that step and why it is not automatic, then offers it.</item>
/// <item><b>FilteredOut</b> — there is data, a filter is hiding it. Names every
/// filter currently narrowing the view, including ones the user has forgotten.</item>
/// </list>
/// </summary>
public enum EmptyState
{
    /// <summary>The list has rows; show nothing.</summary>
    None,
    Success,
    NotYet,
    FilteredOut,
}

/// <summary>
/// Decides a list pane's empty state. Pure, so the "which of six" logic is testable
/// without a UI — the states are easy to get subtly wrong (showing "everything is
/// active" when a filter is simply hiding everything is actively misleading).
/// </summary>
public static class EmptyStatePresenter
{
    /// <param name="totalRows">Rows in the pane before filtering.</param>
    /// <param name="visibleRows">Rows still visible after search and filters.</param>
    /// <param name="anyModsInstalled">Whether the scan found anything at all.</param>
    public static EmptyState For(int totalRows, int visibleRows, bool anyModsInstalled)
    {
        if (visibleRows > 0) return EmptyState.None;

        // Nothing scanned at all is a "not yet", not a success: telling someone
        // everything is fine when the app has not found their mods is a lie.
        if (!anyModsInstalled) return EmptyState.NotYet;

        // Rows exist but none survive the filters — say so, and name them. Reporting
        // this as success is the most misleading thing an empty state can do.
        return totalRows > 0 ? EmptyState.FilteredOut : EmptyState.Success;
    }

    /// <summary>
    /// Names every filter currently narrowing the view (3e). Includes ones the user
    /// may have forgotten — a stale tag filter from ten minutes ago is exactly the
    /// case this text exists for.
    /// </summary>
    public static string DescribeFilters(
        string? search, bool warningsOnly, int tagFilters)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search \u201c{search}\u201d");
        if (tagFilters == 1) parts.Add("1 tag filter");
        if (tagFilters > 1) parts.Add($"{tagFilters} tag filters");
        if (warningsOnly) parts.Add("Warnings");

        return parts.Count switch
        {
            0 => string.Empty,
            1 => $"{parts[0]} is narrowing this list.",
            _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]} are narrowing this list.",
        };
    }
}
