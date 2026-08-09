using RimManager.Core.Domain;
namespace RimManager.App.ViewModels;

/// <summary>
/// Pure operations on the active list (mods + separators). Kept free of Avalonia
/// so the fiddly grouping/renumber logic is unit-testable.
/// </summary>
public static class ActiveListOps
{
    /// <summary>Numbers mod rows 1..n (separators get 0) and recomputes each separator's group count.</summary>
    public static void Renumber(IReadOnlyList<RowViewModel> rows)
    {
        int n = 1;
        foreach (var row in rows)
        {
            row.Index = row is ModRowViewModel ? n++ : 0;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is SeparatorRowViewModel sep)
            {
                var (_, length) = GroupExtent(rows, i);
                sep.ModCount = length - 1; // extent includes the separator itself
            }
        }
    }

    /// <summary>
    /// Where a new separator goes: <b>above the selection</b>, or at the top when nothing
    /// in this pane is selected.
    /// <para>
    /// Above rather than below, because a separator owns the rows <em>after</em> it — so
    /// "insert above what I selected" is the only reading under which the rows you had
    /// just picked end up inside the group you are about to name. Inserting below would
    /// hand the new group everything <em>except</em> the selection.
    /// </para>
    /// <para>
    /// The <b>topmost</b> selected row decides, not the first one clicked: a multi-row
    /// selection has no inherent order, and a group that starts halfway down its own
    /// selection is not a group anybody asked for.
    /// </para>
    /// </summary>
    /// <param name="selection">
    /// May contain rows from the other pane — the two lists keep independent selections —
    /// so rows that are not in <paramref name="rows"/> are ignored rather than trusted.
    /// </param>
    public static int SeparatorInsertIndex(
        IReadOnlyList<RowViewModel> rows, IEnumerable<RowViewModel> selection)
    {
        var top = rows.Count;

        // Reference identity, through the file's existing IndexOf: a row is a view model,
        // and two separators can carry the same name.
        foreach (var row in selection)
        {
            var index = IndexOf(rows, row);
            if (index >= 0 && index < top) top = index;
        }

        return top == rows.Count ? 0 : top;
    }

    /// <summary>
    /// The span a separator "owns": the separator itself plus the contiguous mod rows
    /// after it, up to the next separator or the end. Returns (startIndex, length).
    /// </summary>
    public static (int start, int length) GroupExtent(IReadOnlyList<RowViewModel> rows, int separatorIndex)
    {
        int j = separatorIndex + 1;
        while (j < rows.Count && rows[j] is ModRowViewModel) j++;
        return (separatorIndex, j - separatorIndex);
    }

    /// <summary>
    /// O9 · hides a separator whose whole group has been filtered out, and shows one
    /// whose group still has a survivor. Run AFTER the mods have been filtered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter's one assignment is <c>row.IsFilteredOut = row is ModRowViewModel mod
    /// &amp;&amp; !Matches(...)</c>, so a separator fails the type test and is actively
    /// UN-hidden on every pass. Searching for a mod therefore left every group heading on
    /// screen, including headings over nothing.
    /// </para>
    /// <para>
    /// It is not only untidy. <c>EmptyStatePresenter</c> short-circuits on
    /// <c>visibleRows > 0</c> and separators counted, so the active pane's "Nothing
    /// matches these filters" card was <b>unreachable on any list containing a
    /// separator</b> — the pane showed a column of empty headings instead of saying
    /// nothing matched.
    /// </para>
    /// <para>
    /// A separator is judged on its own group only, and a group is the contiguous mods
    /// after it (<see cref="GroupExtent"/>) — the same positional rule collapse and the
    /// group count already use. While a filter runs, a heading earns its place by having
    /// something under it.
    /// </para>
    /// <para>
    /// <paramref name="filtering"/> is what stops that rule reaching further than it
    /// should: with no filter on, an EMPTY group is not a failed match, it is a separator
    /// the user has just made and is about to drag mods into. Hiding it would make the
    /// act of creating one look like it failed.
    /// </para>
    /// </remarks>
    public static void ApplySeparatorVisibility(IReadOnlyList<RowViewModel> rows, bool filtering)
    {
        ArgumentNullException.ThrowIfNull(rows);

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not SeparatorRowViewModel sep) continue;

            if (!filtering) { sep.IsFilteredOut = false; continue; }

            var (_, length) = GroupExtent(rows, i);
            var anySurvivor = false;

            for (var j = i + 1; j < i + length; j++)
            {
                // IsFilteredOut, not IsRowVisible: a COLLAPSED group's mods are hidden
                // too, and a collapsed separator must stay on screen — it is the only
                // thing left to click to get them back.
                if (rows[j] is ModRowViewModel { IsFilteredOut: false }) { anySurvivor = true; break; }
            }

            sep.IsFilteredOut = !anySurvivor;
        }
    }

    /// <summary>
    /// O9 · where an activated mod joins the active list: the end, or — while a filter is
    /// running — immediately after the last mod the user can actually see.
    /// </summary>
    /// <remarks>
    /// Appending to the true end is right with no filter and wrong with one: the end is
    /// then below a run of hidden rows and off screen, so pressing Activate made the mod
    /// disappear. After the last visible mod it lands where the user was looking, and
    /// "the last one by load order" rather than "the last one on screen" is the same row
    /// — the pane never reorders.
    /// </remarks>
    public static int ActivationIndex(IReadOnlyList<RowViewModel> rows, bool filtering)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!filtering) return rows.Count;

        for (var i = rows.Count - 1; i >= 0; i--)
            if (rows[i] is ModRowViewModel { IsRowVisible: true }) return i + 1;

        // Nothing visible to sit after: the end is as good an answer as exists, and it is
        // the one the unfiltered case gives.
        return rows.Count;
    }

    /// <summary>Sets a separator's collapsed state and hides/shows its child mod rows.</summary>
    public static void ApplyCollapsed(IReadOnlyList<RowViewModel> rows, SeparatorRowViewModel sep, bool collapsed)
    {
        int idx = IndexOf(rows, sep);
        if (idx < 0) return;

        sep.Collapsed = collapsed;
        var (start, length) = GroupExtent(rows, idx);
        for (int i = start + 1; i < start + length; i++)
        {
            rows[i].IsCollapsedChild = collapsed;
        }
    }

    /// <summary>The mod rows a separator owns (its group), in order.</summary>
    public static IReadOnlyList<ModRowViewModel> GroupMods(IReadOnlyList<RowViewModel> rows, SeparatorRowViewModel sep)
    {
        int idx = IndexOf(rows, sep);
        if (idx < 0) return [];

        var (start, length) = GroupExtent(rows, idx);
        var mods = new List<ModRowViewModel>();
        for (int i = start + 1; i < start + length; i++)
        {
            if (rows[i] is ModRowViewModel mod) mods.Add(mod);
        }

        return mods;
    }

    private static int IndexOf(IReadOnlyList<RowViewModel> rows, RowViewModel row)
    {
        for (int i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i], row)) return i;
        return -1;
    }

    /// <summary>
    /// Why dropping <paramref name="dragged"/> at <paramref name="index"/> is refused,
    /// or null when it is fine.
    /// <para>
    /// This used to refuse ANY drop above the last Core/DLC row, on the stated grounds
    /// that "RimWorld will not load with a mod above them". <b>That is false.</b> The
    /// developer's own live <c>ModsConfig.xml</c> — the file the game itself reads —
    /// has Prepatcher, Harmony, Loading Progress and Better Stacktraces above
    /// <c>ludeon.rimworld</c> in a working install, and our own sorter puts them there.
    /// The app was refusing by hand the order it produces by itself.
    /// </para>
    /// <para>
    /// What IS true is narrower: the base game and its expansions keep their own order.
    /// Ludeon pins it with <c>forceLoadAfter</c>/<c>forceLoadBefore</c>, and the sorter
    /// restores it on every run, so dragging one out of line achieves nothing but a
    /// warning. Everything else is the user's to arrange — warnings report a bad order,
    /// they do not forbid one.
    /// </para>
    /// <para>
    /// Pure and here rather than in the window, because a drop rule that refuses
    /// everything and one that refuses nothing look identical until someone drags.
    /// </para>
    /// </summary>
    public static string? InvalidDropReason(
        IReadOnlyList<RowViewModel> rows, int index, RowViewModel? dragged)
    {
        if (dragged is not ModRowViewModel { Mod.Source: ModSource.Core or ModSource.Dlc })
            return null;

        int first = int.MaxValue, last = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is ModRowViewModel { Mod.Source: ModSource.Core or ModSource.Dlc })
            {
                first = System.Math.Min(first, i);
                last = i;
            }
        }

        if (last < 0) return null;

        // Inside the game's own block, or immediately after it, is a no-op or a shuffle
        // within it. Anywhere else takes an expansion away from the game it belongs to.
        return index >= first && index <= last + 1
            ? null
            : "The base game and its expansions keep their own order";
    }

    /// <summary>
    /// True when dropping <paramref name="row"/> at <paramref name="dropIndex"/> —
    /// an insertion point in <paramref name="source"/>'s OWN index space — lands it
    /// exactly where it already is (3a: a drag that ends where it started produces no
    /// undo entry, no snapshot, no status line).
    /// <para>
    /// One space, deliberately: the old guard compared the drop index against
    /// <c>row.Index</c>, the DISPLAYED number, and the two disagree by one for every
    /// separator above the row — so dragging a mod one position up matched the guard
    /// and silently did nothing, on any list with separators, which is every real
    /// list. The same collision no-opped the keyboard nudge a commit earlier.
    /// </para>
    /// </summary>
    public static bool IsSameSpotDrop(
        IReadOnlyList<RowViewModel> source, RowViewModel row, int dropIndex)
    {
        var at = -1;
        for (var i = 0; i < source.Count; i++)
        {
            if (ReferenceEquals(source[i], row)) { at = i; break; }
        }

        return at >= 0 && (dropIndex == at || dropIndex == at + 1);
    }
}
