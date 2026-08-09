namespace RimManager.App.ViewModels;

/// <summary>
/// "1 mod" / "2 mods" — the house style, in one place (D3).
/// </summary>
/// <remarks>
/// It exists because "(s)" had leaked into eight user-facing lines while ~17 others did
/// it properly, and the sharpest symptom was the commit bar contradicting itself about
/// the same noun in the same slot: the blocked path wrote "1 blocking warning(s)" where
/// the overridden path wrote "1 blocking warning". Machine output in a sentence the user
/// is being asked to act on.
/// <para>
/// Log lines are deliberately NOT routed through this. "(s)" is conventional there, the
/// reader is a developer, and changing them buys nothing.
/// </para>
/// </remarks>
public static class Plural
{
    /// <summary>"1 snapshot", "3 snapshots" — for nouns that take a plain -s.</summary>
    public static string Of(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? "" : "s")}";

    /// <summary>For nouns that do not: <c>Of(2, "entry", "entries")</c>.</summary>
    public static string Of(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}
