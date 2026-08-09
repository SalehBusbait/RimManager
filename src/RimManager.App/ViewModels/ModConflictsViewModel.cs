using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the per-mod conflict window (N6b): non-modal, read-only, opened from the ⚡
/// badge or a double-click on an active row. A thin shell over
/// <see cref="ModConflictsDetail"/> — every decision lives in
/// <see cref="ModConflictsPresenter"/>, where the tests are.
/// </summary>
public sealed partial class ModConflictsViewModel : ObservableObject
{
    private readonly Func<ModId, int?> _positionOf;
    private readonly IReadOnlyDictionary<ModId, string> _names;
    private readonly Action<XmlDiffViewModel> _openDiff;
    private readonly Func<ContestRow, bool> _winConflict;
    private readonly Func<ModConflictsDetail> _rebuild;

    public ModConflictsViewModel(
        ModConflictsDetail detail,
        Func<ModId, int?> positionOf,
        IReadOnlyDictionary<ModId, string> names,
        Action<XmlDiffViewModel> openDiff,
        Func<ContestRow, bool> winConflict,
        Func<ModConflictsDetail> rebuild)
    {
        _detail = detail;
        _positionOf = positionOf;
        _names = names;
        _openDiff = openDiff;
        _winConflict = winConflict;
        _rebuild = rebuild;
    }

    /// <summary>Observable because <see cref="WinThis"/> replaces it wholesale.</summary>
    [ObservableProperty] private ModConflictsDetail _detail;

    /// <summary>A row's "Diff" — the same two-up viewer the rest of the app uses (3c).</summary>
    [RelayCommand]
    private void OpenDiff(ContestRow? row)
    {
        if (row is null) return;
        if (ModConflictsPresenter.DiffFor(row, _positionOf, _names) is { } diff) _openDiff(diff);
    }

    /// <summary>
    /// "Win this" — the tab's "Make another win", ported when the tab went (N6c): moves
    /// this mod below the row's live winner so it loads last and takes effect, through
    /// the same snapshot-and-undo path a drag takes. The one action that refreshes the
    /// window's snapshot, because the change was this window's own doing — an outside
    /// drag still needs a reopen, as the footer says.
    /// </summary>
    [RelayCommand]
    private void WinThis(ContestRow? row)
    {
        if (row is null) return;
        if (_winConflict(row)) Detail = _rebuild();
    }
}
