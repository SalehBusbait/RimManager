namespace RimManager.App.ViewModels;

/// <summary>A rectangle in screen coordinates. Avalonia-free so the rules below are testable.</summary>
public readonly record struct PlacementRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool IntersectsWith(PlacementRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    /// <summary>The area shared with <paramref name="other"/>; 0 when they miss.</summary>
    public double OverlapArea(PlacementRect other)
    {
        var w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        var h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }
}

/// <summary>
/// Whether a remembered window rectangle may be used, and what to use instead when it
/// may not (O17).
/// <para>
/// The rule exists because restoring a position blind is how a window ends up on a
/// monitor that is no longer plugged in — off screen, un-draggable, and to the user
/// indistinguishable from the app failing to start. Every restore is checked against
/// the screens that exist NOW.
/// </para>
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// The smallest window worth restoring. Below this a saved size is more likely to
    /// be a glitch — a minimised or mid-animation measurement — than an intent.
    /// </summary>
    public const double MinWidth = 640;

    public const double MinHeight = 480;

    /// <summary>
    /// How much of the window must land on a screen for the position to be kept.
    /// <para>
    /// A fraction of the window's own area rather than "the top-left corner is on a
    /// screen": a window can have its corner on one monitor and 95% of its body on a
    /// monitor that has since gone, and that is exactly the case a corner test passes
    /// and the user cannot use. A quarter is enough to grab and drag.
    /// </para>
    /// </summary>
    public const double MinVisibleFraction = 0.25;

    /// <summary>
    /// The rectangle to open at, or <c>null</c> to let the window centre itself at its
    /// designed size — which is what a first run, a nonsense saved size, or a monitor
    /// that has gone away all mean.
    /// </summary>
    public static PlacementRect? Restore(
        double? x, double? y, double? width, double? height, IReadOnlyList<PlacementRect> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (x is not { } px || y is not { } py) return null;
        if (width is not { } w || height is not { } h) return null;
        if (w < MinWidth || h < MinHeight) return null;
        if (double.IsNaN(px) || double.IsNaN(py) || double.IsNaN(w) || double.IsNaN(h)) return null;
        if (screens.Count == 0) return null;

        var wanted = new PlacementRect(px, py, w, h);
        var visible = screens.Sum(screen => screen.OverlapArea(wanted));

        return visible >= w * h * MinVisibleFraction ? wanted : null;
    }

    /// <summary>
    /// Whether the saved size alone is worth keeping when the POSITION was rejected.
    /// Reopening centred at the size the user chose beats reopening centred at the
    /// markup literal — the monitor went away, not their preference for a big window.
    /// The size is still clamped to the screen it will land on by the caller.
    /// </summary>
    public static (double Width, double Height)? RestoreSizeOnly(
        double? width, double? height, IReadOnlyList<PlacementRect> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (width is not { } w || height is not { } h) return null;
        if (w < MinWidth || h < MinHeight) return null;
        if (screens.Count == 0) return null;

        // Never larger than the largest screen available now: a size saved on a 4K
        // monitor must not open beyond the edges of a laptop panel.
        var widest = screens.Max(s => s.Width);
        var tallest = screens.Max(s => s.Height);

        return (Math.Min(w, widest), Math.Min(h, tallest));
    }
}
