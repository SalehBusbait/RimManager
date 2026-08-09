using CommunityToolkit.Mvvm.ComponentModel;

namespace RimManager.App.ViewModels;

/// <summary>Backs the one destructive-confirm dialog (<c>2i</c>-6).</summary>
public sealed partial class DestructiveConfirmViewModel : ObservableObject
{
    public DestructiveConfirmViewModel(ConfirmRequest request)
    {
        Title = request.Title;
        Body = request.Body;
        Verb = request.Verb;
        SafetyLabel = request.SafetyLabel;
        _safetyChosen = request.SafetyDefaultsOn;
    }

    public string Title { get; }
    public string Body { get; }

    /// <summary>The primary button's label. Always a verb (<c>2i</c>-6), never "OK".</summary>
    public string Verb { get; }

    public string? SafetyLabel { get; }

    /// <summary>Hidden entirely when there is no safety worth offering — an unticked,
    /// unexplained checkbox reads as a step the user has skipped.</summary>
    public bool HasSafety => SafetyLabel is not null;

    [ObservableProperty] private bool _safetyChosen;

    /// <summary>
    /// Set by the primary button only. Everything else — Cancel, Escape, the window's
    /// close button — leaves it false, so a dismissed dialog can never read as consent.
    /// </summary>
    public bool Confirmed { get; private set; }

    public void Confirm() => Confirmed = true;

    public ConfirmResult Result => Confirmed
        ? new ConfirmResult(true, HasSafety && SafetyChosen)
        : ConfirmResult.Cancelled;
}
