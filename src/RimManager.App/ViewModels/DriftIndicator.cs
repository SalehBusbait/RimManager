using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// The words for <see cref="DriftKind"/>, in the two places the app answers "is there
/// anything to apply": the active pane's footer and the Apply ▾ flyout's dimmed line.
/// <para>
/// A helper rather than a switch in the view model, for a reason specific to this project:
/// <c>MainWindowViewModel</c> is not constructible under test — <c>new MainWindowViewModel</c>
/// appears nowhere in <c>tests/</c> — so a string built inside it can be pinned by nothing.
/// Here the mapping is reachable, and <c>DriftIndicatorTests</c> holds the part that a test
/// can actually hold.
/// </para>
/// <para>
/// <b>Deviation from the design handoff, deliberate.</b> <c>README.md</c> §main-window
/// specifies this slot verbatim as <c>"◆ 4 unsaved changes" in 10px mono warning</c>. Both
/// halves are wrong here and both are dropped:
/// </para>
/// <list type="number">
///   <item><b>The word.</b> Nothing about the modlist is unsaved — it commits on every
///   edit and has no Save button anywhere. The only thing not written is the game's
///   <c>ModsConfig.xml</c>, so the line says what is true of it. The handoff's own rationale
///   panel gives the feature's purpose as "nothing tells you the list differs from what the
///   game will load", which is exactly this — "unsaved" was its shorthand, not its
///   intent.</item>
///   <item><b>The count.</b> Not implementable as an honest number. The applied <em>order</em>
///   is not persisted — <c>Modlist.LastAppliedHash</c> is 16 hex characters — and the nearest
///   available diff, <c>ProfileDiff.Moved</c>, counts index inequality: inserting one mod at
///   the top of a 548-mod list reports 547 changes. A number nobody can act on is worse than
///   no number.</item>
/// </list>
/// </summary>
public static class DriftIndicator
{
    /// <summary>
    /// The status bar's drift ZONE (S-DRIFT, built at last — the display lived in the
    /// active pane's footer until the UI audit moved it home). All four states get
    /// their own words, including in-sync: the earlier "no applied timestamp is
    /// persisted" deviation was STALE — <see cref="Modlist.LastAppliedUtc"/> has been
    /// stamped since the modlist migration, and the timestamp is the information.
    /// <para>
    /// Collapsing <see cref="DriftKind.Unknown"/> into pending would call a list you
    /// have never applied "edited", and collapsing
    /// <see cref="DriftKind.ChangedOutsideRimManager"/> into it would hide the one
    /// state where the next Apply destroys what RimWorld itself just wrote.
    /// </para>
    /// </summary>
    /// <param name="appliedAtLocal">When the game file was last written by RimManager,
    /// LOCAL time — shown only in-sync. Null (a list last applied before the stamp
    /// existed) falls back to the plain words rather than inventing a time.</param>
    public static string Zone(DriftKind kind, DateTimeOffset? appliedAtLocal) => kind switch
    {
        DriftKind.PendingApply => "Edited — not applied",
        DriftKind.Unknown => "Never applied",
        DriftKind.ChangedOutsideRimManager => "Changed outside · Review",
        _ => appliedAtLocal is { } at ? $"Applied {at:HH:mm}" : "In sync",
    };

    /// <summary>The zone's tooltip: the long sentence the 10px line cannot carry.</summary>
    public static string ZoneTip(DriftKind kind) => kind switch
    {
        DriftKind.PendingApply =>
            "This list differs from what the game will load. Click to apply it — "
            + "nothing reaches ModsConfig.xml until you do.",
        DriftKind.Unknown =>
            "This list has never been written to the game. Apply writes it; "
            + "until then RimWorld loads whatever its file already says.",
        DriftKind.ChangedOutsideRimManager =>
            "Something other than RimManager rewrote ModsConfig.xml — usually RimWorld "
            + "loading a save's mod list. Click to review before the next Apply "
            + "overwrites it.",
        _ => "The game will load exactly this list.",
    };

    /// <summary>
    /// The dimmed footer inside the Apply ▾ flyout. Its markup comment has always said it
    /// answers "is there anything to apply"; until now it rendered
    /// <c>"N active · M installed"</c>, which is an inventory and answers nothing of the
    /// kind. The count of what would be written stays, because that is the thing about to
    /// happen; the second half is now the answer.
    /// </summary>
    public static string ApplyFlyout(DriftKind kind, int activeCount) =>
        $"{activeCount} active · {Clause(kind)}";

    private static string Clause(DriftKind kind) => kind switch
    {
        DriftKind.PendingApply => "not applied yet",
        DriftKind.Unknown => "never applied",
        DriftKind.ChangedOutsideRimManager => "the game was changed elsewhere",
        _ => "already applied",
    };
}
