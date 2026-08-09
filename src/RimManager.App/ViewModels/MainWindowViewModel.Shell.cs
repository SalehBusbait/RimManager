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
using RimManager.Integrations.Http;
using RimManager.Integrations.SteamCmd;
using RimManager.Integrations.Steamworks;
using RimManager.Storage;
using RimManager.Storage.Repositories;

namespace RimManager.App.ViewModels;

// One class, several files: the partial split keeps every binding path,
// notification and DI registration identical (N11 — see NEXT_PLAN's record).
public sealed partial class MainWindowViewModel
{
    // --- the shortcut sheet -------------------------------------------------

    /// <summary>
    /// Raised for Help ▸ Keyboard shortcuts and ⌘/. An event rather than a window,
    /// because the view model must stay constructible without one — the same
    /// arrangement the XML diff uses.
    /// </summary>
    public event Action<ShortcutSheetViewModel>? ShortcutSheetRequested;

    [RelayCommand]
    private void ShowShortcutSheet() =>
        ShortcutSheetRequested?.Invoke(new ShortcutSheetViewModel(ShortcutGesture.IsMac));

    // --- toolbar (1a, 1d, 3f) -----------------------------------------------

    /// <summary>Sort ▾: Topological (default) vs Alphabetical within separators.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseAlphabeticalSort))]
    private bool _useTopologicalSort = true;

    [ObservableProperty] private bool _snapshotBeforeSorting = true;

    /// <summary>Settings ▸ Sorting borrows this rather than opening its own path to the network.</summary>
    System.Windows.Input.ICommand IAppPreferences.SyncRules => SyncRulesCommand;

    /// <summary>Settings borrows the command rather than growing a second reset path.</summary>
    System.Windows.Input.ICommand IAppPreferences.ResetLaunchCommand => ResetLaunchCommandCommand;

    string IAppPreferences.LogLevelNote => LogLevelNote;
    string IAppPreferences.ScanCacheSummary => ScanCacheSummary;
    System.Windows.Input.ICommand IAppPreferences.OpenLogFolder => OpenLogFolderCommand;
    System.Windows.Input.ICommand IAppPreferences.ChooseTopologicalSort => ChooseTopologicalSortCommand;
    System.Windows.Input.ICommand IAppPreferences.ChooseAlphabeticalSort => ChooseAlphabeticalSortCommand;
    System.Windows.Input.ICommand IAppPreferences.CopyDiagnostics => CopyDiagnosticsBundleCommand;
    System.Windows.Input.ICommand IAppPreferences.ResetLayout => ResetWindowLayoutCommand;
    System.Windows.Input.ICommand IAppPreferences.RebuildScanCache => RebuildScanCacheCommand;
    System.Windows.Input.ICommand IAppPreferences.OpenBackupFolder => OpenBackupFolderCommand;
    System.Windows.Input.ICommand IAppPreferences.DeleteAllSnapshots => DeleteAllSnapshotsCommand;
    System.Windows.Input.ICommand IAppPreferences.ResetRimManager => ResetRimManagerCommand;
    System.Windows.Input.ICommand IAppPreferences.OpenRuleEditor => OpenRuleEditorCommand;

    /// <summary>
    /// Weekly community-rules check. The preference is honoured by the sync path when
    /// startup checking lands; it is stored and editable now so the setting is not
    /// invented later against a page that already shipped without it.
    /// </summary>
    [ObservableProperty] private bool _weeklyRuleCheck = true;

    /// <summary>Open the dock on Warnings when a sort breaks a cycle (2a behaviour).</summary>
    [ObservableProperty] private bool _openDockOnCycleBreak = true;

    /// <summary>
    /// OFF by default and it stays off (design non-negotiable #8). Activating a mod
    /// is how people explore a list; re-sorting under them on every click would move
    /// the row they were about to read.
    /// </summary>
    [ObservableProperty] private bool _autoSortAfterActivate;

    // --- 2k · adaptive layout ------------------------------------------------

    /// <summary>
    /// The window's current width, pushed in from the view on every resize. The one
    /// number both breakpoints are decided from, so there is no second opinion about
    /// which layout is in force.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutWidth), nameof(IsInfoOverlay), nameof(ShowInfoDrawer),
        nameof(IsSegmentedLayout), nameof(IsFullLayout), nameof(InfoOverlayWidth),
        // Every column the breakpoint collapses. A width that is merely correct and
        // never announced leaves the row rendering the layout it was born with.
        nameof(VersionColumnWidth), nameof(AuthorColumnWidth),
        nameof(RowPackageIdWidth), nameof(RowVersionWidth),
        nameof(RowChevronWidth))]
    private double _windowWidth = 1440;

    /// <summary>
    /// The width the LAYOUT actually gets, which is what the breakpoints are about.
    /// <para>
    /// Not the same as the window's width once UI scale is in play: at 150% on an
    /// 1180px window the app is laid out in 787 logical pixels, and every pane, column
    /// and row is competing for that, not for 1180. Deciding the breakpoints from the
    /// physical width left the app in the full three-pane layout at an effective 787px
    /// — narrower than the segmented breakpoint it should have crossed two steps back.
    /// </para>
    /// </summary>
    public double LayoutWidth => UiScaleFactor <= 0 ? WindowWidth : WindowWidth / UiScaleFactor;

    /// <summary>Below 1150 mod info is an overlay rather than the third pane.</summary>
    public bool IsInfoOverlay => Breakpoints.InfoIsOverlay(LayoutWidth);

    public bool IsFullLayout => Breakpoints.For(LayoutWidth) == WindowLayout.Full;

    public bool IsSegmentedLayout => Breakpoints.For(LayoutWidth) == WindowLayout.Segmented;

    public double InfoOverlayWidth => Breakpoints.OverlayWidth(LayoutWidth);

    /// <summary>
    /// Whether the drawer is showing. Only meaningful in overlay layouts — widening the
    /// window past the breakpoint docks the pane back, so this is cleared there rather
    /// than left set for the next time the window narrows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInfoDrawer))]
    private bool _isInfoDrawerOpen;

    public bool ShowInfoDrawer => IsInfoOverlay && IsInfoDrawerOpen;

    partial void OnWindowWidthChanged(double value)
    {
        LayoutWidthChanged?.Invoke(LayoutWidth);
        if (!IsInfoOverlay) IsInfoDrawerOpen = false;

        // 24px rows below 900 (2k). Re-applied on every width change rather than only
        // on a crossing, because ApplyDensity is idempotent and a missed crossing
        // leaves every row the wrong height with nothing to say so.
        ApplyDensity();
    }

    /// <summary>
    /// Raised when ⌘3 should move focus to the docked pane. The drawer needs no event —
    /// it is a state — but focusing a pane is a view concern.
    /// </summary>
    public event Action? InfoPaneFocusRequested;

    /// <summary>
    /// ⌘3. Opens and closes the drawer when mod info is an overlay; focuses the pane
    /// when it is docked. It resolved to NO COMMAND until R9 — the menu entry rendered
    /// disabled and the key binding did nothing.
    /// </summary>
    [RelayCommand]
    private void ToggleInfoPane()
    {
        if (IsInfoOverlay) IsInfoDrawerOpen = !IsInfoDrawerOpen;
        else InfoPaneFocusRequested?.Invoke();
    }

    /// <summary>Esc, and the drawer's own close button.</summary>
    [RelayCommand]
    private void CloseInfoDrawer() => IsInfoDrawerOpen = false;

    /// <summary>
    /// A row's `›` (<c>2k</c>). Opens, never toggles: the chevron also selects its row,
    /// so a toggle would close the sheet for anyone tapping down the list to read each
    /// one — the second tap would shut what the first opened.
    /// </summary>
    [RelayCommand]
    private void OpenInfoDrawer() => IsInfoDrawerOpen = true;

    /// <summary>
    /// How many filters are on, for the collapsed "Filters N ▾" button. Counts the tag
    /// filter as ONE however many tags are ticked: the button stands for the chips it
    /// replaced, and there are four of those.
    /// </summary>
    public int ActiveFilterCount =>
        (HasTagFilter ? 1 : 0) + (WarningsOnly ? 1 : 0);

    public string FilterButtonText =>
        ActiveFilterCount == 0 ? "Filters" : $"Filters {ActiveFilterCount}";

    // --- 2k · breakpoint 2, one segmented list -------------------------------

    /// <summary>
    /// Which of the two lists the segmented control is showing below 900px. There is
    /// no third list: the two panes already carry their own templates, footers, drag
    /// handling and empty states, so the narrow layout hides one rather than building
    /// a third that would have to be kept in step with both.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveSegment), nameof(IsInactiveSegment))]
    private bool _segmentShowsInactive;

    /// <summary>
    /// The segmented control's two halves. Written as a settable pair rather than a
    /// command, so the pressed state IS the selection and the two cannot disagree.
    /// </summary>
    public bool IsActiveSegment
    {
        get => !SegmentShowsInactive;
        set { if (value) SegmentShowsInactive = false; }
    }

    public bool IsInactiveSegment
    {
        get => SegmentShowsInactive;
        set { if (value) SegmentShowsInactive = true; }
    }

    /// <summary>"Active 214" / "Inactive 128" (<c>2k</c>). Both counts stay visible on
    /// the unselected half — the point of the control is to say what is on the other
    /// side of it.</summary>
    public string SegmentActiveLabel => $"Active {ActiveCount}";

    public string SegmentInactiveLabel => $"Inactive {InactiveCount}";

    /// <summary>How many rows are selected, for "Sort selection only 3".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectionCount;

    public bool HasSelection => SelectionCount > 0;

    // Every gesture comes from the one shortcut table (guide §6), so the window's
    // key bindings, the menu labels and the ⌘/ sheet cannot disagree.
    public static Avalonia.Input.KeyGesture? ApplyGesture =>
        ShortcutGesture.For(ShortcutTable.ApplyToGame);

    public static Avalonia.Input.KeyGesture? ApplyAndLaunchGesture =>
        ShortcutGesture.For(ShortcutTable.ApplyAndLaunch);

    // Eleven more KeyGesture properties stood here and NOTHING bound any of them —
    // Undo, Redo, Sort, Validate, ScanConflicts, CheckUpdates, Refresh, Dock,
    // Separator and ModInfoPane. The menu bar builds its own gestures straight from
    // MenuItemViewModel.Gesture, and the window's key bindings come from
    // ShortcutBindings, so these were a third route that nothing ever took. Counted
    // before deleting: zero references each.

    /// <summary>Sort ▾ radio pair. The two are mutually exclusive by construction.
    /// READ-ONLY display state now: the TwoWay-radio-on-an-inverse-property pattern
    /// wedged after a mode switch (the log-level bug's family — display state must
    /// fire nothing; the choice fires from the commands below).</summary>
    public bool UseAlphabeticalSort => !UseTopologicalSort;

    [RelayCommand] private void ChooseTopologicalSort() => UseTopologicalSort = true;
    [RelayCommand] private void ChooseAlphabeticalSort() => UseTopologicalSort = false;

    // --- empty states (3e) --------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InactiveIsEmptySuccess), nameof(InactiveIsEmptyNotYet))]
    [NotifyPropertyChangedFor(nameof(InactiveIsEmptyFiltered))]
    private EmptyState _inactiveEmptyState;

    // Every one of these yields to LoadFailed. After a failed scan the counts behind
    // EmptyState were never computed, so any story told from them is invented — and two
    // cards stacked in one pane is the other half of the same mistake.
    public bool InactiveIsEmptySuccess => !LoadFailed && InactiveEmptyState == EmptyState.Success;
    public bool InactiveIsEmptyNotYet => !LoadFailed && InactiveEmptyState == EmptyState.NotYet;
    public bool InactiveIsEmptyFiltered => !LoadFailed && InactiveEmptyState == EmptyState.FilteredOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveIsEmptyFiltered), nameof(ActiveIsEmptySuccess))]
    [NotifyPropertyChangedFor(nameof(ActiveIsEmptyNothingActive))]
    private EmptyState _activeEmptyState;

    public bool ActiveIsEmptyFiltered => !LoadFailed && ActiveEmptyState == EmptyState.FilteredOut;
    public bool ActiveIsEmptySuccess => !LoadFailed && ActiveEmptyState == EmptyState.Success;

    /// <summary>
    /// The active pane's empty state (C2): a load order with nothing in it.
    /// <para>
    /// Its own name rather than binding <see cref="ActiveIsEmptySuccess"/>, even though
    /// both read the same enum value. <c>EmptyStatePresenter</c> is pane-agnostic and
    /// classifies "no rows, no filter" as <c>Success</c> — which on the INACTIVE pane
    /// means "everything is active" and here would mean the exact opposite. An empty
    /// load order is not a success, and a shared name is what would tempt the next
    /// author to reuse the inactive pane's wording with it.
    /// </para>
    /// <para>
    /// Suppressed while nothing has been scanned: with no mods installed both panes are
    /// empty at once, and the inactive pane's "No mods found" already says why. Two
    /// cards saying the same thing side by side is the noise this avoids.
    /// </para>
    /// </summary>
    public bool ActiveIsEmptyNothingActive =>
        !LoadFailed && ActiveEmptyState == EmptyState.Success && InstalledCount > 0;

    /// <summary>Names every filter still narrowing the view (3e).</summary>
    [ObservableProperty] private string _filterSummary = string.Empty;

    /// <summary>
    /// How much each pane's filter is hiding (C4). Warnings says "340 warnings are
    /// hidden" and History says "12 others hidden by this chip"; the two mod panes named
    /// the filters and no number at all — and on a 560-mod install the difference between
    /// "a filter is on" and "a filter is hiding 559 mods" is the whole question of
    /// whether clearing it is worth it. SCREENS.md prescribes the count.
    /// <para>
    /// PER PANE, and that is the point: <see cref="FilterSummary"/> is one property bound
    /// by both, so appending a count to it would put the inactive pane's number in the
    /// active pane's card. The count is simply the pane's row total — FilteredOut only
    /// fires when nothing is visible, so hidden equals all of them.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _activeHiddenText = string.Empty;

    [ObservableProperty] private string _inactiveHiddenText = string.Empty;

    /// <summary>Recomputes both panes' empty states after any scan or filter change.</summary>
    private void RefreshEmptyStates()
    {
        // Favourites counts with Untagged: both are pseudo-tags, and a filter the
        // summary does not count is a filter the user is told is not running.
        var tagFilters = AllTags.Count(t => t.IsSelected)
            + (UntaggedOnly ? 1 : 0)
            + (FavouritesOnly ? 1 : 0);

        FilterSummary = EmptyStatePresenter.DescribeFilters(
            SearchText, WarningsOnly, tagFilters);

        InactiveEmptyState = EmptyStatePresenter.For(
            InactiveRows.Count, InactiveRows.Count(r => r.IsRowVisible), InstalledCount > 0);

        ActiveEmptyState = EmptyStatePresenter.For(
            ActiveRows.Count, ActiveRows.Count(r => r.IsRowVisible), InstalledCount > 0);

        // MODS, not rows: a separator hidden along with its emptied group would otherwise
        // be counted as one, and "74 mods hidden" over a list of 73 is the kind of number
        // this audit exists to catch. Only visible when O9 gave separators a way to hide.
        ActiveHiddenText = HiddenText(ActiveRows.Count(r => r is ModRowViewModel), "mod");
        InactiveHiddenText = HiddenText(InactiveRows.Count(r => r is ModRowViewModel), "mod");
    }

    private static string HiddenText(int hidden, string noun) =>
        hidden == 0 ? string.Empty : $"{Plural.Of(hidden, noun)} hidden.";

    /// <summary>The empty state's own escape hatch: lift everything at once.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        WarningsOnly = false;
        _suppressTagFilterNotify = true;
        UntaggedOnly = false;
        foreach (var tag in AllTags) tag.IsSelected = false;
        _suppressTagFilterNotify = false;
        NotifyTagFilterChanged();
    }

    // --- status bar zones (1a) ----------------------------------------------
    //
    // Zone 2's ✓ used to be a COMPUTED property off _communityRules with nothing
    // raising it, so the tick showed whatever it evaluated to when the window was
    // built and never changed again — a rules sync could not turn it on. It is an
    // announced property set in UpdateRulesStatus now.

    /// <summary>Zone 4: "snapshot #47".</summary>
    [ObservableProperty] private string _snapshotLabel = "no snapshot";

    /// <summary>
    /// Zone 5. The only place background progress appears — nothing modal for
    /// background work (1a). "idle" when nothing is running.
    /// </summary>
    [ObservableProperty] private string _activityText = "idle";

    /// <summary>Whether ANY background work is running — zone 5's progress bar.</summary>
    [ObservableProperty] private bool _hasActivity;

    private readonly List<ActivityClaim> _activity = [];

    /// <summary>
    /// Claims zone 5 for the length of a background operation.
    /// <para>
    /// Zone 5 is <c>1a</c>'s ONLY background-progress surface, and until this existed
    /// exactly ONE of the five operations that block for seconds drove it — the
    /// SteamCMD download. Checking 538 mods for updates, syncing the rules database
    /// and running Cecil over 543 mods all left it reading "idle" while they ran,
    /// because each had invented its own busy flag (<c>Updates.IsChecking</c>,
    /// <c>Conflicts.IsAnalyzing</c>, <c>IsBusy</c>) and only that one also thought to
    /// say so where the user looks. Caught in a screenshot: zone 1 read "Checking for
    /// updates…" beside a zone 5 reading "idle".
    /// </para>
    /// <para>
    /// A claim rather than an assignment, for two reasons. A new operation cannot
    /// forget to clear it — <c>using</c> does that, including on the exception path
    /// every one of these has. And two overlapping operations cannot fight: the
    /// newest claim is what shows, and the zone returns to idle only when the LAST is
    /// released. The SteamCMD path used to write "idle" in its own <c>finally</c>,
    /// which would have wiped the rescan running underneath it.
    /// </para>
    /// </summary>
    private ActivityClaim Activity(string label) => new(this, label);

    private void RefreshActivity()
    {
        ActivityText = _activity.Count == 0 ? "idle" : _activity[^1].Label;
        HasActivity = _activity.Count > 0;
    }

    /// <summary>
    /// One operation's hold on zone 5. <see cref="Label"/> is settable because a long
    /// operation legitimately changes what it is doing — SteamCMD provisions, then
    /// downloads — and re-claiming to say so would briefly show whatever was beneath.
    /// </summary>
    private sealed class ActivityClaim : IDisposable
    {
        private readonly MainWindowViewModel _vm;
        private string _label;

        internal ActivityClaim(MainWindowViewModel vm, string label)
        {
            _vm = vm;
            _label = label;
            vm._activity.Add(this);
            vm.RefreshActivity();
        }

        public string Label
        {
            get => _label;
            set
            {
                _label = value;
                _vm.RefreshActivity();
            }
        }

        public void Dispose()
        {
            if (!_vm._activity.Remove(this)) return;
            _vm.RefreshActivity();
        }
    }

    // --- menu bar (2h) ------------------------------------------------------

    /// <summary>
    /// The five menus, generated from <see cref="MenuModel"/> and
    /// <see cref="ShortcutTable"/> rather than authored in XAML. Built once: the rows
    /// are static, only their enabled/checked state changes.
    /// </summary>
    public ImmutableArray<MenuItemViewModel> Menus => _menus ??= MenuItemViewModel.BuildBar(CommandForShortcut);

    private ImmutableArray<MenuItemViewModel>? _menus;

    /// <summary>
    /// Maps a shortcut id to the command that runs it. Returning null leaves the row
    /// visible but disabled — <c>2h</c> shows disabled items still carrying their
    /// shortcut, because a greyed row with its key on it still teaches. Ids with no
    /// command yet are surfaces later phases build (the rule editor, snapshots,
    /// the palette's own window), and hiding them would misrepresent the product.
    /// </summary>
    private ICommand? CommandForShortcut(string id) => id switch
    {
        ShortcutTable.SortLoadOrder => SortCommand,
        ShortcutTable.ApplyToGame => RequestApplyCommand,
        ShortcutTable.CheckUpdates => CheckUpdatesCommand,
        ShortcutTable.ScanConflicts => AnalyzeConflictsCommand,
        // Tools ▸ Validate now rendered greyed while the Warnings tab's Revalidate
        // button ran this very command — the id was simply never joined up.
        ShortcutTable.ValidateNow => RevalidateCommand,
        ShortcutTable.SyncRules => SyncRulesCommand,
        ShortcutTable.RefreshFolders => RefreshCommand,
        ShortcutTable.ImportModList => ImportCommand,
        ShortcutTable.ImportCollection => ImportCollectionCommand,
        ShortcutTable.ExportModList => ExportCommand,
        ShortcutTable.ExportWorkshopItem => ExportWorkshopItemCommand,
        ShortcutTable.ExportCollection => ExportCollectionCommand,
        ShortcutTable.InsertSeparator => AddSeparatorCommand,
        ShortcutTable.RenameSeparator => RenameSeparatorCommand,
        ShortcutTable.Undo => UndoCommand,
        ShortcutTable.Redo => RedoCommand,
        ShortcutTable.BottomDock => ToggleDockCommand,
        ShortcutTable.ModInfoPane => ToggleInfoPaneCommand,
        ShortcutTable.Settings => OpenSettingsCommand,
        ShortcutTable.NewModlist => NewModlistCommand,

        // Opens Settings on the Modlists page, which is where renaming, duplicating,
        // making default and deleting all live. A second management surface would be a
        // second place for those rules to disagree.
        ShortcutTable.ManageModlists => OpenSettingsCommand,
        ShortcutTable.Quit => QuitCommand,
        ShortcutTable.ShortcutSheet => ShowShortcutSheetCommand,
        ShortcutTable.CheckAppUpdates => CheckAppUpdatesCommand,
        ShortcutTable.RerunFirstRun => RerunFirstRunCommand,
        ShortcutTable.About => ShowAboutCommand,
        ShortcutTable.ApplyAndLaunch => ApplyAndLaunchCommand,
        ShortcutTable.LaunchOnly => LaunchGameCommand,

        // The UI-audit wave: every id below rendered greyed (and its gesture dead)
        // while its operation existed somewhere else in the app — the R2a bug class,
        // at scale. Ids are wired here the day their command is real, never before.
        ShortcutTable.RuleEditor => OpenRuleEditorCommand,
        ShortcutTable.Snapshots => OpenSnapshotsCommand,
        ShortcutTable.CollapseAllGroups => CollapseAllCommand,
        ShortcutTable.ResetLayout => ResetWindowLayoutCommand,
        ShortcutTable.OpenLogFolder => OpenLogFolderCommand,
        ShortcutTable.CopyDiagnostics => CopyDiagnosticsBundleCommand,
        ShortcutTable.ActivateSelected => ActivateSelectedCommand,
        ShortcutTable.DeactivateSelected => DeactivateSelectedCommand,
        ShortcutTable.MoveUp => MoveSelectionUpCommand,
        ShortcutTable.MoveDown => MoveSelectionDownCommand,
        ShortcutTable.SelectAll => SelectAllActiveCommand,
        ShortcutTable.CopyPackageId => CopySelectedPackageIdsCommand,
        ShortcutTable.ToggleFavorite => ToggleFavouriteSelectedCommand,
        ShortcutTable.EditNote => EditNoteCommand,
        ShortcutTable.InactivePane => FocusInactivePaneCommand,
        ShortcutTable.FocusSearch => FocusSearchCommand,
        ShortcutTable.ReportIssue => ReportIssueCommand,
        ShortcutTable.DensityCompact => SetDensityCompactCommand,
        ShortcutTable.DensityComfortable => SetDensityComfortableCommand,
        ShortcutTable.FocusDockWarnings => FocusDockWarningsCommand,
        ShortcutTable.FocusDockUpdates => FocusDockUpdatesCommand,
        ShortcutTable.FocusDockHistory => FocusDockHistoryCommand,
        ShortcutTable.FocusDockActivity => FocusDockActivityCommand,
        ShortcutTable.SortAlphabetical => SortAlphabeticalCommand,
        ShortcutTable.SortTopological => SortTopologicalCommand,
        _ => null,
    };

    // --- the Edit/View/Tools/Help commands the audit found unrouted ----------

    /// <summary>Tools ▸ Snapshots… (Ctrl+Shift+H): the History tab IS the snapshots
    /// surface — append-only, restore appends (#5).</summary>
    [RelayCommand]
    private void OpenSnapshots() => RevealDock(DockHistory);

    [RelayCommand] private void FocusDockWarnings() => RevealDock(DockWarnings);
    [RelayCommand] private void FocusDockUpdates() => RevealDock(DockUpdates);
    [RelayCommand] private void FocusDockHistory() => RevealDock(DockHistory);
    [RelayCommand] private void FocusDockActivity() => RevealDock(DockActivity);

    [RelayCommand] private void SetDensityCompact() => IsComfortableDensity = false;
    [RelayCommand] private void SetDensityComfortable() => IsComfortableDensity = true;

    /// <summary>The INACTIVE pane's selection, pushed by the view like the active
    /// one's — the Edit menu's Activate needs to know what the inactive list holds.</summary>
    private IReadOnlyList<RowViewModel> _inactiveSelection = [];

    public void SetInactiveSelection(IReadOnlyList<RowViewModel> rows)
    {
        _inactiveSelection = rows;
        // O8: the assign flyout's heading and tri-states are about the SELECTION, so
        // they have to follow it rather than only the info pane's one mod.
        RefreshAssignRows();
    }

    [RelayCommand]
    private void ActivateSelected() =>
        ActivateMods(_inactiveSelection.OfType<ModRowViewModel>().ToList());

    [RelayCommand]
    private void DeactivateSelected() =>
        DeactivateMods(_activeSelection.OfType<ModRowViewModel>().ToList());

    /// <summary>Alt+Up / Alt+Down: one place through the same MoveRows a drag takes,
    /// so it is one undo entry, one snapshot, one status line — the drag contract.</summary>
    [RelayCommand]
    private void MoveSelectionUp() => NudgeSelection(up: true);

    [RelayCommand]
    private void MoveSelectionDown() => NudgeSelection(up: false);

    /// <summary>Raised after a keyboard nudge so the view can RE-SELECT the moved
    /// rows: MoveRows removes and reinserts them, which drops the ListBox selection —
    /// and a nudge that deselects its subject makes repeating the key useless.</summary>
    public event Action<IReadOnlyList<RowViewModel>>? ActiveReselectRequested;

    private void NudgeSelection(bool up)
    {
        var rows = _activeSelection
            .Where(ActiveRows.Contains)
            .OrderBy(ActiveRows.IndexOf)
            .ToList();
        if (rows.Count == 0) return;

        var first = ActiveRows.IndexOf(rows[0]);
        var last = ActiveRows.IndexOf(rows[^1]);
        if (up ? first <= 0 : last >= ActiveRows.Count - 1) return;

        // A contiguous block nudges by moving its NEIGHBOUR across it — one
        // collection Move, so the selected rows are never removed and the selection
        // survives untouched. NOT MoveRow: its same-spot guard compares the drop
        // index against the row's DISPLAYED number, and with a separator above the
        // two spaces collide — an up-nudge read as "landed where it started" and
        // silently did nothing, which is the bug the owner caught by hand.
        if (last - first + 1 == rows.Count)
        {
            ActiveRows.Move(up ? first - 1 : last + 1, up ? last : first);
            ActiveListOps.Renumber(ActiveRows);
            RefreshCounts();
            ApplyFilter();
            Validate();
            CommitChange();
            StatusText = rows.Count == 1
                ? $"Moved {(rows[0] is ModRowViewModel m ? m.Name : "the separator")} {(up ? "up" : "down")}."
                : $"Moved {rows.Count} rows {(up ? "up" : "down")}.";
        }
        else
        {
            // A gapped selection compacts into a block, exactly as a drag would.
            MoveRows(rows, ActivePane, ActivePane, up ? first - 1 : last + 2);
        }

        ActiveReselectRequested?.Invoke(rows);
    }

    /// <summary>Ctrl+A. Selection is the ListBox's; the view answers.</summary>
    public event Action? SelectAllRequested;

    [RelayCommand]
    private void SelectAllActive() => SelectAllRequested?.Invoke();

    [RelayCommand]
    private Task CopySelectedPackageIds() => CopyPackageIdsAsync(SelectionForEdit());

    [RelayCommand]
    private Task ToggleFavouriteSelected() => ToggleFavouriteAsync(SelectionForEdit());

    /// <summary>The Edit menu acts on whichever pane holds a selection, active first —
    /// the same row can only be in one.</summary>
    private IReadOnlyList<ModRowViewModel> SelectionForEdit()
    {
        var active = _activeSelection.OfType<ModRowViewModel>().ToList();
        return active.Count > 0 ? active : [.. _inactiveSelection.OfType<ModRowViewModel>()];
    }

    /// <summary>Ctrl+Shift+E: the note lives in Mod Info's NOTES box, so editing one
    /// means getting that box on screen and focused.</summary>
    public event Action? NoteFocusRequested;

    [RelayCommand]
    private void EditNote()
    {
        if (IsInfoOverlay) IsInfoDrawerOpen = true;
        NoteFocusRequested?.Invoke();
    }

    /// <summary>Focusing a pane is a view concern, so the view answers.</summary>
    public event Action? InactivePaneFocusRequested;

    /// <summary>
    /// Ctrl+1 — the mirror of Ctrl+3's two-mode shape (owner's call): in the
    /// SEGMENTED layout (&lt;900px, one pane with an Active/Inactive switch) it
    /// switches the segment to Inactive, which is the only way the keyboard can get
    /// there; in the wider layouts, where the pane is always on screen, it focuses it.
    /// </summary>
    [RelayCommand]
    private void FocusInactivePane()
    {
        if (IsSegmentedLayout) SegmentShowsInactive = true;
        else InactivePaneFocusRequested?.Invoke();
    }

    /// <summary>Ctrl+F. The search box lives on the toolbar; the view answers.</summary>
    public event Action? SearchFocusRequested;

    [RelayCommand]
    private void FocusSearch() => SearchFocusRequested?.Invoke();

    /// <summary>Help ▸ Report an issue ↗ — the external-link affordance it always
    /// wore, honoured at last.</summary>
    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            new ShellUriLauncher().Launch(AboutViewModel.ProjectUrl + "/issues");
        }
        catch (Exception ex)
        {
            StatusText = "Could not open the browser.";
            _log.Warn(LogSubsystem.Ui, $"Could not open the issues page: {ex}");
        }
    }

    // --- the app's own updates (beta.3) --------------------------------------
    // Constructed lazily and only here: the service is pure side-effect (network,
    // temp files, a process launch), so nothing else should be tempted to reach it.
    private AppUpdateService? _appUpdates;
    private AppUpdateService AppUpdates =>
        _appUpdates ??= new AppUpdateService(new HttpClientFetcher());

    /// <summary>
    /// The launch-time check: quiet, unawaited by the loader, offline-silent. With
    /// the auto-install preference ON and an installer-managed copy, it applies the
    /// update immediately; otherwise it says one status line and leaves the decision
    /// in the Help menu.
    /// </summary>
    private async Task CheckForAppUpdateOnLaunchAsync()
    {
        var advice = await AppUpdates.CheckAsync();
        if (advice is null) return;

        if (AutoInstallUpdates && advice.Installer is not null && AppUpdates.CanInstallInPlace)
        {
            StatusText = $"Updating to RimManager {advice.Version}…";
            if (await AppUpdates.DownloadAndRunInstallerAsync(advice))
            {
                Quit();
                return;
            }
        }

        StatusText = $"RimManager {advice.Version} is available — Help ▸ Check for updates…";
        _log.Info(LogSubsystem.Ui, $"Update available: {advice.Version}");
    }

    /// <summary>Help ▸ Check for updates… — the manual path, with a verdict either way.</summary>
    [RelayCommand]
    private async Task CheckAppUpdates()
    {
        if (Confirm is null) return;

        StatusText = "Checking for updates…";
        var advice = await AppUpdates.CheckAsync();

        if (advice is null)
        {
            StatusText = $"You are up to date — RimManager {AppUpdateService.CurrentVersion?.Split('+')[0]}.";
            return;
        }

        var canInstall = advice.Installer is not null && AppUpdates.CanInstallInPlace;
        var result = await Confirm(new ConfirmRequest(
            $"Update to RimManager {advice.Version}?",
            canInstall
                ? "Downloads the update and installs it in place. RimManager closes while "
                  + "the installer runs; your modlists, tags and settings are untouched."
                : "Opens the release page in your browser, where the download for this "
                  + "platform is listed.",
            Verb: canInstall ? "Update now" : "Open the release page"));
        if (!result.Confirmed)
        {
            StatusText = $"RimManager {advice.Version} is available — Help ▸ Check for updates…";
            return;
        }

        if (canInstall)
        {
            StatusText = $"Downloading RimManager {advice.Version}…";
            var started = await AppUpdates.DownloadAndRunInstallerAsync(
                advice, new Progress<double>(p =>
                    StatusText = $"Downloading RimManager {advice.Version}… {p:P0}"));
            if (started) { Quit(); return; }
            StatusText = "The download did not complete — nothing was changed.";
        }
        else
        {
            AppUpdates.OpenReleasePage(advice);
        }
    }

    // --- menu items that open or close a window ------------------------------
    // These raise events rather than touching a Window, for the same reason the XML
    // diff does: the view model has to stay constructible without one, which is what
    // makes everything else in it testable. The view subscribes.
    //
    // They also close a real gap: the menu became data-driven in R2a, and any id
    // missing from the switch above renders VISIBLE BUT DISABLED. These three had
    // code-behind handlers that nothing called any more, so File ▸ New Instance,
    // Settings and Quit had all been dead since that phase.

    /// <summary>Help ▸ Re-run first-time setup. Dead since R2a until R8.</summary>
    public event Action? FirstRunRequested;

    [RelayCommand]
    private void RerunFirstRun() => FirstRunRequested?.Invoke();

    /// <summary>
    /// Opens Settings, optionally ON a page. The page is the whole reason the argument
    /// exists: a footer link called "Manage tags…" that lands on Paths has told the user
    /// where to go and then not taken them there.
    /// </summary>
    public event Action<SettingsPage?>? SettingsRequested;

    public event Action? QuitRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(null);

    /// <summary>The Tags ▾ flyout's "Manage tags…" — Settings, on the page that edits them.</summary>
    [RelayCommand]
    private void ManageTags() => SettingsRequested?.Invoke(SettingsPage.TagsAndMetadata);

    /// <summary>The selector's "Manage…" — Settings, on the Modlists page. It used to
    /// open Settings on whatever page it happened to start on, with a tooltip naming the
    /// one it did not go to.</summary>
    [RelayCommand]
    private void ManageModlists() => SettingsRequested?.Invoke(SettingsPage.Modlists);

    /// <summary>
    /// File ▸ New modlist. Creates an empty list and opens it, so every installed mod
    /// starts inactive — "start from nothing", which is the only thing a new list can
    /// honestly mean.
    /// <para>
    /// Nothing is lost by it: the switch persists the outgoing arrangement before it
    /// changes anything, and the game is untouched until Apply. The status line says
    /// both, because a menu item that empties the load order without explaining itself
    /// would read as a catastrophe.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task NewModlist()
    {
        if (_modlistRepo is null) return;

        var name = ModlistsPresenter.CopyName(Modlists.Select(l => l.Name), "New list");
        var created = await _modlistRepo.CreateAsync(name);

        await RefreshModlistsAsync();
        await SwitchModlistAsync(created);

        StatusText =
            $"Created and opened “{name}” — empty, so every mod starts inactive. "
            + "Nothing was applied to the game.";
    }

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke();

    /// <summary>
    /// Exposed for the guard test: every menu id that the product presents as usable
    /// must resolve to a command, and "renders disabled" must be a deliberate choice
    /// rather than an oversight.
    /// </summary>
    public bool HasCommandFor(string shortcutId) => CommandForShortcut(shortcutId) is not null;

    /// <summary>
    /// The same resolver the menus use, for <see cref="ShortcutBindings"/>. Exposed so
    /// the window's key bindings are generated from the one table rather than written
    /// out again — eleven were, while the table held forty-six.
    /// </summary>
    public ICommand? CommandFor(string shortcutId) => CommandForShortcut(shortcutId);

    // --- density (View ▸ Density) -------------------------------------------

    /// <summary>
    /// Compact 20px is the default; comfortable is 26px; nothing between
    /// (design non-negotiable #10). Nothing reflows between the two, so switching
    /// mid-session never loses the user's place.
    /// </summary>
    [ObservableProperty] private bool _isComfortableDensity;

    partial void OnIsComfortableDensityChanged(bool value) => ApplyDensity();

    /// <summary>
    /// The whole density switch: one resource write re-lays out every list in the
    /// app, because every row binds its height to {DynamicResource RmRowHeight}.
    /// <para>
    /// Below 900px (<c>2k</c>) the narrow height wins over the density setting. That is
    /// the one height between the 20/26 pair non-negotiable #10 fixes, and it is
    /// deliberate: the segmented layout is a single list doing the work of two, on a
    /// laptop, and 2k specifies 24. Resolved here rather than in the view so there
    /// stays exactly ONE writer of RmRowHeight — two would disagree the first time a
    /// resize and a density change arrived in the wrong order.
    /// </para>
    /// </summary>
    private void ApplyDensity()
    {
        if (Application.Current is not { } app) return;

        var key = IsSegmentedLayout
            ? "RmRowHeightNarrow"
            : IsComfortableDensity ? "RmRowHeightComfortable" : "RmRowHeightCompact";

        if (!app.Resources.TryGetResource(key, app.ActualThemeVariant, out var height)) return;

        app.Resources["RmRowHeight"] = height;
    }
}
