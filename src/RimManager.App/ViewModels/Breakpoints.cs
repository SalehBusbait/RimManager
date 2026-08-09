namespace RimManager.App.ViewModels;

/// <summary>Which of <c>2k</c>'s three layouts the window is wide enough for.</summary>
public enum WindowLayout
{
    /// <summary>Three panes side by side. The design layout.</summary>
    Full,

    /// <summary>Below 1150: mod info becomes a 340px overlay drawer, filter chips collapse.</summary>
    Drawer,

    /// <summary>Below 900: one segmented list, the menu bar collapses to ☰.</summary>
    Segmented,
}

/// <summary>
/// The two adaptive breakpoints (<c>2k</c>). Pure so the thresholds are pinned by a
/// test rather than living as two numbers in a resize handler — the load order is the
/// last thing to lose space, and which layout is in force decides where it gets it.
/// </summary>
public static class Breakpoints
{
    /// <summary>Below this the mod-info pane becomes an overlay drawer.</summary>
    public const double Overlay = 1150;

    /// <summary>Below this the two lists become one segmented view.</summary>
    public const double Segmented = 900;

    public static WindowLayout For(double width) => width switch
    {
        < Segmented => WindowLayout.Segmented,
        < Overlay => WindowLayout.Drawer,
        _ => WindowLayout.Full,
    };

    /// <summary>
    /// Whether mod info is an overlay rather than the third pane. True in BOTH narrow
    /// layouts: below 900 the design makes it a full-height sheet, which is the same
    /// drawer at a different width, not a third arrangement.
    /// </summary>
    public static bool InfoIsOverlay(double width) => For(width) != WindowLayout.Full;

    /// <summary>How wide the overlay is. 340 per <c>2k</c>; a sheet below 900.</summary>
    public static double OverlayWidth(double width) =>
        For(width) == WindowLayout.Segmented ? Math.Min(340, Math.Max(260, width - 60)) : 340;
}
