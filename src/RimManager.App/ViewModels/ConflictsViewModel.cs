using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Analysis;

namespace RimManager.App.ViewModels;

/// <summary>
/// The conflict scan's shared state. N6c slimmed this from the 2c dock tab's backing
/// model to the three things that still have readers: the status bar's zone-3 count,
/// the one-line summary the announce path speaks, and the analyzing flag the scan
/// queue and the per-mod window consult. Conflicts themselves live on the rows (⚡,
/// N6a) and in the per-mod window (N6b); the CLI <c>conflicts</c> command remains the
/// full-report surface.
/// </summary>
public sealed partial class ConflictsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private string _summary = "Not analyzed yet.";

    /// <summary>Takes a completed analysis's totals.</summary>
    public void Populate(ConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ConflictCount = report.Conflicts.Length;
        Summary = ConflictsPresenter.Summarize(report.Conflicts);
    }
}
