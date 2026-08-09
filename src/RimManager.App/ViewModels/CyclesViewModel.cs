using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Sorting;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the Cycles panel: the broken-edge rows and a summary, refreshed on each sort.
/// Presentation is the pure <see cref="CyclesPresenter"/>.
/// </summary>
public sealed partial class CyclesViewModel : ObservableObject
{
    public ObservableCollection<CycleRow> Rows { get; } = [];

    [ObservableProperty] private int _cycleCount;
    [ObservableProperty] private string _summary = "Sort to check for cycles.";

    /// <summary>Refreshes the rows from a completed sort.</summary>
    public void Populate(SortResult result)
    {
        Rows.Clear();
        foreach (var row in CyclesPresenter.BuildRows(result)) Rows.Add(row);
        CycleCount = result.Cycles.IsDefaultOrEmpty ? 0 : result.Cycles.Length;
        Summary = CyclesPresenter.Summarize(result);
    }
}
