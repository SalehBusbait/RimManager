namespace RimManager.App.ViewModels;

/// <summary>
/// Where the dock's remembered sizes live — Avalonia-free so the arithmetic is
/// actually covered, because none of the dock's geometry is visible to CI.
/// <para>
/// <b>One height, shared by every tab</b> (O4, owner's call), and the master/detail
/// splitter position still per tab. The design argued the height per tab too —
/// "Conflicts wants more than Updates" (README §Dock tabs) — but Conflicts stopped
/// being a tab in N6c, and what per-tab memory actually produces is the dock resizing
/// itself on every switch. The widths keep their per-tab memory because History is
/// three panes and its detail column is genuinely a different measurement.
/// </para>
/// </summary>
public sealed class DockGeometry
{
    /// <summary>The strip is always visible, so it is never part of the body height.</summary>
    public const double StripHeight = 26;

    /// <summary>README: "min 120px, max 50% of window".</summary>
    public const double MinBodyHeight = 120;

    public const double DefaultBodyHeight = 240;

    private readonly Dictionary<int, double> _detailWidths = [];

    /// <summary>
    /// The design's detail-panel widths: 392 for Warnings and Updates, and History's
    /// left list is 560 so its detail takes the remainder. A tab with no detail panel
    /// still has an entry; it is simply never asked. (Conflicts' 452 went with its
    /// tab in N6c.)
    /// </summary>
    public static double DefaultDetailWidth(int tab) => tab switch
    {
        // History is three panes (2d): its detail is the diff alone, with a fixed
        // 248px rail beside it, so the diff column is wider than the others.
        2 => 620,   // History
        _ => 392,   // Warnings, Updates, Activity
    };

    /// <summary>The one open height, whichever tab is showing.</summary>
    public double BodyHeight { get; set; } = DefaultBodyHeight;

    public double DetailWidthFor(int tab) =>
        _detailWidths.TryGetValue(tab, out var width) ? width : DefaultDetailWidth(tab);

    public void SetDetailWidth(int tab, double width) => _detailWidths[tab] = width;

    /// <summary>Back to the defaults — the reset command's half of layout persistence.</summary>
    public void Reset()
    {
        BodyHeight = DefaultBodyHeight;
        _detailWidths.Clear();
    }

    /// <summary>
    /// Clamps a requested body height to the design's bounds. The maximum is half the
    /// window: a dock that can swallow the mod lists is a dock you cannot get out of
    /// without knowing the shortcut.
    /// </summary>
    public static double ClampBodyHeight(double requested, double windowHeight)
    {
        var max = Math.Max(MinBodyHeight, (windowHeight * 0.5) - StripHeight);
        return Math.Clamp(requested, MinBodyHeight, max);
    }

    /// <summary>
    /// The maximised body height — half the window, which is the same ceiling the
    /// splitter enforces, so ⤢ and dragging to the top agree.
    /// </summary>
    public static double MaximisedBodyHeight(double windowHeight) =>
        ClampBodyHeight(double.MaxValue, windowHeight);
}
