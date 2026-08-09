using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RimManager.App.ViewModels;

/// <summary>
/// One segment of the log-level control (<c>2g</c>).
/// <para>
/// A view model per option rather than five hand-written radios, for the same reason the
/// tag swatches are: an option that is not separately bindable cannot be clicked, and the
/// failure is silent.
/// </para>
/// <para>
/// The choice fires from <see cref="ChooseCommand"/> — a click, Space or Enter, and
/// nothing else — and <see cref="IsSelected"/> is display state with no side effect.
/// It used to fire from <c>OnIsSelectedChanged</c> under a TwoWay-bound radio group,
/// which made the setting writable by everything a radio group does uninvited — arrow
/// keys check segments as focus moves through them — and the segments read
/// loudest-first, so keys meant for scrolling could land the floor on Error: a floor
/// that then silences the very log line that would have named the change. The owner's
/// settings.json carried exactly that unchosen 0.
/// </para>
/// </summary>
public sealed partial class LogLevelChoiceViewModel(int index, string label, Action<int> choose)
    : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;

    /// <summary>Display state only — written by the owner's resync, never a trigger.</summary>
    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Choose() => choose(Index);
}
