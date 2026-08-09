using System.Collections.Immutable;

namespace RimManager.App.ViewModels;

/// <summary>
/// What the inactive pane can be sorted by.
/// <para>
/// Only the inactive pane sorts. Non-negotiable #3: the active list <em>is</em> the
/// load order, so a sorted view of it would be a mode you can enter and forget, and
/// eventually receive a drag. The inactive pane is a set rather than a sequence, so
/// ordering it costs nothing and finding things in 168 rows costs a lot without it.
/// </para>
/// </summary>
public enum InactiveSortKey
{
    Name,
    Source,
    Version,
    Author,

    /// <summary>
    /// Added with the click-to-sort headers (N1). Every column the pane draws must be
    /// clickable, or the one that is not reads as broken rather than as deliberate.
    /// </summary>
    PackageId,
}

/// <summary>
/// Ordering for the inactive pane. Pure, because "sorted" is the kind of claim a
/// screenshot cannot check: a list that is nearly sorted looks exactly like one that
/// is, and the tie-breaking is what keeps it stable across rebuilds.
/// </summary>
public static class InactiveSort
{
    public static string Label(InactiveSortKey key) => key switch
    {
        InactiveSortKey.Source => "Source",
        InactiveSortKey.Version => "Version",
        InactiveSortKey.Author => "Author",
        InactiveSortKey.PackageId => "PackageId",
        _ => "Name",
    };

    /// <summary>
    /// A column heading, carrying the direction arrow when it is the sorted column.
    /// <para>
    /// The arrow is baked into the STRING rather than drawn as a second element beside
    /// the label. A heading sits in a table cell — NAME's is the flexible one — and both
    /// a horizontal <c>StackPanel</c> and a Grid <c>Auto</c> column measure their
    /// children at infinite width, which is exactly the trap <c>LayoutTrapTests</c>
    /// exists for: <c>TextTrimming</c> never engages and the text paints over the next
    /// column. One TextBlock cannot do that, and the string is unit-testable besides.
    /// </para>
    /// </summary>
    public static string Header(InactiveSortKey column, InactiveSortKey sortedBy, bool ascending)
    {
        var caption = Caption(column);
        return column == sortedBy ? $"{caption} {(ascending ? "▲" : "▼")}" : caption;
    }

    /// <summary>
    /// The heading as drawn: upper case, and shortened where the design's column is
    /// narrower than the word. SRC and VER match the active pane's legend exactly,
    /// because two panes calling one column two names is worse than an abbreviation.
    /// </summary>
    private static string Caption(InactiveSortKey key) => key switch
    {
        InactiveSortKey.Source => "SRC",
        InactiveSortKey.Version => "VER",
        InactiveSortKey.Author => "AUTHOR",
        InactiveSortKey.PackageId => "PACKAGEID",
        _ => "NAME",
    };

    /// <summary>
    /// Sorts a copy. Every key falls back to the name, so the order is total and a
    /// rebuild cannot shuffle equal rows — the pane would otherwise reorder itself
    /// under the pointer every time a scan refreshed it.
    /// </summary>
    public static ImmutableArray<RowViewModel> Apply(
        IEnumerable<RowViewModel> rows, InactiveSortKey key, bool ascending)
    {
        var mods = rows.OfType<ModRowViewModel>();

        IOrderedEnumerable<ModRowViewModel> ordered = key switch
        {
            // SourceLabel, not Source. Source became the badge's TOOLTIP when the badge
            // became a wordless icon ("Workshop — subscribed through Steam"), and a
            // column must sort by what it DISPLAYS, not by a sentence describing it.
            InactiveSortKey.Source => Order(mods, r => r.SourceLabel, ascending),
            InactiveSortKey.Version => Order(mods, r => r.Version, ascending),
            InactiveSortKey.Author => Order(mods, r => r.Author, ascending),
            InactiveSortKey.PackageId => Order(mods, r => r.PackageIdText, ascending),
            _ => Order(mods, r => r.Name, ascending),
        };

        return [.. ordered.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                          .ThenBy(r => r.PackageIdText, StringComparer.OrdinalIgnoreCase)];
    }

    private static IOrderedEnumerable<ModRowViewModel> Order(
        IEnumerable<ModRowViewModel> rows, Func<ModRowViewModel, string> key, bool ascending) =>
        ascending
            ? rows.OrderBy(key, StringComparer.OrdinalIgnoreCase)
            : rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase);
}
