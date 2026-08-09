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
    /// <summary>Builds the Settings view-model for the current install (null if none loaded).</summary>
    public SettingsViewModel? BuildSettings()
    {
        if (_installPaths is not { } paths) return null;
        // The save deliberately does NOT update _installPaths: the hub's copy staying
        // stale while Settings edits is what makes the close-time comparison in
        // ReloadPathsAsync able to see that something changed and rescan.
        return new SettingsViewModel(
            // The CARD's line, not the status bar's: it is the one that has to
            // acknowledge a failed sync, because the pill beside it already alarms.
            paths, RulesCardStatus,
            p => _workspace.SavePathsAsync(p),
            _workspace.FileSystem,
            _workspace.DetectPaths,
            this,
            LoadIntegrationStatusAsync,
            _integrations.InstallSteamCmdAsync,
            _gitTrackedCount,
            this,
            this);
    }

    /// <summary>
    /// Measures the Integrations page. The git counts come from the scan's own results
    /// rather than a fresh probe, so the card and the ⎇ glyphs on the rows are the same
    /// measurement — two probes would eventually disagree and neither would be wrong.
    /// </summary>
    private Task<IntegrationStatus> LoadIntegrationStatusAsync() =>
        _integrations.LoadAsync(
            _installPaths?.WorkshopDir,
            _gitTrackedCount,
            _gitStatuses.Count(kv => kv.Value.IsDirty));

    // --- preference persistence ---------------------------------------------

    private readonly AppSettingsRepository _settingsRepo;

    /// <summary>
    /// True while preferences are being applied from disk. Every one of them applies as it
    /// is set, so without this the load would write the file back seventeen times on
    /// startup — and a half-applied load interrupted by a crash would persist itself.
    /// </summary>
    private bool _loadingSettings;

    /// <summary>
    /// Restores the saved preferences. Assigned through the same properties the UI writes,
    /// so loading and choosing take the identical path — a separate "apply on startup"
    /// route is how a preference ends up working only when set by hand.
    /// </summary>
    private void LoadSettings()
    {
        var s = _settingsRepo.Load();
        _loadingSettings = true;
        try
        {
            // ThemeCatalog.Parse, not a bare Enum.TryParse: the legacy "Light"/"Dark"
            // names map to the Drop Pods pair instead of resetting to follow-system.
            Theme = ThemeCatalog.Parse(s.Theme);
            RefreshThemeBrand(Theme);
            FontIndex = UiFonts.Clamp(s.FontIndex);
            UiScalePercent = Math.Clamp(s.UiScalePercent, 80, 150);
            IsComfortableDensity = s.IsComfortableDensity;
            ShowTagStripes = s.ShowTagStripes;
            ZebraStriping = s.ZebraStriping;
            ShowPreviewImages = s.ShowPreviewImages;

            UseTopologicalSort = s.UseTopologicalSort;
            SnapshotBeforeSorting = s.SnapshotBeforeSorting;
            OpenDockOnCycleBreak = s.OpenDockOnCycleBreak;
            AutoSortAfterActivate = s.AutoSortAfterActivate;
            UseCommunityRules = s.UseCommunityRules;
            UseReplacementsDatabase = s.UseReplacementsDatabase;
            UseKnownGoodDatabase = s.UseKnownGoodDatabase;

            ShowGitDirtyOnRows = s.ShowGitDirtyOnRows;
            FetchReposOnStartup = s.FetchReposOnStartup;
            CheckModUpdatesOnStartup = s.CheckModUpdatesOnStartup;
            LaunchCommand = s.LaunchCommand;
            LaunchExtraArguments = s.LaunchExtraArguments;
            ConfirmBeforeApply = s.ConfirmBeforeApply;
            RefuseApplyWithBlockingWarnings = s.RefuseApplyWithBlockingWarnings;
            AutoInstallUpdates = s.AutoInstallUpdates;
            LogLevelIndex = LogLevels.Clamp(s.LogLevelIndex);
            KeepSnapshots = s.KeepSnapshots;
            CommunityRulesUrl = s.CommunityRulesUrl;
            ReplacementsUrl = s.ReplacementsUrl;
            KnownGoodBaseUrl = s.KnownGoodBaseUrl;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    /// <summary>
    /// Writes the current preferences. Called from the change notification of every
    /// preference rather than from each setter, so a new preference is persisted the
    /// moment it is added — forgetting to call this is not a mistake that can be made.
    /// </summary>
    private void SaveSettings()
    {
        if (_loadingSettings || !_initialized) return;

        var settings = new AppSettings
        {
            Theme = Theme.ToString(),
            FontIndex = FontIndex,
            UiScalePercent = UiScalePercent,
            IsComfortableDensity = IsComfortableDensity,
            ShowTagStripes = ShowTagStripes,
            ZebraStriping = ZebraStriping,
            ShowPreviewImages = ShowPreviewImages,
            UseTopologicalSort = UseTopologicalSort,
            SnapshotBeforeSorting = SnapshotBeforeSorting,
            OpenDockOnCycleBreak = OpenDockOnCycleBreak,
            AutoSortAfterActivate = AutoSortAfterActivate,
            UseCommunityRules = UseCommunityRules,
            UseReplacementsDatabase = UseReplacementsDatabase,
            UseKnownGoodDatabase = UseKnownGoodDatabase,
            ShowGitDirtyOnRows = ShowGitDirtyOnRows,
            FetchReposOnStartup = FetchReposOnStartup,
            CheckModUpdatesOnStartup = CheckModUpdatesOnStartup,
            LaunchCommand = LaunchCommand,
            LaunchExtraArguments = LaunchExtraArguments,
            ConfirmBeforeApply = ConfirmBeforeApply,
            RefuseApplyWithBlockingWarnings = RefuseApplyWithBlockingWarnings,
            AutoInstallUpdates = AutoInstallUpdates,
            LogLevelIndex = LogLevelIndex,
            KeepSnapshots = KeepSnapshots,
            CommunityRulesUrl = CommunityRulesUrl,
            ReplacementsUrl = ReplacementsUrl,
            KnownGoodBaseUrl = KnownGoodBaseUrl,
        };

        _settingsWriter.Queue(settings);
    }

    /// <summary>
    /// Serialised, latest-wins, and it swallows nothing quietly. Preferences change on
    /// every toggle, far faster than a disk round-trip, so concurrent writes reordered
    /// and the file kept a stale snapshot until this was serialised.
    /// </summary>
    private readonly SerialWriter<AppSettings> _settingsWriter;

    /// <summary>The same treatment for tags, which save on every keystroke in the
    /// condition editor — two overlapping writes to tags.json crashed the app.</summary>
    private readonly SerialWriter<TagSet> _tagWriter;

    /// <summary>
    /// Every preference is persisted from ONE place: whichever one changed. Listing the
    /// names here rather than adding a call to seventeen setters means a preference that
    /// is added later is saved automatically, and cannot be half-wired.
    /// </summary>
    private static readonly HashSet<string> PersistedPreferences =
    [
        nameof(Theme), nameof(FontIndex), nameof(IsComfortableDensity),
        nameof(UiScalePercent),
        nameof(ShowTagStripes), nameof(ZebraStriping), nameof(ShowPreviewImages),
        nameof(UseTopologicalSort),
        nameof(SnapshotBeforeSorting), nameof(OpenDockOnCycleBreak),
        nameof(AutoSortAfterActivate),
        nameof(UseCommunityRules), nameof(UseReplacementsDatabase), nameof(UseKnownGoodDatabase),
        nameof(ShowGitDirtyOnRows), nameof(FetchReposOnStartup), nameof(CheckModUpdatesOnStartup),
        nameof(LaunchCommand), nameof(LaunchExtraArguments),
        nameof(ConfirmBeforeApply), nameof(RefuseApplyWithBlockingWarnings),
        nameof(AutoInstallUpdates),
        nameof(LogLevelIndex), nameof(KeepSnapshots),
        nameof(CommunityRulesUrl), nameof(ReplacementsUrl), nameof(KnownGoodBaseUrl),
    ];

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && PersistedPreferences.Contains(name)) SaveSettings();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // Before anything is shown: the theme and accent are applied as they are set, so
        // loading later would flash the default and then correct itself.
        LoadSettings();

        StatusText = "Detecting RimWorld install…";
        var paths = await _workspace.EnsurePathsAsync();
        if (paths is null)
        {
            NeedsFirstRun = true;
            StatusText = "Welcome — let's set up your RimWorld install.";
            return;
        }

        _installPaths = paths;
        // Unawaited, as the instance-selection setter used to be: startup returns to the
        // caller while the scan claims the load state and reports through it.
        _ = ReloadAsync();
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync();

    // --- integrations (2g) --------------------------------------------------


    /// <summary>
    /// The <c>⎇</c> glyph on rows with uncommitted changes. On by default, and it
    /// drives the row status directly rather than describing an intention: flipping it
    /// re-runs validation, which is what re-applies every row's single status slot.
    /// </summary>
    [ObservableProperty] private bool _showGitDirtyOnRows = true;

    /// <summary>Fetch tracked repos when an instance loads. Off by default: one network
    /// call per repo, on every launch.</summary>
    [ObservableProperty] private bool _fetchReposOnStartup;

    /// <summary>
    /// Check the Workshop for mod updates when an instance loads. Off by default and
    /// offered in first run (2j step 4), which states the cost: one batched query for
    /// every Workshop item, about two seconds.
    /// </summary>
    [ObservableProperty] private bool _checkModUpdatesOnStartup;

    // --- game launch (2g) ---------------------------------------------------

    /// <summary>
    /// Empty means "not chosen yet" rather than "run nothing": the default depends on the
    /// instance, so it is filled in once one is loaded and can then be edited freely.
    /// </summary>
    [ObservableProperty] private string _launchCommand = string.Empty;

    [ObservableProperty] private string _launchExtraArguments = string.Empty;

    // --- tags & metadata (2g) -----------------------------------------------

    // --- advanced (2g) ------------------------------------------------------

    /// <summary>Raise the inline bar rather than writing straight away. On by default.</summary>
    [ObservableProperty] private bool _confirmBeforeApply = true;

    /// <summary>Refuse to Apply while blocking warnings exist. On by default.</summary>
    [ObservableProperty] private bool _refuseApplyWithBlockingWarnings = true;

    /// <summary>
    /// Install app updates on launch without asking. Off by default. The quiet check
    /// runs on every launch regardless; this decides whether its verdict becomes an
    /// action or just a status line pointing at Help ▸ Check for updates.
    /// </summary>
    [ObservableProperty] private bool _autoInstallUpdates;

    /// <summary>Unprotected snapshots kept per profile; named and pinned are exempt.</summary>
    [ObservableProperty] private int _keepSnapshots = 100;

    /// <summary>The log's level floor, indexing <see cref="LogLevels.Choices"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogLevelNote))]
    private int _logLevelIndex = LogLevels.DefaultIndex;

    /// <summary>Applied to the live log at once — the setting IS the floor, not a
    /// description of one, so the Activity tab changes as you move it. Announced at a
    /// level the new floor admits, or the change silences its own witness — see
    /// <see cref="LogLevels.ApplyFloor"/>.</summary>
    partial void OnLogLevelIndexChanged(int value) => LogLevels.ApplyFloor(_log, value);

    public string LogLevelNote => LogLevels.Note(LogLevelIndex);

    /// <summary>The command this install would get if it had never been edited.</summary>
    public string DefaultLaunchCommand => GameLaunch.DefaultTemplate(
        _installPaths?.GameDir,
        isSteamInstall: _installPaths?.WorkshopDir is not null,
        steamExe: _workspace.LocateSteamExecutable());

    [RelayCommand]
    private void ResetLaunchCommand()
    {
        LaunchCommand = DefaultLaunchCommand;
        LaunchExtraArguments = string.Empty;
        StatusText = "Launch command reset.";
    }

    /// <summary>
    /// Tools ▸ Launch RimWorld. Detached — RimManager does not supervise the game, and
    /// waiting on it would freeze the window for the length of a play session.
    /// </summary>
    [RelayCommand]
    private void LaunchGame()
    {
        if (GameLaunch.Parse(LaunchCommand, LaunchExtraArguments) is not { } plan)
        {
            StatusText = "No launch command set — Settings ▸ Integrations ▸ Game launch.";
            return;
        }

        _log.Info(LogSubsystem.Ui,
            $"Launching: {plan.FileName} {string.Join(' ', plan.Arguments)}");

        var error = new GameProcessLauncher().Launch(
            plan.FileName, plan.Arguments, _installPaths?.GameDir);

        if (error is null)
        {
            StatusText = "Launching RimWorld…";
            return;
        }

        // Name the program that could not be found. "Could not launch" alone sends the
        // user looking at RimWorld, when the thing that is missing is usually Steam.
        _log.Warn(LogSubsystem.Ui, $"Launch failed for '{plan.FileName}': {error}");
        // D5 · curly on screen; the log line above keeps ASCII, where it is conventional.
        StatusText = $"Could not launch “{plan.FileName}” — {error} "
            + "Check Settings ▸ Integrations ▸ Game launch.";
    }

    /// <summary>
    /// ⌘↵ — writes the load order, then starts the game, and <b>only</b> if the write
    /// succeeded. Launching after a failed Apply would start RimWorld on the old list
    /// while the UI showed the new one.
    /// </summary>
    [RelayCommand]
    private void ApplyAndLaunch()
    {
        _launchAfterWrite = true;
        RequestApplyCommand.Execute(null);

        // Apply is an inline bar, never a modal (#4), so this does not launch here — it
        // arms the intent and the bar's Write carries it out. If the user cancels the
        // bar, or blocking warnings stop it, the game must not start.
        if (!Commit.IsVisible) _launchAfterWrite = false;
    }

    /// <summary>Set by ⌘↵, cleared the moment the write finishes or the bar goes away.</summary>
    private bool _launchAfterWrite;

    partial void OnShowGitDirtyOnRowsChanged(bool value)
    {
        _lastValidationReason = "git glyph toggled";
        Validate();
    }

    // --- appearance (2g) ----------------------------------------------------

    /// <summary>The 3px tag stripe in the mod lists. On by default (1e §4).</summary>
    [ObservableProperty] private bool _showTagStripes = true;

    /// <summary>
    /// Off by default: at 20px rows the fixed columns already give the eye a grid,
    /// and banding a 400-row list adds noise the alignment does not need.
    /// </summary>
    [ObservableProperty] private bool _zebraStriping;

    /// <summary>Preview images in the info pane. On by default; the pane collapses to 0 without one.</summary>
    [ObservableProperty] private bool _showPreviewImages = true;

    partial void OnShowTagStripesChanged(bool value) => ApplyTagStripesToRows();

    /// <summary>
    /// Undo/Redo tooltips NAME the action ("Undo: move 4 mods"), so "what would this
    /// undo" never needs a guess (1a).
    /// </summary>
    public string UndoLabel => CanUndo ? "Undo last change" : "Nothing to undo";
    public string RedoLabel => CanRedo ? "Redo" : "Nothing to redo";

    /// <summary>
    /// The dimmed footer line in the Apply ▾ flyout. Its markup has always claimed to
    /// answer "is there anything to apply" and was
    /// <c>"N active · M installed"</c> — an inventory, which answers no such thing, and
    /// which sat two inches from a footer answering the same question differently. The
    /// installed count also went: nothing announced it, so it was half-wired as well.
    /// </summary>
    public string PendingDiffText => DriftIndicator.ApplyFlyout(Drift, ActiveCount);

    /// <summary>Chip label: "Tags" alone, or "Tags: 2" once a filter is on (1a).
    /// Untagged counts as one — it is a pseudo-tag, so it shares the grammar.</summary>
    public string TagFilterLabel =>
        SelectedTagFilterCount > 0 ? $"Tags: {SelectedTagFilterCount}" : "Tags";

    private int SelectedTagFilterCount =>
        AllTags.Count(t => t.IsSelected) + (UntaggedOnly ? 1 : 0) + (FavouritesOnly ? 1 : 0);

    /// <summary>
    /// Computed, never stored (N4g): the chip is lit while the filter RUNS, so it cannot
    /// report a filter that does not exist — which is what the stored bool did, set by
    /// the very click that opened the flyout.
    /// </summary>
    public bool HasTagFilter => SelectedTagFilterCount > 0;

    /// <summary>Untagged is a pseudo-tag, so one control covers all of a mod's metadata.</summary>
    [ObservableProperty] private bool _untaggedOnly;

    partial void OnUntaggedOnlyChanged(bool value) => NotifyTagFilterChanged();

    /// <summary>Favourites, the second pseudo-tag (O14). Favourite is metadata a mod
    /// carries the way a tag is, so it belongs in the one control that covers metadata
    /// rather than as a chip of its own.</summary>
    [ObservableProperty] private bool _favouritesOnly;

    partial void OnFavouritesOnlyChanged(bool value) => NotifyTagFilterChanged();

    public int FavouriteCount => _favouriteIds.Count;

    /// <summary>
    /// The flyout's own search box (O1). Filters WHICH TAG ROWS are listed — it never
    /// touches the mod lists, which the toolbar's search already does. Nineteen tags
    /// overflowed a flyout with no scroll and no way to narrow.
    /// </summary>
    [ObservableProperty] private string _tagSearch = string.Empty;

    partial void OnTagSearchChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(TagSearchFoundNothing));
        OnPropertyChanged(nameof(TagSearchEmptyMessage));
    }

    /// <summary>
    /// The tag rows the flyout shows: every tag, or those matching the search. A ticked
    /// tag the search excludes stays IN the filter — narrowing the list must not lift a
    /// filter the user cannot currently see, which is why the chip count reads from
    /// AllTags and not from here.
    /// </summary>
    public IReadOnlyList<TagFilterViewModel> VisibleTags =>
        string.IsNullOrWhiteSpace(TagSearch)
            ? AllTags
            : [.. AllTags.Where(t => t.Name.Contains(TagSearch.Trim(), StringComparison.OrdinalIgnoreCase))];

    /// <summary>Said instead of an empty flyout, naming the search that emptied it.</summary>
    public bool TagSearchFoundNothing => VisibleTags.Count == 0 && AllTags.Count > 0;

    public string TagSearchEmptyMessage => $"No tag matches “{TagSearch.Trim()}”.";

    /// <summary>Match all (every ticked tag) vs the default Match any. The persistence
    /// slot is <c>LayoutState.MatchAllTags</c>, which waits on layout persistence as a
    /// whole — nothing calls <c>LoadLayout</c> yet, so wiring one field would be the
    /// half-wire N4g exists to remove.</summary>
    [ObservableProperty] private bool _matchAllTags;

    partial void OnMatchAllTagsChanged(bool value)
    {
        ApplyFilter();
        QueueLayoutSave();
    }

    [RelayCommand] private void UseMatchAny() => MatchAllTags = false;
    [RelayCommand] private void UseMatchAll() => MatchAllTags = true;

    /// <summary>The flyout's Clear: lifts the tag filter only — mode is a preference,
    /// not a filter, and the other chips are not this flyout's to reset.</summary>
    [RelayCommand]
    private void ClearTagFilters()
    {
        _suppressTagFilterNotify = true;
        foreach (var tag in AllTags) tag.IsSelected = false;
        UntaggedOnly = false;
        FavouritesOnly = false;
        _suppressTagFilterNotify = false;
        NotifyTagFilterChanged();
    }

    // A tag row toggles itself: TagFilterViewModel.ToggleCommand. The row is inside a
    // popup, where an ancestor binding back to a command here would fail silently.
    // The two pseudo-tags below are NOT templated rows — they are literal markup in the
    // flyout, whose DataContext is the hub — so they bind hub commands directly.

    [RelayCommand] private void ToggleUntaggedFilter() => UntaggedOnly = !UntaggedOnly;
    [RelayCommand] private void ToggleFavouritesFilter() => FavouritesOnly = !FavouritesOnly;

    /// <summary>
    /// One funnel for "the tag selection moved": every property the selection feeds,
    /// then the filter itself. Suppressed during bulk mutations so a five-tag reset is
    /// one refilter, not five.
    /// </summary>
    private bool _suppressTagFilterNotify;

    private void NotifyTagFilterChanged()
    {
        if (_suppressTagFilterNotify) return;
        OnPropertyChanged(nameof(HasTagFilter));
        OnPropertyChanged(nameof(TagFilterLabel));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterButtonText));
        ApplyFilter();
        QueueLayoutSave();
    }

    private void OnTagFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagFilterViewModel.IsSelected)) NotifyTagFilterChanged();
    }

    public int UntaggedCount => InstalledCount - _taggedModCount;
    private int _taggedModCount;

    /// <summary>Every defined tag with its usage count, for the Tags ▾ flyout.</summary>
    public ObservableCollection<TagFilterViewModel> AllTags { get; } = [];

    // --- ITagStore (Settings ▸ Tags & metadata, 2g) --------------------------
    // The page edits the LIVE tag set rather than a copy, so a rename or a recolour
    // reaches the stripes and the Tags ▾ flyout at once. Two copies is how the stripe
    // colour and the tag table would come to disagree.

    TagSet ITagStore.Tags => _tagSet;

    Task ITagStore.SaveAsync(TagSet tags)
    {
        _tagSet = tags;

        // Republished immediately, but WRITTEN through the serial writer. The editor saves
        // on every keystroke, so two writes to tags.json could overlap inside File.Replace
        // — "Unable to remove the file to be replaced" — and, being unawaited, that took
        // the whole app down rather than failing a save.
        RefreshTagFilters();
        ApplyTagStripesToRows();
        // And the ASSIGN flyout, which was missed here and had to be: since O22 removed
        // its create box, Settings is the ONLY place a tag is born, so "make one in
        // Settings, go back and assign it" is now the canonical path — and the flyout
        // was still listing the set from before the tag existed. Caught by driving:
        // "+ New tag" added a third tag that the flyout would not show.
        RefreshAssignRows();
        RefreshTagsForSelection();

        _tagWriter.Queue(tags);
        return Task.CompletedTask;
    }

    private Task WriteTagsAsync(TagSet tags) =>
        _metadata is null ? Task.CompletedTask : _metadata.SaveTagsAsync(tags);

    IReadOnlyDictionary<string, int> ITagStore.CountsByTagId()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (_metadata is null) return counts;

        foreach (var meta in _metadata.LoadModMetadata().Entries.Values)
        {
            if (meta.TagIds.IsDefaultOrEmpty) continue;
            foreach (var id in meta.TagIds) counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        return counts;
    }

    int ITagStore.TaggedModCount() => _taggedModCount;

    async Task<int> ITagStore.DeleteTagAsync(string tagId)
    {
        _tagSet = new TagSet(_tagSet.Tags.RemoveAll(t => t.Id == tagId));

        var cleared = 0;
        if (_metadata is not null)
        {
            var meta = _metadata.LoadModMetadata();
            var builder = meta.Entries.ToBuilder();

            foreach (var (key, entry) in meta.Entries)
            {
                if (entry.TagIds.IsDefaultOrEmpty || !entry.TagIds.Contains(tagId)) continue;
                builder[key] = entry with { TagIds = entry.TagIds.Remove(tagId) };
                cleared++;
            }

            if (cleared > 0) await _metadata.SaveModMetadataAsync(new ModMetadataSet(builder.ToImmutable()));
            await _metadata.SaveTagsAsync(_tagSet);
        }

        RefreshTagFilters();
        ApplyTagStripesToRows();
        return cleared;
    }

    string ITagStore.StorageLine()
    {
        // D4 · not "No modlist loaded", which would be a second inaccurate claim:
        // _metadata is null exactly when no scan has completed, and a modlist can be
        // selected while the game folder is missing.
        if (_metadata is null) return "Nothing scanned yet — the storage file appears after the first scan.";

        // The path comes FROM the repository. Rebuilding it here is what produced a line
        // naming modMetadata.json — a file that does not exist — and a size of 0 B beside
        // a real entry count, which is the kind of half-true a status line must not tell.
        var path = _metadata.ModMetadataPath;
        var entries = _metadata.LoadModMetadata().Entries.Count;
        var size = _workspace.FileSystem.Stat(path)?.Size ?? 0;

        return TagsPresenter.StorageLine(path, entries, size);
    }

    // --- Advanced actions (2g) ----------------------------------------------

    /// <summary>The parsed-mod cache's size, so "rebuild" states what it will discard.</summary>
    public string ScanCacheSummary
    {
        get
        {
            var db = Path.Combine(AppPaths.CacheDir, "mods.db");
            var size = _workspace.FileSystem.Stat(db)?.Size ?? 0;

            return size == 0
                ? "not built yet — the next scan will build it"
                : $"{UpdatesPresenter.Size(size)} · deleting it loses nothing but the next scan is slower";
        }
    }

    /// <summary>
    /// Deletes the parsed-mod cache. Safe by construction: SQLite here is a disposable
    /// derived cache and JSON is the source of truth, so this can lose nothing.
    /// </summary>
    [RelayCommand]
    private async Task RebuildScanCache()
    {
        var db = Path.Combine(AppPaths.CacheDir, "mods.db");
        try
        {
            _workspace.FileSystem.DeleteFile(db);
            _log.Info(LogSubsystem.Scan, "Scan cache deleted; rebuilding on this scan");
            OnPropertyChanged(nameof(ScanCacheSummary));
            await ReloadAsync();
            StatusText = "Scan cache rebuilt.";
        }
        catch (IOException ex)
        {
            // Usually the file is locked by another RimManager still running. Naming that
            // turns "it didn't work" into something the user can act on — and the log line
            // is what makes it visible after the status bar's next message replaces it.
            StatusText = $"Could not delete the scan cache: {ex.Message} "
                + "Close any other RimManager window and try again.";
            _log.Warn(LogSubsystem.Io, $"Rebuild scan cache failed: {ex}");
        }
    }

    /// <summary>
    /// Copies the whole on-disk log. The Activity panel and the file share one formatter
    /// precisely so this is paste-into-an-issue identical to what a developer would ask for.
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagnosticsBundle()
    {
        var lines = _log.Snapshot().Select(LogEntryFormatter.Format);
        // The stamp leads (N9): this bundle is what arrives when something breaks,
        // and "what are you running" must not be a follow-up question.
        var header =
            $"RimManager {BuildStamp.ForAssembly(typeof(MainWindowViewModel).Assembly)} · "
            + $"{InstalledCount} installed · {ActiveCount} active · "
            + $"game {GameVersion ?? "unknown"} · log level {LogLevels.Label(LogLevelIndex)}";

        var text = string.Join(Environment.NewLine, lines.Prepend(header));

        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow.Clipboard: { } clipboard })
        {
            var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(Avalonia.Input.DataTransferItem.Create(Avalonia.Input.DataFormat.Text, text));
            await clipboard.SetDataAsync(transfer);
            StatusText = $"Copied {_log.Count} log lines.";
        }
    }

    /// <summary>
    /// Opens the folder ModsConfig.xml backups land in — <c>&lt;Root&gt;/backups</c>
    /// since O5. It used to open RimWorld's config folder, because that is where the
    /// atomic write left its timestamped copies; both moved together, or the button
    /// would have gone on opening a folder the backups had left.
    /// </summary>
    [RelayCommand]
    private void OpenBackupFolder()
    {
        // Created on demand: before the first Apply there is nothing here, and opening a
        // file manager on a path that does not exist is an error dialog, not an answer.
        var dir = AppPaths.BackupsDir;
        Directory.CreateDirectory(dir);

        var error = new FolderLauncher().Open(dir);
        if (error is not null) StatusText = $"Could not open the backup folder: {error}";
    }

    /// <summary>
    /// Supplied by the view, because a modal needs a parent window. Null in a headless
    /// context, and every destructive action checks it — a missing confirmer means the
    /// action cannot run, which is the safe failure rather than an unconfirmed one.
    /// </summary>
    public Confirmer? Confirm { get; set; }

    /// <summary>
    /// Danger zone · delete every unprotected snapshot for this instance. Named and pinned
    /// states survive, and the confirmation says so — otherwise naming a state would mean
    /// nothing the moment someone used this.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAllSnapshots()
    {
        if (Confirm is null || _modlistRepo is null || SelectedModlist is not { } list) return;

        var total = _modlistRepo.ListSnapshots(list.Id).Count;
        var kept = _modlistRepo.ProtectedSnapshotCount(list.Id);
        var going = total - kept;

        if (going == 0)
        {
            StatusText = "Nothing to delete — every snapshot here is named or pinned.";
            return;
        }

        var result = await Confirm(new ConfirmRequest(
            $"Delete {going} snapshot{(going == 1 ? "" : "s")} for “{list.Name}”?",
            kept > 0
                ? $"Your {kept} named or pinned snapshot{(kept == 1 ? "" : "s")} will be kept. "
                  + "Your mods, ModsConfig.xml and the current load order are not touched."
                : "Your mods, ModsConfig.xml and the current load order are not touched.",
            Verb: "Delete snapshots"));

        if (!result.Confirmed) return;

        var removed = _modlistRepo.DeleteUnprotectedSnapshots(list.Id);
        _log.Warn(LogSubsystem.Io, $"Deleted {removed} snapshots for '{list.Name}'");
        RefreshHistory();
        StatusText = $"Deleted {removed} snapshot{(removed == 1 ? "" : "s")}.";
    }

    /// <summary>
    /// Danger zone · reset RimManager. Removes everything the app owns and nothing the
    /// game does — the sentence the confirmation has to carry, because that is exactly
    /// the fear a mod manager has to answer.
    /// </summary>
    /// <summary>
    /// Everything the reset removes, relative to <see cref="AppPaths.Root"/>. An explicit
    /// list rather than "delete the root": the root also holds <c>steamcmd/</c> (a ~300 MB
    /// re-download is not "settings") and <c>logs/</c> (the sink is appending to it from
    /// this very process), and the old whole-directory delete is how this command came to
    /// claim it removed load orders it never touched.
    /// </summary>
    // "vault" / "vault.json" are still swept even though the vault feature is gone
    // (O13): a machine that used the CLI's `vault pin` before the removal has both on
    // disk, and a reset that leaves them behind would keep claiming to remove
    // everything while quietly stranding full copies of mods.
    private static readonly string[] ResetDirectories =
        ["modlists", "snapshots", "modsettings", "vault", "cache"];

    private static readonly string[] ResetFiles =
        ["vault.json", "tags.json", "categories.json", "modmeta.json",
         "layout.json", "snoozes.json", "rules.json", "rwlistOffers.json",
         "paths.json", "settings.json"];

    [RelayCommand]
    private async Task ResetRimManager()
    {
        if (Confirm is null) return;

        var result = await Confirm(new ConfirmRequest(
            "Reset RimManager?",
            $"Removes all {Modlists.Count} modlist{(Modlists.Count == 1 ? "" : "s")}, "
            + "every snapshot, tag, note, pinned mod and setting. Kept: the log files, "
            // O5 moved the ModsConfig backups INTO RimManager's folder, so a reader of
            // this sentence now has reason to fear for them. ResetFiles is an explicit
            // list and never touches the backups directory; saying so is the difference
            // between that being true and the user knowing it.
            + "your ModsConfig.xml backups, and the private SteamCMD install. Your mods "
            + "and ModsConfig.xml are not touched — RimWorld will start exactly as it does now.",
            Verb: "Reset RimManager",
            SafetyLabel: "Export my current load order first"));

        if (!result.Confirmed) return;

        if (result.SafetyChosen) await ExportCommand.ExecuteAsync(null);

        try
        {
            _log.Warn(LogSubsystem.Io, "Resetting RimManager: removing all app data and settings");

            foreach (var dir in ResetDirectories.Select(d => Path.Combine(AppPaths.Root, d)))
            {
                if (_workspace.FileSystem.DirectoryExists(dir))
                    _workspace.FileSystem.DeleteDirectory(dir, recursive: true);
            }

            foreach (var file in ResetFiles.Select(f => Path.Combine(AppPaths.Root, f)))
            {
                if (_workspace.FileSystem.FileExists(file))
                    _workspace.FileSystem.DeleteFile(file);
            }

            StatusText = "RimManager reset. Restart to set up again — your mods are untouched.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The reset deletes in sequence, so a failure here means it got PART of the
            // way — saying only "Reset failed" implies nothing happened, which is the
            // one thing that is certainly untrue. The log line carries what was reached.
            StatusText = $"Reset stopped part-way — {ex.Message} "
                + "Some data was already removed; check Settings ▸ Advanced and try again.";
            _log.Error(LogSubsystem.Io, $"Reset failed part-way through: {ex}");
        }
    }

    /// <summary>
    /// Dock geometry and filters back to their defaults, LIVE — and persisted, so the
    /// next launch agrees. The old body only wrote the file and promised a next-launch
    /// effect nothing delivered: LoadLayout had no caller (the N11 audit's
    /// fully-wired-yet-false finding).
    /// </summary>
    [RelayCommand]
    private async Task ResetWindowLayout()
    {
        if (_state is null) return;

        _dockGeometry.Reset();
        IsDockOpen = false;
        DockTabIndex = 0;
        DockHeight = DockGeometry.DefaultBodyHeight;
        DockDetailWidth = DockGeometry.DefaultDetailWidth(0);
        WarningsOnly = false;
        MatchAllTags = false;

        _suppressTagFilterNotify = true;
        try
        {
            foreach (var tag in AllTags) tag.IsSelected = false;
        }
        finally
        {
            _suppressTagFilterNotify = false;
        }
        NotifyTagFilterChanged();

        await _state.SaveLayoutAsync(LayoutState.Default);
        StatusText = "Window layout reset — dock and filters are back to their defaults.";
    }
}
