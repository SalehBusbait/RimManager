using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;
using RimManager.Core.Sorting;
using RimManager.Core.Validation;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the Warnings dock tab (<c>2a</c>): the six ordered groups, the severity
/// chips, the search box and the detail panel. All the arrangement lives in the pure
/// <see cref="WarningsPresenter"/>; this is the observable shell around it.
/// </summary>
public sealed partial class WarningsViewModel : ObservableObject
{
    private ImmutableArray<WarningEntry> _all = [];
    private SortResult? _lastSort;
    private Func<ModId, int?> _positionOf = _ => null;
    private IReadOnlyDictionary<ModId, string> _names = new Dictionary<ModId, string>();

    public ObservableCollection<WarningEntry> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(SelectionText), nameof(HasSelection),
                              nameof(HasChain), nameof(HasChainNote))]
    private WarningEntry? _selected;

    [ObservableProperty] private string _search = string.Empty;

    /// <summary>"Revalidated 4s ago · after Sort" — where the numbers on screen came from.</summary>
    [ObservableProperty] private string _provenance = "Not validated yet";

    // The chips are mutually exclusive; they are a lens, not a set. Null tone = All.
    [ObservableProperty] private bool _showAll = true;
    [ObservableProperty] private bool _showBlocking;
    [ObservableProperty] private bool _showWarning;
    [ObservableProperty] private bool _showInfo;

    public int AllCount => _all.Length;
    public int BlockingCount => _all.Count(e => e.Tone == WarningTone.Blocking);
    public int WarningToneCount => _all.Count(e => e.Tone == WarningTone.Warning);
    public int InfoCount => _all.Count(e => e.Tone == WarningTone.Info);

    // The chip counts wear their severity's colour while it exists (2a / the v2
    // count-tone pass): a red 48 says "blocking" before the label does, and a grey 0
    // says "nothing here" without one. Bound as bools because tone is a class.
    public bool BlockingCountToned => BlockingCount > 0;
    public bool WarningCountToned => WarningToneCount > 0;

    // The dock strip's pill and icon = HIGHEST severity inside (v2 — the pill used
    // to bind warn to HasAny, so blocking read amber and info-only ALSO read amber).
    // Info-only stays neutral: an info count is not a problem to colour.
    public bool PillIsBad => BlockingCount > 0;
    public bool PillIsWarn => WarningToneCount > 0 && BlockingCount == 0;

    public bool HasSelection => Selected is { IsGroupHeader: false };

    /// <summary>The detail panel's header counter: "3 of 12 · ↑↓".</summary>
    public string SelectionText
    {
        get
        {
            if (Selected is not { IsGroupHeader: false }) return string.Empty;
            var issues = Rows.Where(r => !r.IsGroupHeader).ToList();
            var index = issues.IndexOf(Selected);
            return index < 0 ? string.Empty : $"{index + 1} of {issues.Count} · ↑↓";
        }
    }

    /// <summary>Only cycles render the indented edge chain.</summary>
    public bool HasChain => Detail.Chain.Length > 0;

    public bool HasChainNote => Detail.ChainNote.Length > 0;

    public WarningDetail Detail =>
        WarningsPresenter.BuildDetail(Selected, _lastSort, _positionOf, _names);

    partial void OnShowAllChanged(bool value) { if (value) ExclusiveChip(all: true); Regroup(); }
    partial void OnShowBlockingChanged(bool value) { if (value) ExclusiveChip(blocking: true); Regroup(); }
    partial void OnShowWarningChanged(bool value) { if (value) ExclusiveChip(warning: true); Regroup(); }
    partial void OnShowInfoChanged(bool value) { if (value) ExclusiveChip(info: true); Regroup(); }
    partial void OnSearchChanged(string value) => Regroup();

    private void ExclusiveChip(bool all = false, bool blocking = false, bool warning = false, bool info = false)
    {
        if (!all) ShowAll = false;
        if (!blocking) ShowBlocking = false;
        if (!warning) ShowWarning = false;
        if (!info) ShowInfo = false;
    }

    private WarningTone? Tone =>
        ShowBlocking ? WarningTone.Blocking
        : ShowWarning ? WarningTone.Warning
        : ShowInfo ? WarningTone.Info
        : null;

    /// <summary>
    /// Refreshes from a validation pass. The last sort is kept, not re-supplied, so a
    /// revalidate that does not re-sort still shows the cycles it broke.
    /// </summary>
    public void Populate(
        IEnumerable<ValidationIssue> issues,
        IEnumerable<ModWarning> scanWarnings,
        Func<ModId, int?> positionOf,
        IReadOnlyDictionary<ModId, string> names,
        string provenance)
    {
        _positionOf = positionOf;
        _names = names;
        _all = WarningsPresenter.BuildIssues(issues, _lastSort, scanWarnings, names);
        Provenance = provenance;
        Regroup();

        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(BlockingCount));
        OnPropertyChanged(nameof(WarningToneCount));
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(BlockingCountToned));
        OnPropertyChanged(nameof(WarningCountToned));
        OnPropertyChanged(nameof(PillIsBad));
        OnPropertyChanged(nameof(PillIsWarn));
    }

    /// <summary>Records the sort whose broken edges feed the Cycles group.</summary>
    public void RecordSort(SortResult result) => _lastSort = result;

    /// <summary>The recorded sort, for the hub's "Accept dropped edge" — the edge to
    /// pin lives in its <see cref="SortResult.BrokenEdges"/>.</summary>
    public SortResult? LastSort => _lastSort;

    /// <summary>
    /// Every warning, of every kind, unfiltered — validation issues, the sort's broken
    /// edges and the scan's duplicates.
    /// <para>
    /// The rows and the toolbar chip read from THIS rather than from the validation
    /// report, which is what makes them agree with the count in the tab strip. They did
    /// not before: the report is only one of the three sources, so a cycle or a
    /// duplicate was counted in the dock and invisible to the chip that claims to find
    /// mods with warnings (N2 · UI-7.1).
    /// </para>
    /// </summary>
    public ImmutableArray<WarningEntry> All => _all;

    /// <summary>
    /// Every warning this mod OWNS — the ones produced by rules it declared itself.
    /// Group headers are never returned; they are a rendering device.
    /// </summary>
    public ImmutableArray<WarningEntry> For(ModId id) =>
        [.. _all.Where(e => !e.IsGroupHeader && e.Owner == id)];

    /// <summary>
    /// Selects the first warning this mod owns, for "click the row's
    /// glyph and land on the warning it stands for" (<c>UI-4</c>).
    /// <para>
    /// Clears the severity chip first when the warning would be filtered out. A chip
    /// left on Blocking while the user clicks a Warning-tone glyph would select
    /// something not on screen, and the dock would look like it had ignored the click —
    /// the chips are a lens, and following a link through one has to widen it.
    /// </para>
    /// </summary>
    /// <returns>False when nothing in the dock names this mod.</returns>
    public bool SelectFor(ModId id)
    {
        var match = For(id).FirstOrDefault();
        if (match is null) return false;

        if (Tone is { } tone && tone != match.Tone) ShowAll = true;

        // Regroup rebuilds Rows, so the instance to select is the one now IN Rows —
        // WarningEntry is a record, so this matches by value rather than by reference.
        Selected = Rows.FirstOrDefault(r => r == match) ?? match;
        return true;
    }

    /// <summary>
    /// After a sort that broke a cycle the dock opens here with the first cycle
    /// selected (<c>2a</c> "Behaviour"). Returns true when there was one to select.
    /// </summary>
    public bool SelectFirstCycle()
    {
        var cycle = Rows.FirstOrDefault(r => r is { IsGroupHeader: false, Group: WarningGroup.Cycles });
        if (cycle is null) return false;

        Selected = cycle;
        return true;
    }

    private void Regroup()
    {
        var keep = Selected;
        var cycles = _lastSort is { BrokenEdges.IsDefaultOrEmpty: false }
            ? $"from last sort, {_lastSort.BrokenEdges.Length} dropped"
            : string.Empty;

        Rows.Clear();
        foreach (var row in WarningsPresenter.Group(_all, Tone, Search, cycles)) Rows.Add(row);

        // Keep the selection if it survived the filter; otherwise drop it rather than
        // silently selecting a neighbour the user did not choose.
        Selected = keep is not null && Rows.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(IsTrulyEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(FilteredEmptyText));
        OnPropertyChanged(nameof(HasAny));
    }

    // --- empty states (v2 systemic pass) -------------------------------------
    // The success state used to bind !Rows.Count — the FILTERED list — so a chip or
    // search that hid every row showed "No warnings" while real warnings existed
    // behind the filter: the app asserting a clean list it has not got. Truly-empty
    // keys on AllCount; filtered-empty is its own state and says how much it hides.

    public bool IsTrulyEmpty => AllCount == 0;

    public bool IsFilteredEmpty => AllCount > 0 && Rows.Count == 0;

    public string FilteredEmptyText =>
        AllCount == 1
            ? "Nothing matches these filters — 1 warning is hidden."
            : $"Nothing matches these filters — {AllCount} warnings are hidden.";

    /// <summary>The dock strip's warn tone: colour asserts a problem only when one exists.</summary>
    public bool HasAny => AllCount > 0;

    /// <summary>The filtered-empty state's way out: back to All, search cleared.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Search = string.Empty;
        ShowAll = true;
    }
}
