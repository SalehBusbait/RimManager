using System.Collections.Immutable;

namespace RimManager.Core.Domain;

/// <summary>
/// The window arrangement that survives a restart, written by
/// <c>WorkspaceStateRepository</c> to <c>layout.json</c> at the app root: the window's
/// own bounds, the main pane splitters, the dock's open state, tab and geometry, plus
/// the standing filters.
/// <para>
/// <b>App-global, not per install.</b> This said "persisted per install" and CLAUDE.md
/// said "per-instance"; both were stale by the time N11 slice D retired
/// <c>Instance</c>. <c>WorkspaceService.State()</c> passes no root, so the repository
/// defaults to <c>AppPaths.Root</c> — which is right for window geometry, since a
/// window belongs to a screen and a person, not to a game folder.
/// </para>
/// <para>
/// Trimmed in N11 to what the shipped app actually models. The appearance fields it
/// used to carry (density, theme, stripes, zebra, previews) live in
/// <c>settings.json</c> and were never read from here — a second store for one
/// preference is the trap R6 already paid for. The pane widths came BACK in O17, with
/// the thing that was missing when they were cut: the window bounds they were measured
/// in. A width without them really was half a promise.
/// </para>
/// <para>
/// An older <c>layout.json</c> carrying the per-tab <c>dockHeights</c> map still loads
/// — unknown properties are ignored — and the dock simply opens at its default height
/// once. That loss IS the change, so it needs no migration.
/// </para>
/// <para>
/// Filters persist so a narrowed list is never a surprise on the next launch — the
/// empty state (<c>3e</c>) names every filter still active for the same reason.
/// </para>
/// </summary>
public sealed record LayoutState
{
    public static readonly LayoutState Default = new();

    // --- dock (1e) ----------------------------------------------------------

    /// <summary>Closed by default: the 26px strip is always visible, the body is not.</summary>
    public bool IsDockOpen { get; init; }

    /// <summary>Which tab the strip has selected, by id (<c>warnings</c>, <c>updates</c>, …).</summary>
    public string DockTab { get; init; } = "warnings";

    /// <summary>
    /// The dock's open height — <b>one</b>, shared by every tab (O4, owner's call).
    /// <para>
    /// This reverses N11 slice B, which made it per tab on the design's argument that
    /// "Conflicts wants more room than Updates". Conflicts is not a tab any more (N6c),
    /// and in use the per-tab memory reads as the dock resizing itself when you switch
    /// — a jump you did not ask for, every time, to save a drag you rarely want.
    /// </para>
    /// <para>
    /// Null until the user has dragged the splitter once; the default is
    /// <c>DockGeometry.DefaultBodyHeight</c>, which belongs to the App layer.
    /// </para>
    /// </summary>
    public double? DockHeight { get; init; }

    /// <summary>
    /// The master/detail splitter position, still <b>per tab</b> — and deliberately
    /// not collapsed with the height. History is a three-pane tab whose detail column
    /// is the diff alone, sized against a fixed rail beside it; one shared width would
    /// cost it the geometry it was designed to.
    /// </summary>
    public ImmutableDictionary<string, double> DockDetailWidths { get; init; } =
        ImmutableDictionary<string, double>.Empty;

    // --- the window itself (O17) ---------------------------------------------

    /// <summary>
    /// Where the window was and how big, so reopening lands where it was left rather
    /// than centred at a markup literal. All four are null until the first close.
    /// <para>
    /// These are the <b>restored</b> bounds even when <see cref="WindowMaximised"/> is
    /// set: un-maximising after a restart has to return to a size the user chose, and
    /// saving the maximised rectangle as if it were normal is how that goes wrong.
    /// </para>
    /// </summary>
    public double? WindowX { get; init; }

    public double? WindowY { get; init; }

    public double? WindowWidth { get; init; }

    public double? WindowHeight { get; init; }

    /// <summary>Whether to reopen maximised. Kept apart from the bounds above.</summary>
    public bool WindowMaximised { get; init; }

    // --- the main window's pane splitters (O17) ------------------------------

    /// <summary>
    /// The inactive (left) pane's width and the Mod Info (right) pane's width. The
    /// active list takes what is left, which is why only two of the three persist.
    /// Null until dragged; the defaults are the markup's, which a source test pins.
    /// </summary>
    public double? InactivePaneWidth { get; init; }

    public double? InfoPaneWidth { get; init; }

    // --- filters ------------------------------------------------------------

    public ImmutableArray<string> ActiveTagFilters { get; init; } = [];

    public bool MatchAllTags { get; init; }

    public bool WarningsOnly { get; init; }
}
