using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Immutable;
using System.Windows.Input;
using RimManager.App.Shortcuts;
using RimManager.App.Services;
using RimManager.App.Themes;
using RimManager.Core.Domain;
using RimManager.Core.Git;
using RimManager.Core.Parsing;
using RimManager.Core.Sharing;
using RimManager.Core.Rules;
using RimManager.Core.Scanning;
using RimManager.Core.Sorting;
using RimManager.Core.Undo;
using RimManager.Core.Validation;
using RimManager.Core.Workshop;
using RimManager.Core.Writing;
using RimManager.Integrations.SteamCmd;
using RimManager.Integrations.Steamworks;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.App.ViewModels;

// One class, several files: the partial split keeps every binding path,
// notification and DI registration identical (N11 — see NEXT_PLAN's record).
public sealed partial class MainWindowViewModel
{
    // --- bottom dock (collapsible) -----------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DockRow))]
    private bool _isDockOpen;

    [ObservableProperty] private int _dockTabIndex;

    /// <summary>
    /// Tab order in the bottom dock. There is NO Cycles tab: design non-negotiable
    /// #7 makes cycles a category inside Warnings, because a broken dependency edge
    /// IS a validation warning, it only ever appears right after a sort, and a tab
    /// reading "0" for weeks trains people to ignore the whole strip.
    /// </summary>
    // Four tabs. Collection moved into the import wizard (2i-3), and Conflicts moved
    // onto the rows and the per-mod window (N6c) — the dock shows standing state, and
    // both were tasks you act on rather than counts you live with.
    private const int DockWarnings = 0, DockUpdates = 1,
                      DockHistory = 2, DockActivity = 3;

    private readonly DockGeometry _dockGeometry = new();

    /// <summary>The open body height of the CURRENT tab; the strip is not part of it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DockRow), nameof(DockHeightText))]
    private double _dockHeight = DockGeometry.DefaultBodyHeight;

    /// <summary>The detail-panel width of the current tab; the shell writes it back.</summary>
    [ObservableProperty] private double _dockDetailWidth = DockGeometry.DefaultDetailWidth(0);

    /// <summary>Live window height, pushed in by the window so the 50% cap is real.</summary>
    [ObservableProperty] private double _windowHeight = 800;

    /// <summary>The strip's right-hand readout, e.g. "520px".</summary>
    public string DockHeightText => $"{(int)Math.Round(DockHeight)}px";

    /// <summary>
    /// The dock region's grid row — the 26px strip plus the body when open.
    /// <para>
    /// Two-way, because the grip that resizes the dock sits <em>above</em> the strip:
    /// the strip and the body have to share one row for a <c>GridSplitter</c> to reach
    /// them, since a splitter only ever resizes the rows either side of itself.
    /// </para>
    /// </summary>
    public Avalonia.Controls.GridLength DockRow
    {
        get => IsDockOpen
            ? new Avalonia.Controls.GridLength(DockGeometry.StripHeight + DockHeight, Avalonia.Controls.GridUnitType.Pixel)
            : Avalonia.Controls.GridLength.Auto;
        set
        {
            if (!IsDockOpen || !value.IsAbsolute) return;
            DockHeight = DockGeometry.ClampBodyHeight(value.Value - DockGeometry.StripHeight, WindowHeight);
        }
    }

    partial void OnDockHeightChanged(double value)
    {
        _dockGeometry.BodyHeight = value;

        // O24 · a drag (or a window resize re-clamp) invalidates the restore point. It
        // used to survive, so ⤢ could return the dock to a height the user had left long
        // before — the behaviour that read as the button doing something random.
        if (!_settingDockHeight) _dockRestoreHeight = null;

        OnPropertyChanged(nameof(IsDockMaximised));
        OnPropertyChanged(nameof(DockMaximiseTip));
        QueueLayoutSave();
    }

    /// <summary>
    /// Re-clamps when the window resizes. Without this, a dock dragged tall on a
    /// maximised window stays tall after the window is restored — the 50% ceiling was
    /// only ever checked at drag time, so shrinking the window left the dock owning
    /// most of it with no way back except the splitter it had pushed off screen.
    /// </summary>
    partial void OnWindowHeightChanged(double value)
    {
        if (!IsDockOpen) return;

        var clamped = DockGeometry.ClampBodyHeight(DockHeight, value);
        if (Math.Abs(clamped - DockHeight) > 0.5) DockHeight = clamped;
    }

    partial void OnDockDetailWidthChanged(double value)
    {
        _dockGeometry.SetDetailWidth(DockTabIndex, value);
        QueueLayoutSave();
    }

    partial void OnDockTabIndexChanged(int value)
    {
        // O4: the HEIGHT no longer moves when you switch — that jump was the complaint.
        // The splitter position still does, because History's three panes make its
        // detail column a genuinely different measurement.
        DockDetailWidth = _dockGeometry.DetailWidthFor(value);
        QueueLayoutSave();
    }

    [RelayCommand]
    private void ToggleDock() => IsDockOpen = !IsDockOpen;

    // --- layout persistence (N11: LayoutState finally consumed) --------------

    /// <summary>Dock tab ids in strip order — LayoutState keys by id, the VM by index.</summary>
    private static readonly string[] DockTabIds = ["warnings", "updates", "history", "activity"];

    private SerialWriter<LayoutState>? _layoutWriter;
    private bool _layoutApplied;
    private bool _applyingLayout;

    /// <summary>
    /// Applies the persisted arrangement ONCE per session, after the first reload has
    /// built the rows and the tag filter chips it re-selects. Later reloads (F5, a
    /// modlist switch) must not stomp the live layout the user has since changed.
    /// </summary>
    private void ApplyLayout()
    {
        if (_layoutApplied || _state is null) return;

        var layout = _state.LoadLayout();
        _applyingLayout = true;
        try
        {
            if (layout.DockHeight is { } saved) _dockGeometry.BodyHeight = saved;

            for (var tab = 0; tab < DockTabIds.Length; tab++)
            {
                if (layout.DockDetailWidths.TryGetValue(DockTabIds[tab], out var width))
                    _dockGeometry.SetDetailWidth(tab, width);
            }

            var tabIndex = Array.IndexOf(DockTabIds, layout.DockTab);
            DockTabIndex = tabIndex >= 0 ? tabIndex : 0;
            DockHeight = DockGeometry.ClampBodyHeight(_dockGeometry.BodyHeight, WindowHeight);
            DockDetailWidth = _dockGeometry.DetailWidthFor(DockTabIndex);
            IsDockOpen = layout.IsDockOpen;

            // O17 · the pane splitters. Seeded so a later save round-trips them even if
            // the user never drags one this session; the view raises PaneWidthsRequested
            // to put them on screen, because the widths live in the markup's Grid.
            _savedInactivePaneWidth = layout.InactivePaneWidth;
            _savedInfoPaneWidth = layout.InfoPaneWidth;
            if (layout.InactivePaneWidth is { } left && layout.InfoPaneWidth is { } right)
                PaneWidthsRequested?.Invoke(left, right);

            WarningsOnly = layout.WarningsOnly;
            MatchAllTags = layout.MatchAllTags;

            if (!layout.ActiveTagFilters.IsDefaultOrEmpty)
            {
                var wanted = layout.ActiveTagFilters.ToHashSet(StringComparer.Ordinal);
                _suppressTagFilterNotify = true;
                try
                {
                    foreach (var tag in AllTags) tag.IsSelected = wanted.Contains(tag.Id);
                }
                finally
                {
                    _suppressTagFilterNotify = false;
                }
                NotifyTagFilterChanged();
            }
        }
        finally
        {
            _applyingLayout = false;
            _layoutApplied = true;
        }
    }

    private LayoutState BuildLayoutState()
    {
        var widths = ImmutableDictionary.CreateBuilder<string, double>();
        for (var tab = 0; tab < DockTabIds.Length; tab++)
            widths[DockTabIds[tab]] = _dockGeometry.DetailWidthFor(tab);

        return new LayoutState
        {
            IsDockOpen = IsDockOpen,
            DockTab = DockTabIds[Math.Clamp(DockTabIndex, 0, DockTabIds.Length - 1)],
            DockHeight = _dockGeometry.BodyHeight,
            DockDetailWidths = widths.ToImmutable(),
            ActiveTagFilters = [.. AllTags.Where(t => t.IsSelected).Select(t => t.Id)],
            MatchAllTags = MatchAllTags,
            WarningsOnly = WarningsOnly,

            // O17 · the window's own geometry. Carried through from whatever the view
            // last reported, so a layout save triggered by a dock drag cannot blank the
            // window bounds it knows nothing about.
            WindowX = _windowBounds?.X,
            WindowY = _windowBounds?.Y,
            WindowWidth = _windowBounds?.Width,
            WindowHeight = _windowBounds?.Height,
            WindowMaximised = _windowMaximised,
            InactivePaneWidth = _savedInactivePaneWidth,
            InfoPaneWidth = _savedInfoPaneWidth,
        };
    }

    // --- window geometry, reported by the view (O17) --------------------------

    private PlacementRect? _windowBounds;
    private bool _windowMaximised;

    // Named apart from the live InactivePaneWidth observable, which the inactive pane's
    // own SizeChanged drives for column arithmetic. Two writers on one property is how
    // R6's "two stores for one preference" went wrong; these are the SAVED values.
    private double? _savedInactivePaneWidth;
    private double? _savedInfoPaneWidth;

    /// <summary>
    /// Raised once, from <see cref="ApplyLayout"/>, asking the view to put the saved
    /// splitter positions on screen. An event rather than two bound properties because
    /// the widths live in <c>MainWindow.axaml</c>'s Grid as literals that a markup test
    /// pins — the restore has to overwrite them at runtime and leave the markup alone.
    /// </summary>
    public event Action<double, double>? PaneWidthsRequested;

    /// <summary>
    /// The view reports the window's RESTORED bounds and whether it is maximised.
    /// <para>
    /// Restored, never the maximised rectangle: un-maximising after a restart has to
    /// return to a size the user chose, and saving the maximised bounds as if they were
    /// normal is exactly how that goes wrong. The view is the only thing that can tell
    /// the two apart, so it does the telling.
    /// </para>
    /// </summary>
    public void ReportWindowGeometry(PlacementRect? restored, bool maximised)
    {
        // Null means "the view has no genuine restored bounds to give" — a window that
        // has been maximised for the whole session. Keeping the last real ones is the
        // point: overwriting them with the maximised rectangle is exactly the failure
        // this method's summary says it exists to prevent.
        if (restored is { } bounds) _windowBounds = bounds;
        _windowMaximised = maximised;
    }

    /// <summary>The two main splitters; the active list takes what is left.</summary>
    public void ReportPaneWidths(double inactive, double info)
    {
        if (inactive > 0) _savedInactivePaneWidth = inactive;
        if (info > 0) _savedInfoPaneWidth = info;
    }

    /// <summary>
    /// Saves the layout immediately rather than through the debounce, for the one
    /// caller that has no later chance: the window closing.
    /// <para>
    /// <c>layout.json</c> writes a timestamped backup on every save, so the ordinary
    /// path must stay debounced — a window drag is a stream of resize events, and
    /// unthrottled that reproduces the backup churn <c>PruneBackups</c> exists for.
    /// </para>
    /// </summary>
    public Task SaveLayoutNowAsync() =>
        _state is null || !_layoutApplied
            ? Task.CompletedTask
            : _state.SaveLayoutAsync(BuildLayoutState());

    /// <summary>
    /// Persists on every layout change, serialised latest-wins like settings and tags
    /// — a drag fires per-pixel, and the writer coalesces the churn. Quiet until the
    /// first apply, so startup's own property traffic does not write defaults back.
    /// </summary>
    private void QueueLayoutSave()
    {
        if (!_layoutApplied || _applyingLayout) return;
        _layoutWriter?.Queue(BuildLayoutState());
    }

    partial void OnIsDockOpenChanged(bool value) => QueueLayoutSave();

    [RelayCommand]
    private void CloseDock() => IsDockOpen = false;

    /// <summary>
    /// ⤢ — grow the dock to its 50% ceiling, and back again (O24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things made this read as erratic. The restore height was remembered when the
    /// button was pressed and <b>never invalidated</b>, so dragging the splitter
    /// afterwards left it pointing at a height from before: press ⤢ and the dock jumped
    /// somewhere the user had not been in a while. Worse, dragging the splitter TO the
    /// ceiling with no restore point recorded made the button a no-op — it set the
    /// restore height to the maximum and then set the height to the maximum.
    /// </para>
    /// <para>
    /// Now the restore point is cleared by any height change the button did not make (see
    /// <c>OnDockHeightChanged</c>), so it can only ever mean "the height you had
    /// immediately before you pressed this". With none recorded, restoring goes to the
    /// design's default, which is an answer rather than a nothing.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void ToggleDockMaximise()
    {
        var maximum = DockGeometry.MaximisedBodyHeight(WindowHeight);

        _settingDockHeight = true;
        try
        {
            if (IsDockMaximised)
            {
                var restore = _dockRestoreHeight ?? DockGeometry.DefaultBodyHeight;
                _dockRestoreHeight = null;
                DockHeight = DockGeometry.ClampBodyHeight(restore, WindowHeight);
                return;
            }

            _dockRestoreHeight = DockHeight;
            DockHeight = maximum;
        }
        finally
        {
            _settingDockHeight = false;
            OnPropertyChanged(nameof(IsDockMaximised));
            OnPropertyChanged(nameof(DockMaximiseTip));
        }
    }

    /// <summary>Whether the dock is already at its ceiling — what ⤢ will do next.</summary>
    public bool IsDockMaximised =>
        DockHeight >= DockGeometry.MaximisedBodyHeight(WindowHeight) - 1;

    /// <summary>
    /// The button said "Maximise the dock" in both states, so at full height it named the
    /// one thing it would not do.
    /// </summary>
    public string DockMaximiseTip =>
        IsDockMaximised ? "Restore the dock to its previous height" : "Maximise the dock (half the window)";

    private double? _dockRestoreHeight;

    /// <summary>True only while <see cref="ToggleDockMaximise"/> is writing the height,
    /// so its own write does not discard the restore point it just recorded.</summary>
    private bool _settingDockHeight;

    /// <summary>Opens the dock (if closed) and selects a tab by index.</summary>
    private void RevealDock(int index)
    {
        DockTabIndex = index;
        IsDockOpen = true;
    }

    /// <summary>
    /// Dock-strip buttons pass their index as a string (XAML CommandParameter).
    /// Clicking the tab that is already open closes the dock (README §Dock tab strip)
    /// — the strip stays, so nothing is lost and the toggle is where the eye already is.
    /// </summary>
    [RelayCommand]
    private void OpenDockTab(string index)
    {
        if (!int.TryParse(index, out var i)) return;
        if (IsDockOpen && DockTabIndex == i) { IsDockOpen = false; return; }
        RevealDock(i);
    }

    /// <summary>
    /// The row's status glyph, clicked: open the Warnings tab on the warning that glyph
    /// stands for (N2 · UI-4).
    /// <para>
    /// The glyph said "Has warnings" and stopped, which is what the coloured mark
    /// already said. Now it names them on hover and lands on them when clicked — and
    /// those are the two halves of the same complaint, that a warning was visible in
    /// one place and actionable in another.
    /// </para>
    /// <para>
    /// Does nothing when no warning names the mod. A row can carry the glyph for an
    /// update or a dirty working tree, neither of which is in this dock, and taking the
    /// user to a tab that does not mention their mod is worse than not moving.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void RevealWarningsFor(ModRowViewModel? row)
    {
        if (row is null) return;
        if (!WarningsPanel.SelectFor(row.PackageId)) return;

        RevealDock(DockWarnings);
    }

    // --- History tab (2d) ---------------------------------------------------

    /// <summary>
    /// "Restore this state" — <b>appends</b> a new snapshot whose contents equal the
    /// selected one (non-negotiable #5). Nothing in history is ever rewound; the old
    /// state stays exactly where it was and a new one is written after it.
    /// </summary>
    [RelayCommand]
    private async Task RestoreHistoryState()
    {
        if (History.Selected is not { } row) return;
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        SelectedModlist = await _modlistRepo.RestoreSnapshotAsync(list, row.Snapshot.Id);
        LoadActiveRows(row.Snapshot.State);
        CommitChange();
        RefreshHistory();
        StatusText = $"Appended #{row.Number}'s contents as a new state — nothing was rewound.";
        _log.Info(LogSubsystem.Io, $"History: restored #{row.Number} by appending a new snapshot");
    }

    /// <summary>✎ — opens the aside's name for editing, seeded with what is there.</summary>
    [RelayCommand]
    private void BeginHistoryRename()
    {
        if (History.Selected is not { } row) return;

        History.EditName = row.Snapshot.Name ?? string.Empty;
        History.IsEditingName = true;
    }

    /// <summary>Escape or ✕ — leaves the name exactly as it was.</summary>
    [RelayCommand]
    private void CancelHistoryRename() => History.IsEditingName = false;

    /// <summary>
    /// × beside the name — un-names the state in one press, without opening the editor
    /// to empty it by hand. Offered only when there is a name to remove.
    /// <para>
    /// Empty string, not null: null means "leave it alone" to the repository, which is
    /// the trap that made clearing impossible in the first pass.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryName()
    {
        if (History.Selected is not { } row) return;
        if (_modlistRepo is null || SelectedModlist is not { } list) return;
        if (row.Snapshot.Name is not { Length: > 0 }) return;

        History.IsEditingName = false;
        await _modlistRepo.AnnotateSnapshotAsync(list.Id, row.Snapshot.Id, name: string.Empty);

        RefreshHistory();
        StatusText = $"Cleared the name on #{row.Number} — it can be pruned again.";
    }

    /// <summary>
    /// ✓ or Enter — commits the typed name. O26, second pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinning is gone</b>, and the evidence is that it never did anything a name did
    /// not: <c>IsProtected</c> was <c>Pinned || Name is set</c>, so naming alone already
    /// exempted a state from every prune, and the Named chip filtered that same union.
    /// Its only independent case — protected with no name — was unreachable, because
    /// pinning auto-assigned one. Two controls, one visible effect, no way to tell which
    /// had caused it: which is exactly why unpinning looked inert. The domain field is
    /// gone too; all 63 stored snapshots carried <c>pinned: false</c>.
    /// </para>
    /// <para>
    /// Committed by a deliberate press, not by blur. The first pass saved on LostFocus,
    /// so the field was always live and a stray click through it wrote.
    /// </para>
    /// <para>
    /// The empty case needs care: <c>AnnotateSnapshotAsync</c> reads <c>name: null</c> as
    /// "leave it alone", and only an EMPTY STRING clears. The first pass passed null to
    /// mean "clear", which silently kept the old name — clearing was impossible.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task CommitHistoryRename()
    {
        if (History.Selected is not { } row) { History.IsEditingName = false; return; }
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        var typed = History.EditName?.Trim() ?? string.Empty;
        History.IsEditingName = false;
        if (typed == (row.Snapshot.Name ?? string.Empty)) return;

        // Empty string, never null: null means "unchanged" to the repository.
        await _modlistRepo.AnnotateSnapshotAsync(list.Id, row.Snapshot.Id, name: typed);

        RefreshHistory();
        StatusText = typed.Length == 0
            ? $"Cleared the name on #{row.Number} — it can be pruned again."
            : $"Named #{row.Number} “{typed}” — it will survive Prune.";
    }

    /// <summary>
    /// Prunes snapshots older than 30 days. Named states survive, which is the promise
    /// the History footer makes.
    /// </summary>
    [RelayCommand]
    private void PruneHistory()
    {
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        var removed = _modlistRepo.PruneOlderThan(list.Id, TimeSpan.FromDays(30));
        RefreshHistory();
        StatusText = removed == 0
            ? "Nothing older than 30 days to prune."
            : $"Pruned {removed} snapshot{(removed == 1 ? "" : "s")}; named states kept.";
    }

    /// <summary>"68 more moves · show all" expands the change list in place.</summary>
    [RelayCommand]
    private void ExpandHistoryChanges() => History.ExpandChanges = true;

    /// <summary>
    /// View ▸ Theme. Flips against what is on screen NOW rather than against the stored
    /// choice, because "follow system" has no opposite — toggling out of it has to mean
    /// "the other one from what I am looking at". With ten themes the toggle stays a
    /// light/dark flip WITHIN the Drop Pods pair (the same pairing follow-system uses);
    /// a flavoured theme is a destination the toggle deliberately leaves.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var actual = Application.Current?.ActualThemeVariant;
        var isLightNow = actual == ThemeVariant.Light
            || ThemeCatalog.All.Any(t => t.IsLight && t.Variant == actual);
        Theme = isLightNow ? AppTheme.DropPodsDark : AppTheme.DropPodsLight;
    }
}
