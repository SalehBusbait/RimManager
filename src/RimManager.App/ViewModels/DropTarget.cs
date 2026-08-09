namespace RimManager.App.ViewModels;

/// <summary>One row as the drop arithmetic sees it: where it sits, and whether it is on
/// screen at all.</summary>
/// <param name="Index">Its index in the underlying collection — the space the move uses.</param>
/// <param name="Top">Top edge in list coordinates.</param>
/// <param name="Height">Rendered height.</param>
/// <param name="Visible">False for a row the filter (or a collapsed separator) is hiding.</param>
public readonly record struct DropRow(int Index, double Top, double Height, bool Visible);

/// <summary>
/// Where a drop lands (O9). Pure, because this is the arithmetic that can silently
/// reorder a load order and it had no test of any kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hidden rows must be excluded by the caller's own visibility flag, never by their
/// geometry.</b> Avalonia's <c>Layoutable.ArrangeCore</c> is wrapped in <c>if (IsVisible)</c>
/// with no else branch, so a container that is hidden after being arranged keeps its
/// PRE-FILTER rectangle for ever, and one realized while already hidden keeps
/// <c>0,0,0,0</c>. Neither is "zero height where the row would be". The previous
/// implementation read <c>Bounds</c> off every realized container and took the MINIMUM
/// qualifying index, so a single stale rectangle outranked every visible row: typing a
/// filter and then dragging could move a mod to the top of the list while the insertion
/// line was drawn exactly where the user aimed.
/// </para>
/// <para>
/// <b>The rule: a drop lands immediately after the visible row above it.</b> With mods at
/// 12, 23 and 40 visible and everything between them hidden, dropping in the gap under 12
/// gives 13 — adjacent to the row the user can actually see, not after ten rows they
/// cannot. When nothing is hidden this is exactly the old behaviour: the gap between
/// adjacent rows i and i+1 anchors on i and returns i+1.
/// </para>
/// </remarks>
public static class DropTarget
{
    /// <summary>
    /// The insertion index in the underlying collection, or <paramref name="fallback"/>
    /// when no row is visible to aim at.
    /// </summary>
    public static int For(IReadOnlyList<DropRow> rows, double pointerY, int fallback)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var anchor = -1;      // the last visible row whose midpoint is above the pointer
        var first = -1;       // the topmost visible row

        foreach (var row in rows)
        {
            if (!row.Visible) continue;
            if (first < 0 || row.Index < first) first = row.Index;

            var midpoint = row.Top + (row.Height / 2);
            if (pointerY >= midpoint && row.Index > anchor) anchor = row.Index;
        }

        if (first < 0) return fallback;

        // Above every visible midpoint: land on the topmost visible row's own index, so
        // the mod renders immediately above it.
        //
        // The plan's worked example says 11 for a topmost visible row of 12 — one place
        // FURTHER up. That is not adopted, and the reason is that it cannot be confined
        // to the filtered case: with no filter the "topmost visible row" is whatever the
        // pointer is above, so 'index - 1' would change every ordinary drop too, moving
        // rows one place higher than the indicator promises. Adjacent-to-what-you-can-see
        // is the same answer in both cases, which is the property worth having.
        if (anchor < 0) return first;

        return anchor + 1;
    }
}
