using System.Threading.Tasks;

namespace RimManager.App.ViewModels;

/// <summary>
/// What a destructive confirmation asks (<c>2i</c>-6). One shape for all of them, because
/// the shape is the safety feature: a user who has read one of these knows where the
/// consequence sentence is and what the primary button will say.
/// </summary>
/// <param name="Title">Names the OBJECT, not the operation — "Delete Vanilla+ Anomaly?".</param>
/// <param name="Body">
/// Exactly what is lost <b>and what is not</b>. The second half is not politeness: with a
/// mod manager the reasonable fear is that it will touch the game folder, and every one of
/// these has to say whether it does.
/// </param>
/// <param name="Verb">
/// The primary button's label, and it is a verb — "Delete instance", never "OK". A button
/// that says OK makes the reader reconstruct what they are agreeing to from the title.
/// </param>
/// <param name="SafetyLabel">
/// An optional opt-out that makes the action recoverable ("Export a backup first"), or
/// null when nothing would help. Offered only when it is real.
/// </param>
/// <param name="SafetyDefaultsOn">Whether that safety is ticked to begin with.</param>
public sealed record ConfirmRequest(
    string Title,
    string Body,
    string Verb,
    string? SafetyLabel = null,
    bool SafetyDefaultsOn = true);

/// <summary>The answer: whether it was confirmed, and whether the safety was left on.</summary>
/// <param name="Confirmed">False for Cancel, Escape, or closing the window.</param>
/// <param name="SafetyChosen">Meaningless when <paramref name="Confirmed"/> is false.</param>
public sealed record ConfirmResult(bool Confirmed, bool SafetyChosen = false)
{
    /// <summary>What a dismissed dialog returns, and the default everywhere it is absent.</summary>
    public static readonly ConfirmResult Cancelled = new(false);
}

/// <summary>
/// Asks the user to confirm something destructive.
/// <para>
/// A delegate rather than an interface with one method, and supplied <b>by the view</b>:
/// a modal needs a parent window, and view models in this project stay constructible
/// without one — which is what keeps their logic testable. A view model with no confirmer
/// wired simply cannot perform the action, which is the safe failure.
/// </para>
/// </summary>
public delegate Task<ConfirmResult> Confirmer(ConfirmRequest request);
