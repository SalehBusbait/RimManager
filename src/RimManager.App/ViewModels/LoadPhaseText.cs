namespace RimManager.App.ViewModels;

/// <summary>
/// What the load state is doing. A reload is not one operation, and on a modlist switch the
/// slowest part is not the scan.
/// </summary>
public enum LoadPhase
{
    /// <summary>The scan: every mod folder under every root.</summary>
    ReadingMods,

    /// <summary>Copying the outgoing list's <c>Mod_*.xml</c> files out of the game's config.</summary>
    SavingModSettings,

    /// <summary>Copying the incoming list's back in.</summary>
    RestoringModSettings,

    /// <summary>
    /// Four collision passes over the active list, the last of them Cecil over every
    /// assembly. In the load state rather than backgrounded <b>because the result decorates
    /// the rows</b> — N6 puts a ⚡ badge on each one — so a list rendered before this finishes
    /// is a list that changes under the user a second later. That is the line: work whose
    /// answer lands on a row waits; work whose answer lands in a tab does not.
    /// </summary>
    AnalysingConflicts,
}

/// <summary>
/// The words for each phase.
/// <para>
/// Kept out of the view model and out of Avalonia so it can be tested, per the convention
/// that presentation logic worth checking lives in a helper. The card used to hardcode
/// "Reading mod folders…" in its markup, which is why a switch showed that title while it was
/// actually copying settings files — the one phase the title was wrong for was the one the
/// user was waiting on.
/// </para>
/// <para>
/// There is no <c>HasCount</c> and no <c>CanSkip</c> here, and their absence is the point.
/// Both existed because two phases could only show a moving stripe: the settings copies did
/// not know their totals, and the conflict pass reported nothing at all. Skip was the
/// compensation for the second — eighteen cold seconds behind an indeterminate bar is a hang
/// with a logo. <b>Counting the work removed the need for both.</b> The settings copies
/// materialise their file list before the first copy, and the conflict pass ticks once per
/// mod per pass, so every phase reports a real fraction. A bar visibly moving through
/// 214 / 292 is not something anyone needs to escape.
/// </para>
/// </summary>
public static class LoadPhaseText
{
    public static string For(LoadPhase phase) => phase switch
    {
        LoadPhase.SavingModSettings => "Saving this list's mod settings…",
        LoadPhase.RestoringModSettings => "Restoring the new list's mod settings…",
        LoadPhase.AnalysingConflicts => "Checking for conflicts…",
        _ => "Reading mod folders…",
    };

    /// <summary>
    /// What the count is counting, so the line under the bar reads as a sentence rather than
    /// as two bare numbers: <c>214 / 292 mods · Harmony patches</c>.
    /// </summary>
    public static string Unit(LoadPhase phase) => phase switch
    {
        LoadPhase.SavingModSettings or LoadPhase.RestoringModSettings => "files",
        LoadPhase.AnalysingConflicts => "checks",
        _ => "folders",
    };
}
