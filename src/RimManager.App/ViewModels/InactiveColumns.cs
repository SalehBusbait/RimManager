namespace RimManager.App.ViewModels;

/// <summary>The widths the inactive pane's optional columns get, or zero for absent.</summary>
public readonly record struct InactiveColumnLayout(
    double PackageId, double Author, double Version)
{
    /// <summary>How many optional columns are on screen. Only ever grows with width.</summary>
    public int Count =>
        (PackageId > 0 ? 1 : 0) + (Author > 0 ? 1 : 0) + (Version > 0 ? 1 : 0);
}

/// <summary>
/// Which columns the inactive pane can afford, given the width it actually has.
/// <para>
/// This replaced <c>Columns ▾</c> (N1 · UI-6 / UI-7.3, §0b). The picker configured this
/// pane and nothing else; §0b settles that all columns are shown, with the breakpoints
/// still deciding, "all columns" meaning <i>at full width</i>. This is that made
/// continuous — and it is arithmetic rather than a control because of a measurement:
/// literally taking "the active pane's columns minus the order number" costs 286px of
/// fixed width, which in the design's 298px pane leaves NAME <b>ten pixels</b>. That is
/// the R9 defect — equal panes left NAME at 30px, every mod three characters and an
/// ellipsis — reintroduced and worse.
/// </para>
/// <para>
/// Pure, and separate from the view model, for the reason every helper here is: the hub
/// view model needs ten services and the UI dispatcher to exist, so anything worth
/// testing has to live outside it. A width is also a poor thing to check by eye — a
/// column 8px too narrow still looks like a column.
/// </para>
/// </summary>
public static class InactiveColumns
{
    /// <summary>
    /// The narrowest NAME may be squeezed to before a column is dropped. Roughly 25
    /// characters at the body size, which is where a mod name stops being recognisable
    /// and starts being a prefix.
    /// </summary>
    public const double MinName = 150;

    /// <summary>
    /// What the row spends before NAME and after it, whatever else is showing: the
    /// pane's 1px border either side, the row's 8px margins, the stripe + gap + source
    /// badge + gap run, and the trailing gap + 16px status slot.
    /// </summary>
    public const double Fixed = 2 + 16 + (3 + 7 + 14 + 7) + (7 + 16);

    /// <summary>The gap between any two columns.</summary>
    public const double Gap = 7;

    public const double Source = 14;
    public const double Version = 52;
    public const double Author = 110;
    public const double PackageId = 150;

    /// <summary>
    /// The optional columns that fit.
    /// <para>
    /// Revealed cheapest-first — VER (52), then AUTHOR (110), then PACKAGEID (150) —
    /// and that order is arithmetic rather than taste. AUTHOR is the more useful column
    /// when you are hunting for something to activate, but it cannot go first: at the
    /// design's 298px it would leave NAME at 109, under the floor, so the pane would
    /// show nothing extra at its own default size.
    /// </para>
    /// </summary>
    /// <param name="paneWidth">The pane's measured width, in layout units.</param>
    /// <param name="segmented">
    /// <c>2k</c> breakpoint 2. Below 900px every optional column collapses regardless of
    /// room: one list is on screen at full window width down there, so the arithmetic
    /// would happily show all three — but the ACTIVE row deliberately drops packageId
    /// and version at that breakpoint, and two lists disagreeing about what a narrow
    /// window shows is worse than either answer on its own.
    /// </param>
    /// <param name="chevronWidth">
    /// The <c>›</c> column, 14px whenever mod info is an overlay. Taken out of the same
    /// budget, or the last column to appear would be 14px too eager and overlap the
    /// button that opens mod info.
    /// </param>
    public static InactiveColumnLayout For(double paneWidth, bool segmented, double chevronWidth)
    {
        if (segmented) return default;

        var budget = paneWidth - Fixed - chevronWidth;

        var version = Afford(Version, budget, 0) ? Version : 0;
        var author = Afford(Author, budget, Spent(version)) ? Author : 0;
        var packageId = Afford(PackageId, budget, Spent(version) + Spent(author)) ? PackageId : 0;

        return new InactiveColumnLayout(packageId, author, version);
    }

    /// <summary>What NAME is left with — the flexible column absorbs the remainder.</summary>
    public static double NameWidth(double paneWidth, double chevronWidth, InactiveColumnLayout l) =>
        paneWidth - Fixed - chevronWidth
        - Spent(l.Version) - Spent(l.Author) - Spent(l.PackageId);

    private static bool Afford(double width, double budget, double alreadySpent) =>
        budget - alreadySpent - Spent(width) >= MinName;

    /// <summary>A column costs its own width plus the gap before it, or nothing at all.</summary>
    private static double Spent(double width) => width > 0 ? width + Gap : 0;
}
