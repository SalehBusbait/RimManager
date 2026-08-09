using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the History dock tab (<c>2d</c>). History is <b>append-only</b> (design
/// non-negotiable #5): restoring appends a new snapshot whose contents equal the old
/// one, and nothing here ever rewinds or rewrites the past. Named and pinned states
/// are exempt from pruning, which is the whole point of naming one.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private ImmutableArray<SnapshotEntry> _all = [];
    private IReadOnlyList<ModlistSnapshot> _snapshots = [];
    private IReadOnlyDictionary<ModId, string> _names = new Dictionary<ModId, string>();
    private string _gameVersion = "unknown";
    private string _rules = "none";

    public ObservableCollection<SnapshotEntry> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(HasSelection), nameof(DetailHeaderText),
                              nameof(AsideHeaderText), nameof(NameDisplay), nameof(HasName),
                              nameof(NameNote))]
    private SnapshotEntry? _selected;

    // The editor follows the selection, and selecting elsewhere ABANDONS an open edit:
    // leaving one state's name in the box while another is selected is how a rename
    // lands on the wrong snapshot.
    partial void OnSelectedChanged(SnapshotEntry? value)
    {
        IsEditingName = false;
        EditName = value?.Snapshot.Name ?? string.Empty;
    }

    /// <summary>"Vanilla+ Anomaly · 48 snapshots · 3.1 MB" — the MODLIST it belongs to.</summary>
    [ObservableProperty] private string _summary = "No snapshots yet";

    [ObservableProperty] private bool _showAll = true;
    [ObservableProperty] private bool _showAppliedOnly;
    [ObservableProperty] private bool _showNamed;

    /// <summary>"68 more moves · show all" expands in place rather than paging.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    private bool _expandChanges;

    /// <summary>Every snapshot, filtered or not — the strip's header counts states,
    /// not the subset a chip happens to be showing.</summary>
    public int TotalCount => _all.Length;

    public int AppliedCount => _all.Count(r => r.IsApplied);
    public int NamedCount => _all.Count(r => r.IsNamed);

    public bool HasSelection => Selected is not null;

    public string DetailHeaderText =>
        Selected is { } row ? $"DIFF · {Detail.Title}" : "DIFF";

    /// <summary>The third pane's caps header — "SNAPSHOT #48".</summary>
    public string AsideHeaderText =>
        Selected is { } row ? $"SNAPSHOT #{row.Number}" : "SNAPSHOT";

    // --- naming (O26, second pass) -------------------------------------------
    //
    // PINNING IS GONE, and the evidence is that it never did anything a name did not:
    // IsProtected was `Pinned || Name is set`, so naming alone already exempted a state
    // from every prune, and the Named chip filtered the same union. Its only independent
    // case — protected WITHOUT a name — was unreachable, because pinning auto-assigned
    // one. Two controls, one visible effect, and no way to tell which had caused it: that
    // is why unpinning looked like it did nothing.
    //
    // Naming is now the whole feature. A named state wears the star, appears under Named,
    // and survives Prune. Clearing the name undoes all three. The domain field went too,
    // once measurement showed all 63 stored snapshots carried `pinned: false` — there was
    // no legacy state for it to protect, only a second way to mean the same thing.

    /// <summary>Whether the aside's name is being edited — the ✎ opens it, ✓ commits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditingName))]
    private bool _isEditingName;

    public bool IsNotEditingName => !IsEditingName;

    /// <summary>The name being edited; committed by ✓ or Enter, never by typing.</summary>
    [ObservableProperty] private string _editName = string.Empty;

    /// <summary>What the aside shows when not editing — the name, or that there is none.</summary>
    public string NameDisplay =>
        Selected?.Snapshot.Name is { Length: > 0 } name ? name : "Unnamed";

    /// <summary>Greys the placeholder, so "Unnamed" cannot be mistaken for a name.</summary>
    public bool HasName => Selected?.Snapshot.Name is { Length: > 0 };

    /// <summary>
    /// What naming buys, said next to the control that does it. It was only ever stated
    /// in a tooltip on Prune, at the other end of the toolbar.
    /// </summary>
    public string NameNote => HasName
        ? "Named states appear under Named and are never pruned."
        : "Naming keeps this state out of every prune.";

    public HistoryDetail Detail => HistoryPresenter.BuildDetail(
        Selected, _snapshots, _names, _gameVersion, _rules, ExpandChanges);

    partial void OnShowAllChanged(bool value) { if (value) Exclusive(all: true); Refilter(); }
    partial void OnShowAppliedOnlyChanged(bool value) { if (value) Exclusive(applied: true); Refilter(); }
    partial void OnShowNamedChanged(bool value) { if (value) Exclusive(named: true); Refilter(); }

    private void Exclusive(bool all = false, bool applied = false, bool named = false)
    {
        if (!all) ShowAll = false;
        if (!applied) ShowAppliedOnly = false;
        if (!named) ShowNamed = false;
    }

    private HistoryFilter Filter =>
        ShowAppliedOnly ? HistoryFilter.AppliedOnly
        : ShowNamed ? HistoryFilter.Named
        : HistoryFilter.All;

    // --- empty states (v2 systemic pass) -------------------------------------
    // "No snapshots yet" used to bind !Rows.Count — the FILTERED list — so the
    // Named chip with zero named snapshots claimed there were no snapshots at all.

    public bool IsTrulyEmpty => TotalCount == 0;

    public bool IsFilteredEmpty => TotalCount > 0 && Rows.Count == 0;

    public string FilteredEmptyText =>
        ShowNamed
            ? $"No named snapshots — {TotalCount} others hidden by this chip."
            : $"No applied snapshots — {TotalCount} others hidden by this chip.";

    /// <summary>The filtered-empty state's way out.</summary>
    [RelayCommand]
    private void ClearFilters() => ShowAll = true;

    public void Populate(
        IReadOnlyList<ModlistSnapshot> newestFirst,
        IReadOnlyDictionary<string, long> sizes,
        IReadOnlyDictionary<ModId, string> names,
        DateTimeOffset now,
        string instanceName,
        string gameVersion,
        string rules)
    {
        _snapshots = newestFirst;
        _names = names;
        _gameVersion = gameVersion;
        _rules = rules;
        _all = HistoryPresenter.BuildRows(newestFirst, sizes, now);

        Summary = newestFirst.Count == 0
            ? $"{instanceName} · no snapshots yet"
            : $"{instanceName} · {HistoryPresenter.Total(newestFirst.Count, sizes)}";

        Refilter();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(NamedCount));
    }

    private void Refilter()
    {
        var keepId = Selected?.Snapshot.Id;

        Rows.Clear();
        foreach (var row in HistoryPresenter.Filter(_all, Filter)) Rows.Add(row);

        OnPropertyChanged(nameof(IsTrulyEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(FilteredEmptyText));

        Selected = keepId is null ? null : Rows.FirstOrDefault(r => r.Snapshot.Id == keepId);
        ExpandChanges = false;
    }
}
