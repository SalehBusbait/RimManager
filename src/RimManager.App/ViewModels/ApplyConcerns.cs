using System.Collections.Generic;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// Whether pressing Apply is worth stopping for, and what to say when it is.
/// <para>
/// The bar used to appear on <b>every</b> apply, at the very bottom of the window, asking
/// "Write 73 mods to ModsConfig.xml?" — a question with one answer, seven hundred pixels
/// from the button that raised it. A confirmation that always fires teaches the hand to
/// dismiss it, and then it is not a confirmation at all: it is a second click on Apply
/// wearing a warning's clothes.
/// </para>
/// <para>
/// So the rule is <b>say something or stay out of the way</b>. What counts is narrow on
/// purpose, and the exclusions matter as much as the inclusions.
/// </para>
/// </summary>
public static class ApplyConcerns
{
    /// <summary>
    /// The reasons this apply deserves a stop. Empty means write, immediately, with no bar.
    /// </summary>
    /// <param name="drift">Fresh — the caller re-reads the game's file first, or this lies.</param>
    /// <param name="blockingErrors">
    /// Errors, when the user has turned the refusal <em>off</em> in Settings ▸ Advanced.
    /// Opting out of being stopped is not the same as opting out of being told.
    /// </param>
    public static IReadOnlyList<string> For(DriftKind drift, int blockingErrors)
    {
        var reasons = new List<string>(2);

        // The one case where applying DESTROYS something: RimWorld wrote that file, and
        // this write replaces it with an order the game has never seen.
        if (drift == DriftKind.ChangedOutsideRimManager)
        {
            reasons.Add("RimWorld's mod list changed outside RimManager — applying replaces it");
        }

        // S-COMMIT's copy: reaching this bar at all means the refusal was turned off in
        // Settings ▸ Advanced, so the sentence names that fact — "overridden" — and
        // states both halves plainly: Apply still works, the game may not.
        if (blockingErrors > 0)
        {
            // Through the shared helper (D3): this line was already correct, and the
            // blocked path in LoadOrder was not — one noun, one slot, two answers. Both
            // now come from the same place, which is the only way that stays true.
            reasons.Add($"{Plural.Of(blockingErrors, "blocking warning")} "
                        + "overridden — Apply stays available, but the game may fail to "
                        + "load this order");
        }

        return reasons;
    }

    /// <summary>
    /// What is deliberately <b>not</b> a reason, recorded because each was considered and
    /// each would put the bar back in front of the user on every single apply:
    /// <list type="bullet">
    ///   <item><b>Ordinary warnings.</b> Standing state, not an event — a real install sits
    ///   at a dozen of them for months. They are on the rows, in the dock and in the status
    ///   bar; repeating them at the moment of the write adds nothing and costs the bar its
    ///   meaning.</item>
    ///   <item><b>Mods in the list that are not installed.</b> Also standing, also already
    ///   on the row, and Apply writes them deliberately — omitting them would make Apply
    ///   produce something other than what the pane shows.</item>
    ///   <item><b>The size of the change.</b> A big diff is not a dangerous one, and the
    ///   list on screen is the diff.</item>
    /// </list>
    /// </summary>
    public static string Summarise(IReadOnlyList<string> reasons) => string.Join(" · ", reasons);

    /// <summary>
    /// The bar's headline. Names the file and the count, because that is what a stop is for
    /// — but only ever appears when <see cref="For"/> found something.
    /// </summary>
    public static string Title(int modCount) =>
        $"Apply {modCount} mod{(modCount == 1 ? "" : "s")} to the game?";

    /// <summary>True when this apply should just happen.</summary>
    public static bool IsRoutine(DriftKind drift, int blockingErrors) =>
        !For(drift, blockingErrors).Any();
}
