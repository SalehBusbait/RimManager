using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;
using RimManager.Core.Workshop;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the Updates dock tab (<c>2b</c>): the rows, the tri-state header checkbox and
/// its safe set, the selection summary and the detail panel. The network and compare
/// work lives in <c>UpdateCheckService</c>; the arrangement lives in the pure
/// <see cref="UpdatesPresenter"/>.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private ImmutableArray<ModUpdateStatus> _statuses = [];
    private SnoozeSet _snoozes = SnoozeSet.Empty;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private string? _gameVersion;

    public ObservableCollection<UpdateRowViewModel> Rows { get; } = [];

    // --- state filter (O25) ---------------------------------------------------
    //
    // Every other dock tab filters by category from chips in its toolbar; this one had
    // no filter at all, so a list mixing updates, delisted items and snoozed ones could
    // only be read by scanning the STATE column. Same exclusive shape as History's: a
    // state is one thing, not a set.

    private readonly List<UpdateRowViewModel> _allRows = [];

    [ObservableProperty] private bool _showAll = true;
    [ObservableProperty] private bool _showUpdatesOnly;
    [ObservableProperty] private bool _showSnoozedOnly;
    [ObservableProperty] private bool _showDelistedOnly;

    partial void OnShowAllChanged(bool value) { if (value) Exclusive(all: true); Refilter(); }
    partial void OnShowUpdatesOnlyChanged(bool value) { if (value) Exclusive(updates: true); Refilter(); }
    partial void OnShowSnoozedOnlyChanged(bool value) { if (value) Exclusive(snoozed: true); Refilter(); }
    partial void OnShowDelistedOnlyChanged(bool value) { if (value) Exclusive(delisted: true); Refilter(); }

    private void Exclusive(bool all = false, bool updates = false, bool snoozed = false, bool delisted = false)
    {
        if (!all) ShowAll = false;
        if (!updates) ShowUpdatesOnly = false;
        if (!snoozed) ShowSnoozedOnly = false;
        if (!delisted) ShowDelistedOnly = false;
    }

    /// <summary>Counts come from ALL rows, never the filtered view — a chip that
    /// counted only what it was already showing would read 0 for every chip but one.</summary>
    public int AllStatesCount => _allRows.Count;

    public int UpdatesStateCount => _allRows.Count(r => r is { IsUpdate: true, IsSnoozed: false });

    public int SnoozedStateCount => _allRows.Count(r => r.IsSnoozed);

    public int DelistedStateCount => _allRows.Count(r => r.IsDelisted);

    private void Refilter()
    {
        Rows.Clear();
        foreach (var row in _allRows)
        {
            var keep =
                ShowUpdatesOnly ? row is { IsUpdate: true, IsSnoozed: false }
                : ShowSnoozedOnly ? row.IsSnoozed
                : ShowDelistedOnly ? row.IsDelisted
                : true;

            if (keep) Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(FilteredEmptyText));
    }

    public bool HasVisibleRows => Rows.Count > 0;

    /// <summary>Said when a chip has emptied the table — never confused with
    /// "nothing to update", which is a different fact and has its own card.</summary>
    public string FilteredEmptyText => ShowUpdatesOnly ? "No mod has an update waiting."
        : ShowSnoozedOnly ? "Nothing is snoozed."
        : ShowDelistedOnly ? "Nothing has been delisted."
        : string.Empty;

    [ObservableProperty] private bool _isChecking;

    /// <summary>The Workshop update batch is running (the hub drives it). Separate
    /// from <see cref="IsChecking"/> because they gate different buttons; the
    /// toolbar's live strip shows for either.</summary>
    [ObservableProperty] private bool _isBatchRunning;

    public bool IsWorking => IsChecking || IsBatchRunning;

    partial void OnIsCheckingChanged(bool value) => OnPropertyChanged(nameof(IsWorking));
    partial void OnIsBatchRunningChanged(bool value) => OnPropertyChanged(nameof(IsWorking));
    [ObservableProperty] private bool _hasChecked;

    /// <summary>
    /// 2k · offline. The rows are the last answer Steam gave; they are no longer known
    /// to be current, so the tab badges them rather than clearing them. Wiping the
    /// table would be a global failure wearing a per-feature costume — the previous
    /// result is still the best information available.
    /// </summary>
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private int _updateCount;
    [ObservableProperty] private string _summary = "Not checked yet.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRow), nameof(SelectedTitle), nameof(SelectedFacts),
                              nameof(DetailHeaderText), nameof(CanOpenWorkshopPage))]
    private UpdateRowViewModel? _selectedRow;

    [ObservableProperty] private string _selectionSummary = "0 of 0 selected";

    /// <summary>Tri-state: true all safe rows, false none, null some.</summary>
    [ObservableProperty] private bool? _headerChecked = false;

    /// <summary>Live download strip, pushed right in the toolbar while a batch runs.</summary>
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadText = string.Empty;

    public bool HasSelectedRow => SelectedRow is not null;
    public string SelectedTitle => SelectedRow?.Name ?? string.Empty;

    /// <summary>
    /// What we can honestly say about the selected row. Deliberately facts rather than
    /// a changelog: Steam's keyless endpoints do not return one, and a panel headed
    /// "changelog" that invents entries is worse than one that says there is none.
    /// </summary>
    public string SelectedFacts
    {
        get
        {
            if (SelectedRow is not { } row) return string.Empty;

            // Most real mods declare no <modVersion>, and "Installed —" reads as a
            // rendering fault rather than as an absent field. Drop the clause instead.
            var parts = new List<string>();
            if (row.InstalledVersion != "—") parts.Add($"installed {row.InstalledVersion}");
            parts.Add($"published {row.PublishedText}");
            if (row.SizeText != "—") parts.Add(row.SizeText);

            var line = string.Join(" · ", parts);
            return char.ToUpperInvariant(line[0]) + line[1..];
        }
    }

    /// <summary>
    /// Checked, but nothing needs a decision. Distinct from "not checked yet": one is
    /// a result, the other is the absence of one, and 3e is explicit that reporting
    /// success you have not verified is the failure to avoid.
    /// </summary>
    // "Nothing to update" is about the CHECK's result, so it reads all rows — a chip
    // emptying the table is a different fact, and FilteredEmptyText says that one.
    public bool IsCleanAfterCheck => HasChecked && _allRows.Count == 0;

    /// <summary>
    /// The detail panel's caps header, naming the row it is about — "CHANGELOG ·
    /// CAMERA+" in <c>2b</c>. With nothing selected it names the panel instead of
    /// going blank, so the column never reads as broken.
    /// </summary>
    public string DetailHeaderText =>
        SelectedRow is { } row ? $"CHANGELOG · {row.Name.ToUpperInvariant()}" : "SELECTED UPDATE";

    /// <summary>Only a Workshop item has a page to open.</summary>
    public bool CanOpenWorkshopPage => SelectedRow?.PublishedFileId is not null;

    // O25 · counted over ALL rows, not the filtered view. A chip that hid a ticked row
    // would silently drop it from the batch while the button beside it still said
    // "Update N selected" — the tick survives the filter, so the count must too.
    public int SelectedCount => _allRows.Count(r => r.IsSelected);

    public string UpdateButtonText => $"Update {SelectedCount} selected";

    public bool CanUpdateSelection => SelectedCount > 0;

    // Source chips (2b). Counted over the rows on screen, and each chip hides itself
    // when its source is absent — a "Git 0" chip on a Workshop-only list is noise.
    public int WorkshopCount => _allRows.Count(r => r.Source == ModSource.Workshop);
    public int LocalCount => _allRows.Count(r => r.Source == ModSource.Local);
    public bool HasWorkshopRows => WorkshopCount > 0;

    /// <summary>The dock strip's accent tone: colour only when there is work.</summary>
    public bool HasUpdates => UpdateCount > 0;
    public bool HasLocalRows => LocalCount > 0;

    /// <summary>Replaces the rows from a completed check (ordered + summarized here).</summary>
    public void Populate(
        ImmutableArray<ModUpdateStatus> statuses,
        SnoozeSet snoozes,
        DateTimeOffset now,
        string? gameVersion)
    {
        _statuses = statuses;
        _snoozes = snoozes;
        _now = now;
        _gameVersion = gameVersion;

        Rows.Clear();
        _allRows.Clear();
        foreach (var status in UpdatesPresenter.Order(statuses))
        {
            // An expired snooze is not shown as a snooze — it is simply gone. Snoozes
            // expire by comparison, not by a timer (R1c).
            var snooze = snoozes.IsSnoozed(status.Id, now, status.InstalledVersion, gameVersion)
                ? snoozes.For(status.Id)
                : null;

            // A worklist, not an inventory — 344 "up to date" rows would bury the 21
            // that need a decision. The totals are still stated, in Summary.
            if (!UpdatesPresenter.IsWorthShowing(status, snooze is not null)) continue;

            var row = new UpdateRowViewModel(status, now, snooze);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(UpdateRowViewModel.IsSelected)) RefreshSelection();
            };
            _allRows.Add(row);
        }

        Refilter();
        OnPropertyChanged(nameof(AllStatesCount));
        OnPropertyChanged(nameof(UpdatesStateCount));
        OnPropertyChanged(nameof(SnoozedStateCount));
        OnPropertyChanged(nameof(DelistedStateCount));

        // A snoozed mod is not an update you are being asked about — that is what
        // snoozing means, and the strip's count pill has to agree with the tab.
        // HasUpdates drives the strip pill/icon's accent tone (T2): colour only
        // when there is something to act on. Counted from ALL rows, not the filtered
        // view: a chip must not be able to change the number on the tab.
        UpdateCount = _allRows.Count(r => r is { IsUpdate: true, IsSnoozed: false });
        Summary = UpdatesPresenter.Summarize(statuses);
        HasChecked = true;
        RefreshSelection();

        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(WorkshopCount));
        OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(HasWorkshopRows));
        OnPropertyChanged(nameof(HasLocalRows));
        OnPropertyChanged(nameof(IsCleanAfterCheck));
    }

    /// <summary>
    /// The header checkbox. Ticking it selects <b>only the safe set</b>; clearing it
    /// clears everything, including rows ticked by hand — a "clear" that left some
    /// ticked would misreport what it just did.
    /// </summary>
    public void ToggleHeader(bool select)
    {
        // Selecting takes the VISIBLE safe set — the header sits on the table you can
        // see. Clearing takes everything, filtered or not, because the promise above is
        // that a clear leaves nothing ticked, and a tick you cannot see is the worst
        // kind to leave behind.
        if (select)
        {
            foreach (var row in Rows) row.IsSelected = UpdatesPresenter.IsSafeToBatch(row);
        }
        else
        {
            foreach (var row in _allRows) row.IsSelected = false;
        }

        RefreshSelection();
    }

    /// <summary>Records a snooze for the selected row and rebuilds against it.</summary>
    public SnoozeSet Snooze(SnoozeKind kind)
    {
        if (SelectedRow is not { } row) return _snoozes;

        var status = _statuses.FirstOrDefault(s => s.Id == row.Id);
        var updated = _snoozes.With(new ModSnooze(
            row.Id, kind, _now,
            AtModVersion: status?.InstalledVersion,
            AtGameVersion: _gameVersion));

        Populate(_statuses, updated, _now, _gameVersion);
        return updated;
    }

    /// <summary>Un-snoozes the selected row.</summary>
    public SnoozeSet Unsnooze()
    {
        if (SelectedRow is not { } row) return _snoozes;

        var updated = _snoozes.Without(row.Id);
        Populate(_statuses, updated, _now, _gameVersion);
        return updated;
    }

    /// <summary>The live Workshop publish time from the last check — the updater's
    /// finish line: the acf catching up to this is what "updated" means.</summary>
    public DateTimeOffset? RemoteUtcFor(string publishedFileId) =>
        _statuses.FirstOrDefault(s => s.PublishedFileId == publishedFileId)?.RemoteUtc;

    /// <summary>The ids a batch update would touch, in display order.</summary>
    /// <remarks>
    /// O25 · ALL rows. This is what actually gets updated, and reading the filtered view
    /// would mean a chip could quietly remove a ticked mod from the batch between the
    /// user ticking it and pressing the button.
    /// </remarks>
    public ImmutableArray<string> SelectedPublishedFileIds =>
        [.. _allRows.Where(r => r is { IsSelected: true, PublishedFileId: not null })
                    .Select(r => r.PublishedFileId!)];

    private void RefreshSelection()
    {
        // The SUMMARY is about the batch, so it counts everything ticked; the HEADER tick
        // is the header of the table you can see, so it reflects the visible rows.
        SelectionSummary = UpdatesPresenter.SelectionSummary(_allRows);
        HeaderChecked = UpdatesPresenter.HeaderState(Rows) switch
        {
            TriState.All => true,
            TriState.None => false,
            _ => null,
        };

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(CanUpdateSelection));
    }
}
