namespace RimManager.App.ViewModels;

/// <summary>
/// The wording of <c>2k</c>'s game-not-found state.
/// <para>
/// Pure and tested, for the reason every other presenter here is: the sentence is a
/// claim about the user's disk. "The install folder no longer exists" said about a
/// folder that is sitting right there, merely emptied by a Steam repair, sends
/// someone looking for the wrong thing.
/// </para>
/// </summary>
public static class GameMissingPresenter
{
    /// <summary>
    /// The reassurance, which never changes: it is the whole reason this is a state
    /// and not an error dialog. Nothing of the user's has been lost.
    /// </summary>
    public const string Reassurance =
        "Your load order and snapshots are safe — we just cannot read the mods.";

    /// <summary>Whether the configured game folder puts the app into this state.</summary>
    public static bool IsMissing(PathCheck game) => game.IsMissing;

    /// <summary>
    /// What was actually found, in one sentence. Driven by <see cref="PathProbe.Game"/>
    /// so this screen and Settings ▸ Paths cannot disagree about the same folder.
    /// </summary>
    public static string Describe(PathCheck game, string? path) => game.Message switch
    {
        _ when string.IsNullOrWhiteSpace(path) =>
            // D4 · "instance" outlived instances. Shown twice for one event — the
            // game-not-found headline and the status bar — so it was the most visible
            // survivor of a concept the app no longer has.
            "No game folder is set yet.",
        _ when game.Message.Contains("Data/Core", StringComparison.Ordinal) =>
            "The folder is still there, but it is not a RimWorld install any more — "
            + "Data/Core is missing.",
        _ => "The configured install folder no longer exists.",
    };

    /// <summary>
    /// The stale path, shown in mono so it can be compared character by character
    /// against what the user thinks it should be. Never elided in the middle: the
    /// difference is usually in one segment, and a middle ellipsis hides exactly that.
    /// </summary>
    public static string Path(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "(no folder set)" : path;
}
