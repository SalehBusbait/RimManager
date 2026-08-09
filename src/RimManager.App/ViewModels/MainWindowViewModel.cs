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

public sealed partial class MainWindowViewModel
    : ObservableObject, ISeparatorHost, IAppPreferences, ITagStore, IModlistStore
{
    public const string ActivePane = "active";
    public const string InactivePane = "inactive";

    private readonly WorkspaceService _workspace;
    private readonly UpdateCheckService _updateCheck;
    private readonly ConflictAnalysisService _conflictAnalysis;
    private readonly CollectionService _collection;
    private readonly WorkshopDownloadService _download;
    private readonly FileDialogService _fileDialogs;
    private readonly RulesService _rulesService;
    private readonly ModDatabasesService _modDatabases;
    private readonly GitService _git;

    /// <summary>Notices the game rewriting its own mod list while this window is open.</summary>
    private readonly IFileWatcher _watcher;
    private readonly IntegrationStatusService _integrations;
    private LoadOrderRules _communityRules = LoadOrderRules.Empty;

    // N7 · Mlie's two databases, loaded from cache per reload and refreshed by Sync.
    private RimManager.Core.ModDatabases.ReplacementDatabase _replacements =
        RimManager.Core.ModDatabases.ReplacementDatabase.Empty;
    private RimManager.Core.ModDatabases.KnownGoodDatabase _knownGood =
        RimManager.Core.ModDatabases.KnownGoodDatabase.Empty;

    // --- N7c · the community-database toggles --------------------------------
    // Each applies immediately, which is the Settings page's own promise: the FIELD
    // holds the effective database, so every consumer — sorter, validator, status
    // bar, parity guard — follows without knowing the toggle exists.

    [ObservableProperty] private bool _useCommunityRules = true;

    partial void OnUseCommunityRulesChanged(bool value)
    {
        _communityRules = value ? _rulesService.LoadCached() : LoadOrderRules.Empty;
        UpdateRulesStatus();
        if (!_loadingSettings && _initialized) Validate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacementsStatus), nameof(ReplacementsPill))]
    private bool _useReplacementsDatabase = true;

    partial void OnUseReplacementsDatabaseChanged(bool value)
    {
        _replacements = value
            ? _modDatabases.LoadCachedReplacements()
            : RimManager.Core.ModDatabases.ReplacementDatabase.Empty;
        if (!_loadingSettings && _initialized) Validate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KnownGoodStatus), nameof(KnownGoodPill))]
    private bool _useKnownGoodDatabase = true;

    partial void OnUseKnownGoodDatabaseChanged(bool value)
    {
        _knownGood = value
            ? _modDatabases.LoadCachedKnownGood(_gameMajorMinor)
            : RimManager.Core.ModDatabases.KnownGoodDatabase.Empty;
        if (!_loadingSettings && _initialized) Validate();
    }

    // Custom source URLs (N7d): empty = default. Trimmed at the point of use, so a
    // pasted URL with a stray space does not quietly become a different source.
    [ObservableProperty] private string _communityRulesUrl = string.Empty;
    [ObservableProperty] private string _replacementsUrl = string.Empty;
    [ObservableProperty] private string _knownGoodBaseUrl = string.Empty;

    private static string? EffectiveUrl(string configured) =>
        string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();

    // The card pills (T5/S-INTEG): one word each, elaborated by the status line
    // beneath. The error fields hold the last sync's NON-CONNECTIVITY failure —
    // connectivity is the offline system's news, and three red pills because the
    // wifi dropped would be noise wearing an alarm.
    private string? _rulesSyncError;
    private string? _replacementsSyncError;
    private string? _knownGoodSyncError;

    public DatabasePill RulesPill =>
        DatabasePill.For(UseCommunityRules, _communityRules.Rules.Count, _rulesSyncError);

    /// <summary>
    /// The Integrations card's rules line. Separate from <see cref="RulesStatus"/>, which
    /// is the status bar's zone 2 — 24px high, no room for a sentence, and shared.
    /// <para>
    /// The card has room, and it needs it: <see cref="DatabasePill"/>'s own contract says
    /// the "sync failed" pill is a headline "which the status line beneath says [is still
    /// serving cached data]" — and the line never said it, because RulesStatus is built
    /// from count, age and offline and never saw the error at all. So the card showed a
    /// red alarm above a sentence reporting a healthy 21-hour-old cache, and the two read
    /// as disagreeing rather than as headline and detail. The failure text itself lives
    /// only in the pill's tooltip, which is not somewhere a user looks to resolve a
    /// contradiction.
    /// </para>
    /// </summary>
    public string RulesCardStatus =>
        _rulesSyncError is null
            ? RulesStatus
            : $"{RulesStatus} · last sync failed, this cache is still in use";
    public DatabasePill ReplacementsPill =>
        DatabasePill.For(UseReplacementsDatabase, _replacements.Count, _replacementsSyncError);
    public DatabasePill KnownGoodPill =>
        DatabasePill.For(UseKnownGoodDatabase, _knownGood.Count, _knownGoodSyncError);

    /// <summary>The Integrations card's line: count · fetch date, or why there is none.</summary>
    public string ReplacementsStatus
    {
        get
        {
            if (!UseReplacementsDatabase) return "Off — installed mods are not checked for replacements.";
            if (_replacements.Count == 0) return "Not synced yet — Sync to download.";

            var age = _modDatabases.ReplacementsCachedAtUtc() is { } at
                ? DateTimeOffset.UtcNow - at : (TimeSpan?)null;
            return $"{_replacements.Count} replacement rules · synced {NetworkFailure.Age(age)}";
        }
    }

    public string KnownGoodStatus
    {
        get
        {
            if (!UseKnownGoodDatabase) return "Off — version warnings are never suppressed.";
            if (_knownGood.Count == 0)
            {
                return _gameMajorMinor is { } v
                    ? $"Not synced yet for {v} — Sync to download."
                    : "Not synced yet — Sync to download.";
            }

            var age = _modDatabases.KnownGoodCachedAtUtc(_gameMajorMinor) is { } at
                ? DateTimeOffset.UtcNow - at : (TimeSpan?)null;
            return $"{_knownGood.Count} known-good packageIds for {_gameMajorMinor} "
                + $"· synced {NetworkFailure.Age(age)}";
        }
    }
    private bool _initialized;
    private int _separatorSeq;
    private string? _gameMajorMinor;
    private MetadataRepository? _metadata;
    private WorkspaceStateRepository? _state;
    private SnoozeSet _snoozes = SnoozeSet.Empty;

    /// <summary>
    /// Git state for the mods that are working trees, keyed by packageId. Measured once
    /// per scan and read by two surfaces — the ⎇ row glyph and the Integrations card —
    /// so the page and the list can never disagree about what git said.
    /// </summary>
    private ImmutableDictionary<ModId, GitStatus> _gitStatuses =
        ImmutableDictionary<ModId, GitStatus>.Empty;

    /// <summary>How many installed mods are git working trees (Integrations card).</summary>
    private int _gitTrackedCount;
    private ImmutableArray<ModId> _knownExpansions = [];
    private ImmutableDictionary<ModId, Mod> _byId = ImmutableDictionary<ModId, Mod>.Empty;
    private ModsConfig? _modsConfig;
    private UndoHistory<ModlistState>? _undo;
    private readonly ActivityLog _log;

    public MainWindowViewModel(
        WorkspaceService workspace, UpdateCheckService updateCheck,
        ConflictAnalysisService conflictAnalysis, CollectionService collection,
        WorkshopDownloadService download, FileDialogService fileDialogs, RulesService rulesService,
        ModDatabasesService modDatabases,
        GitService git, IntegrationStatusService integrations,
        IFileWatcher watcher,
        ActivityLog log)
    {
        _modDatabases = modDatabases;
        _watcher = watcher;
        _rootProbe = new ModRootProbe(workspace.FileSystem);
        _git = git;
        _integrations = integrations;
        _log = log;
        _settingsRepo = new AppSettingsRepository(workspace.FileSystem);
        _settingsWriter = new SerialWriter<AppSettings>(
            s => _settingsRepo.SaveAsync(s),
            ex => _log.Warn(LogSubsystem.Io, $"could not save preferences: {ex.Message}"));
        _tagWriter = new SerialWriter<TagSet>(
            WriteTagsAsync,
            ex => _log.Warn(LogSubsystem.Io, $"could not save tags: {ex.Message}"));
        log.EntryWritten += entry => Dispatcher.UIThread.Post(() =>
        {
            ActivityLines.Add(entry);
            RefilterActivity();
        });
        _workspace = workspace;
        _updateCheck = updateCheck;
        _conflictAnalysis = conflictAnalysis;
        _collection = collection;
        _download = download;
        _fileDialogs = fileDialogs;
        _rulesService = rulesService;
        // Start from what the app was actually launched into, and do NOT push it back:
        // assigning through the property here would apply a variant before the window
        // exists. Follow-system is the honest starting state — nothing has chosen yet.
        _theme = AppTheme.FollowSystem;

    }

    public ObservableCollection<RowViewModel> ActiveRows { get; } = [];
    public ObservableCollection<RowViewModel> InactiveRows { get; } = [];
    public ObservableCollection<ValidationIssue> Warnings { get; } = [];
    public UpdatesViewModel Updates { get; } = new();
    public ConflictsViewModel Conflicts { get; } = new();
    /// <summary>
    /// The most recent import. Built by the wizard and adopted here on commit, rather
    /// than rebuilt: the ticks the user made on step 2 are what both halves of the
    /// commit act on. Nothing renders it — the Collection dock tab is gone — it is the
    /// model the download batch and the status line work from.
    /// </summary>
    public CollectionViewModel Collection { get; private set; } = new();

    /// <summary>
    /// Every "Workshop ↗" in the app. One service so the Steam-first policy lives in one
    /// place: four separate call sites each reached for the browser URL directly, so all
    /// four opened a browser even with Steam running.
    /// </summary>
    public static readonly WorkshopLinkService WorkshopLinks =
        new(new ShellUriLauncher(), () => new SteamClientDetector().IsClientRunning());
    public CyclesViewModel Cycles { get; } = new();

    /// <summary>The Warnings dock tab (2a) — six ordered groups, Cycles among them.</summary>
    public WarningsViewModel WarningsPanel { get; } = new();

    /// <summary>The History dock tab (2d) — append-only, named states exempt from pruning.</summary>
    public HistoryViewModel History { get; } = new();

    /// <summary>Scan-level warnings (duplicate packageId) — the Duplicates group.</summary>
    private ImmutableArray<ModWarning> _scanWarnings = [];

    /// <summary>
    /// The Activity tab's lines (2f). Deliberately plain text, and the SAME text as
    /// the on-disk log — its job is to be pasted into a GitHub issue.
    /// </summary>
    public ObservableCollection<LogEntry> ActivityLines { get; } = [];

    /// <summary>What the Activity list renders, after the level chips (2f).</summary>
    public ObservableCollection<LogEntry> VisibleActivityLines { get; } = [];

    [ObservableProperty] private bool _activityShowAll = true;
    [ObservableProperty] private bool _activityErrorsOnly;
    [ObservableProperty] private bool _activityWarnOnly;

    /// <summary>
    /// 2f's fourth chip. "All" is the normal reading view — Info and above — while
    /// Debug drops the floor to everything the log holds. Without the distinction the
    /// two chips would be the same chip.
    /// </summary>
    [ObservableProperty] private bool _activityDebug;

    /// <summary>
    /// Auto-scroll, and it "turns itself off the moment the user scrolls up" (2f).
    /// A follow toggle that fights you back up the log is worse than none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityCanJump))]
    private bool _activityFollow = true;

    /// <summary>
    /// Whether the Activity log is scrolled to its newest line. Pushed from the view,
    /// which is the only thing that can know.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityCanJump))]
    private bool _activityAtEnd = true;

    /// <summary>
    /// Whether to offer a way back to the newest line.
    /// <para>
    /// Follow disarms itself the moment you scroll up, which is right — a log that
    /// yanks you back while you are reading is worse than one that never follows. But
    /// it disarmed SILENTLY, and lines kept arriving below the viewport with nothing
    /// saying so: the log looked stopped. This is the other half of that behaviour, and
    /// it only appears when it has something to offer — Follow off, and not already at
    /// the end.
    /// </para>
    /// </summary>
    public bool ActivityCanJump => ActivityJump.CanJump(ActivityFollow, ActivityAtEnd);

    public int ActivityErrorCount => ActivityLines.Count(e => e.IsError);
    public int ActivityWarnCount => ActivityLines.Count(e => e.IsWarn);

    // The chip counts wear their severity while it exists (the v2 count-tone pass).
    public bool ActivityErrorsToned => ActivityErrorCount > 0;
    public bool ActivityWarnToned => ActivityWarnCount > 0;

    partial void OnActivityShowAllChanged(bool value) { if (value) ClearOtherLevelChips(all: true); RefilterActivity(); }
    partial void OnActivityErrorsOnlyChanged(bool value) { if (value) ClearOtherLevelChips(errors: true); RefilterActivity(); }
    partial void OnActivityWarnOnlyChanged(bool value) { if (value) ClearOtherLevelChips(warn: true); RefilterActivity(); }
    partial void OnActivityDebugChanged(bool value) { if (value) ClearOtherLevelChips(debug: true); RefilterActivity(); }

    /// <summary>The chips are mutually exclusive — they are a level FLOOR, not a set.</summary>
    private void ClearOtherLevelChips(
        bool all = false, bool errors = false, bool warn = false, bool debug = false)
    {
        if (!all) ActivityShowAll = false;
        if (!errors) ActivityErrorsOnly = false;
        if (!warn) ActivityWarnOnly = false;
        if (!debug) ActivityDebug = false;
    }

    private void RefilterActivity()
    {
        var floor = ActivityErrorsOnly ? LogLevel.Error
                  : ActivityWarnOnly ? LogLevel.Warn
                  : ActivityDebug ? LogLevel.Trace
                  : LogLevel.Info;

        VisibleActivityLines.Clear();
        foreach (var entry in ActivityLines)
            if (entry.Level >= floor) VisibleActivityLines.Add(entry);

        OnPropertyChanged(nameof(ActivityErrorCount));
        OnPropertyChanged(nameof(ActivityWarnCount));
        OnPropertyChanged(nameof(ActivityErrorsToned));
        OnPropertyChanged(nameof(ActivityWarnToned));
    }

    /// <summary>Help ▸ Open log folder, and the Activity tab's "Open log ↗".</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RimManager", "logs");

        // FolderLauncher, not ShellUriLauncher. The URI launcher refuses anything outside
        // its steam:// / http(s):// allowlist, so this threw an ArgumentException that the
        // catch swallowed — the menu item has never opened anything.
        var error = new FolderLauncher().Open(dir);
        if (error is not null) StatusText = $"Could not open the log folder: {error}";
    }

    /// <summary>Copies the whole visible log, formatted exactly as the file is.</summary>
    [RelayCommand]
    private async Task CopyActivityLog()
    {
        var text = LogEntryFormatter.Format(VisibleActivityLines);
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow.Clipboard: { } clipboard })
        {
            // Avalonia 12 clipboard writes go through a DataTransfer, the same API
            // the drag/drop code uses — SetTextAsync was removed.
            var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(Avalonia.Input.DataTransferItem.Create(
                Avalonia.Input.DataFormat.Text, text));
            await clipboard.SetDataAsync(transfer);
            // The VISIBLE count (UI audit): the copy is of the filtered view, and with
            // a quieter chip active the old line claimed more lines than the clipboard
            // holds — a number nobody could reconcile with what they pasted.
            StatusText = $"Copied {VisibleActivityLines.Count} log lines (current filter).";
        }
    }

    /// <summary>How many snapshots exist, for the History tab's header count.</summary>
    public int SnapshotCount => History.TotalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDetail))]
    private ModDetailViewModel? _selectedDetail;
    [ObservableProperty] private int _warningCount;
    // UndoLabel only. The footer indicator used to hang off this too, which is the bug:
    // "can I undo" and "does this differ from the game" are different questions, and after
    // an undo they give opposite answers. Undo enablement is the honest use of CanUndo and
    // is all that is left on it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UndoLabel))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RedoLabel))]
    private bool _canRedo;

    /// <summary>
    /// Where the one install lives. Not observable — nothing binds it; the panes show
    /// the modlist, and this only says where the game is. Null until
    /// <see cref="InitializeAsync"/> resolves it (or first run creates it).
    /// </summary>
    private InstallPaths? _installPaths;

    /// <summary>
    /// The modlist the panes are showing — which arrangement is open, as opposed to
    /// <see cref="_installPaths"/>, which says where the game is.
    /// </summary>
    [ObservableProperty] private Modlist? _selectedModlist;

    /// <summary>Every list, for the switcher M5 puts on the toolbar.</summary>
    public ObservableCollection<Modlist> Modlists { get; } = [];

    private ModlistRepository? _modlistRepo;
    private SerialWriter<Modlist>? _modlistWriter;

    /// <summary>
    /// How the list on screen stands against what the game actually has — <b>the</b> answer,
    /// now that there is only one.
    /// <para>
    /// There used to be two. This held the correct comparison and was shown once per load,
    /// as a status line; the footer indicator computed its own from <c>CanUndo</c> — "have
    /// you edited anything this session" — which compares against nothing at all. That is
    /// exactly the reported behaviour: putting the order back by hand never cleared it,
    /// because doing so is <em>more</em> editing, so <c>CanUndo</c> got more true, not less.
    /// </para>
    /// <para>
    /// <c>[ObservableProperty]</c> rather than a plain field, and that is the load-bearing
    /// part. After the rewire neither of the indicator's inputs moves <c>CanUndo</c>, so a
    /// hand-written notification would have to be remembered at four call sites — and a
    /// bound computed property that nothing announces is this project's signature silent
    /// failure (<c>HasCommunityRules</c> survived four phases that way). Generated, it
    /// cannot quietly stop.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriftZoneText), nameof(DriftZoneTip), nameof(PendingDiffText))]
    [NotifyPropertyChangedFor(nameof(IsDriftInSync), nameof(IsDriftNotApplied))]
    [NotifyPropertyChangedFor(nameof(IsDriftOutside), nameof(IsDriftNeverApplied))]
    [NotifyPropertyChangedFor(nameof(IsDriftActionable))]
    private DriftKind _drift;

    // S-DRIFT's four glyphs, one bool per state so the markup needs no enum
    // converter (a converter hands back a value frozen at conversion time — the
    // catalogued trap this codebase replaced with bound classes everywhere).
    public bool IsDriftInSync => Drift == DriftKind.InSync;
    public bool IsDriftNotApplied => Drift == DriftKind.PendingApply;
    public bool IsDriftOutside => Drift == DriftKind.ChangedOutsideRimManager;
    public bool IsDriftNeverApplied => Drift == DriftKind.Unknown;

    /// <summary>When the game file was last written by RimManager — the most recent
    /// stamp across every list, because the zone reports THE GAME's state.</summary>
    private DateTimeOffset? _lastAppliedAt;

    /// <summary>The status bar's drift zone (S-DRIFT): glyph words per state, the
    /// applied TIME when in sync.</summary>
    public string DriftZoneText => DriftIndicator.Zone(Drift, _lastAppliedAt?.ToLocalTime());

    public string DriftZoneTip => DriftIndicator.ZoneTip(Drift);

    /// <summary>The zone is a CLICK TARGET in exactly two states (S-DRIFT): edited
    /// (click applies) and changed-outside (click brings the review strip back).</summary>
    public bool IsDriftActionable => Drift is DriftKind.PendingApply
        or DriftKind.ChangedOutsideRimManager;

    [RelayCommand]
    private void DriftZoneAction()
    {
        switch (Drift)
        {
            case DriftKind.PendingApply:
                RequestApplyCommand.Execute(null);
                break;
            case DriftKind.ChangedOutsideRimManager:
                // S-DRIFT: the ▲ click opens the review itself — and lifts any
                // dismissal, so the strip's alternatives are visible behind it.
                _gameMovedDismissed = false;
                OnPropertyChanged(nameof(ShowGameMovedStrip));
                OpenOrderDiff();
                break;
        }
    }

    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// The first-scan window state (<c>2k</c>) is up. True for the whole of a reload,
    /// because a reload clears both panes first — the state covers exactly the stretch
    /// during which there is nothing in them to look at.
    /// </summary>
    [ObservableProperty] private bool _isScanning;

    /// <summary>
    /// Which phase the state is reporting. A reload is not one operation, and on a modlist
    /// switch the slowest part is not the scan — it is copying the <c>Mod_*.xml</c> files in
    /// and out of the game's config folder, which on a real install is a few hundred files.
    /// That work used to happen <b>before</b> the state was raised, so a switch showed a
    /// frozen window, then a flash of "Reading mod folders…", then the result.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadPhaseLabel), nameof(ScanCountText), nameof(ScanPathText),
        nameof(ScanIsIndeterminate), nameof(HasScanCount))]
    private LoadPhase _loadPhase;

    /// <summary>The card's title, which used to be hardcoded in its markup.</summary>
    public string LoadPhaseLabel => LoadPhaseText.For(LoadPhase);

    /// <summary>
    /// One place to funnel a phase's progress onto the card, marshalled to the UI thread.
    /// <para>
    /// <c>Progress&lt;T&gt;</c> captures the current synchronisation context at construction,
    /// and every phase here starts on the UI thread — so the reports arrive back on it and
    /// the bar can move while the work runs on a pool thread.
    /// </para>
    /// </summary>
    private Progress<ScanProgress> LoadProgress() => new(p => ScanProgress = p);

    /// <summary>
    /// Where the scan has got to. Every derived string is announced with it: an
    /// unannounced computed property leaves the control that binds it permanently
    /// showing whatever it held at construction.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanCountText), nameof(ScanPathText),
        nameof(ScanFraction), nameof(ScanIsIndeterminate), nameof(HasScanCount))]
    private ScanProgress _scanProgress;

    /// <summary>Whether there is a count to show yet — the separator hangs off this.</summary>
    public bool HasScanCount => ScanProgress.Total > 0;

    /// <summary>"218 / 342", or empty before the roots have been counted.</summary>
    /// <summary>"214 / 292 mods" — the unit comes from the phase, so the line reads as a
    /// sentence rather than as two bare numbers.</summary>
    public string ScanCountText =>
        HasScanCount
            ? $"{ScanProgress.Done} / {ScanProgress.Total} {LoadPhaseText.Unit(LoadPhase)}"
            : string.Empty;

    /// <summary>
    /// The root being read, as its last three segments. Elided in the STRING rather
    /// than by <c>TextTrimming</c>, which clips at arrange without constraining measure
    /// and would let one long segment push the line wider than the panel.
    /// </summary>
    public string ScanPathText
    {
        get
        {
            var text = ScanProgress.ShortRoot();
            return text.Length <= 48 ? text : "…" + text[^47..];
        }
    }

    /// <summary>0..1 for the determinate bar.</summary>
    public double ScanFraction => ScanProgress.Fraction;

    /// <summary>
    /// True only while the roots are still being counted. A determinate bar whose total
    /// is zero renders as permanently empty, which reads as a stalled scan.
    /// </summary>
    public bool ScanIsIndeterminate => ScanProgress.Total <= 0;

    /// <summary>
    /// 2k · offline. Set by the last network request that never got an answer, cleared
    /// by the next one that did. RimManager does not ask the OS whether the network is
    /// up — a VPN, a proxy and a captive portal all answer yes — so it reports what
    /// actually happened to a request it made.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOfflineStrip))]
    private bool _isOffline;

    /// <summary>
    /// Dismissing hides the strip until the next FAILURE, not for ever: a notice you
    /// can silence permanently is one that stops meaning anything.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOfflineStrip))]
    private bool _offlineStripDismissed;

    public bool ShowOfflineStrip => IsOffline && !OfflineStripDismissed;

    /// <summary>The strip's second line — what still works, and how old the cache is.</summary>
    [ObservableProperty] private string _offlineDetail = string.Empty;

    // 2k's third degradation — "the Collection tab disables Download but keeps
    // Activate" — has nowhere to live. That tab is gone (R7g): its content is step 2
    // of a modal you can only reach by successfully fetching the collection, which is
    // itself a network call. Offline you never get there, and a disabled card on an
    // unreachable step is decoration. The fetch reports the failure and raises the
    // strip instead, which is the same information one step earlier.

    /// <summary>
    /// The game-not-found window state (<c>2k</c>). Raised instead of scanning: with no
    /// Data/Core there is no load order to render, and a pane full of Workshop mods that
    /// cannot be loaded would be worse than the state that explains why.
    /// </summary>
    [ObservableProperty] private bool _isGameMissing;

    /// <summary>What was found, from the same probe Settings ▸ Paths reports.</summary>
    [ObservableProperty] private string _gameMissingDetail = string.Empty;

    /// <summary>The stale path, in mono, unelided.</summary>
    [ObservableProperty] private string _gameMissingPath = string.Empty;

    [ObservableProperty] private string? _gameVersion;
    // The segment labels carry these counts (2k), so both must announce them. A count
    // that is merely correct leaves the segmented control showing 0 / 0 for ever.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSummary), nameof(PendingDiffText), nameof(SegmentActiveLabel))]
    private int _activeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SegmentInactiveLabel))]
    private int _inactiveCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveIsEmptyNothingActive))]
    private int _installedCount;
    /// <summary>
    /// The one theme store (<see cref="IAppPreferences.Theme"/>). Setting it is what
    /// applies it — every surface that changes the theme goes through this property, so
    /// none of them can set it without it taking effect.
    /// </summary>
    [ObservableProperty] private AppTheme _theme;

    partial void OnThemeChanged(AppTheme value)
    {
        ApplyTheme(value);
        RefreshThemeBrand(value);
    }

    /// <summary>The current theme's mark (T4, S-BRAND) — the scan hero and anything
    /// else bound to the hub. About and first-run load theirs at construction via
    /// <see cref="ThemeAssets.CurrentMark"/>.</summary>
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _themeMark;

    /// <summary>
    /// Swaps the brand for the theme: the bound mark, the shared static (for windows
    /// constructed later), and the live window icon. Called from every theme write AND
    /// unconditionally after settings load — <c>OnThemeChanged</c> alone misses the
    /// saved-value-equals-default case, and follow-system's mark can only resolve once
    /// the actual variant is known.
    /// </summary>
    private void RefreshThemeBrand(AppTheme theme)
    {
        ThemeAssets.CurrentTheme = theme;
        ThemeMark = ThemeAssets.Mark(theme);

        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } main } && ThemeAssets.Icon(theme) is { } icon)
        {
            main.Icon = icon;
        }
    }

    /// <summary>
    /// The single place <c>RequestedThemeVariant</c> is written. <see cref="ThemeVariant.Default"/>
    /// is what "follow system" means to Avalonia: it defers to the platform, which lands
    /// on the Drop Pods pair because that pair rides the built-in Light/Dark keys. The
    /// flavoured themes are custom variants from <see cref="ThemeCatalog"/>.
    /// <para>
    /// There is no accent step any more: the runtime accent derivation
    /// (<c>AccentPalette.Derive</c>) was retired with the accent picker (design handoff
    /// v2) — each theme's dictionary authors its full accent family, including the
    /// Fluent Slider/RadioButton keys the derivation used to write, so nothing
    /// overwrites dictionaries at application level and the token files are the truth.
    /// </para>
    /// </summary>
    private static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = ThemeCatalog.VariantOf(theme);
    }


    /// <summary>The chosen UI font (<c>2g</c>), indexing <see cref="UiFonts.Choices"/>.</summary>
    [ObservableProperty] private int _fontIndex;

    /// <summary>
    /// One resource write re-renders every piece of text, the same mechanism density uses
    /// for row height. Only the UI role: <c>RmFontMono</c> is left alone because the
    /// aligned version and packageId columns depend on it staying monospaced.
    /// </summary>
    partial void OnFontIndexChanged(int value)
    {
        if (Application.Current is not { } app) return;

        app.Resources["RmFontUi"] = new FontFamily(UiFonts.Get(value).Family);
    }

    /// <summary>
    /// UI scale as a percentage (<c>2g</c>). Held as an int, not a factor: it is what
    /// the slider shows and what gets persisted, and a double round-tripped through
    /// JSON reads back as 120.00000000000001.
    /// <para>
    /// This is NOT the same thing as the display's DPI. It scales the app's own layout
    /// on top of whatever the OS is already doing, which is why the design calls it
    /// restart-free — nothing is re-rasterised, the layout is simply measured larger.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScaleText), nameof(UiScaleFactor), nameof(LayoutWidth),
        // Scale changes how much room the layout has, so it moves the breakpoints
        // exactly as resizing the window does.
        nameof(IsInfoOverlay), nameof(ShowInfoDrawer), nameof(IsSegmentedLayout),
        nameof(IsFullLayout), nameof(InfoOverlayWidth),
        nameof(VersionColumnWidth), nameof(AuthorColumnWidth),
        nameof(RowPackageIdWidth), nameof(RowVersionWidth), nameof(RowChevronWidth))]
    private int _uiScalePercent = 100;

    public string UiScaleText => $"{UiScalePercent}%";

    public double UiScaleFactor => UiScalePercent / 100.0;

    /// <summary>Raised so the window can re-apply its layout transform. An event rather
    /// than a binding: a ScaleTransform is not in the logical tree, and a binding onto
    /// one is the family of failure that resolves against nothing and says nothing.</summary>
    public event Action<double>? UiScaleChanged;

    partial void OnUiScalePercentChanged(int value)
    {
        UiScaleChanged?.Invoke(UiScaleFactor);

        // The layout just got narrower or wider without the window moving, so the
        // breakpoints have to be re-evaluated and the row height re-picked.
        LayoutWidthChanged?.Invoke(LayoutWidth);
        if (!IsInfoOverlay) IsInfoDrawerOpen = false;
        ApplyDensity();
    }

    /// <summary>Raised when the width available to the LAYOUT changes — by a resize or
    /// by a UI-scale change, which are the same thing as far as the breakpoints go.</summary>
    public event Action<double>? LayoutWidthChanged;

    [ObservableProperty] private string _rulesStatus = "No community rules";

    /// <summary>The ✓ in status-bar zone 2. Withdrawn while offline: a tick beside a
    /// cached number claims the rules were confirmed current, which they were not.</summary>
    [ObservableProperty] private bool _hasCommunityRules;

    /// <summary>Set true when there's no instance yet — the view shows the first-run setup.</summary>
    public bool NeedsFirstRun { get; private set; }

    /// <summary>
    /// The first-run wizard (<c>2j</c>). Also what Help ▸ Re-run first-time setup opens,
    /// which is why it is built from the current detection rather than from whatever the
    /// app happened to start with.
    /// </summary>
    public FirstRunViewModel BuildFirstRun()
    {
        var (game, config, workshop) = _workspace.DetectPaths();
        var localMods = game is null ? null : System.IO.Path.Combine(game, "Mods");

        return new FirstRunViewModel(
            _workspace.FileSystem,
            (game, config, localMods, workshop),
            FinishFirstRunAsync);
    }

    /// <summary>
    /// Persists the paths the wizard described and applies its opt-ins. Runs for a
    /// skipped wizard too: an app with no install is not a state anything downstream
    /// handles, so Skip skips the questions rather than the setup.
    /// </summary>
    private async Task FinishFirstRunAsync(FirstRunViewModel wizard)
    {
        // What the wizard asked for names the first MODLIST, which is the thing
        // the toolbar shows and the thing the user switches between.
        var name = wizard.ModlistName.Trim();
        _installPaths = await _workspace.CreatePathsAsync(
            wizard.GameDir,
            wizard.ConfigDir.Length == 0 ? null : wizard.ConfigDir,
            wizard.WorkshopDir);

        CheckModUpdatesOnStartup = wizard.CheckModUpdatesOnStartup;
        await ReloadAsync();

        // Renamed after the reload, because that is what seeds the list from the game's
        // current order — there is nothing to name before it exists. The COLOUR rides
        // the same save (UI audit: step 3's swatches wrote a PaletteIndex no consumer
        // ever read, so the dot the user picked never reached the toolbar selector).
        if (_modlistRepo is not null && SelectedModlist is { } seeded)
        {
            var updated = seeded;
            if (name.Length > 0 && !string.Equals(seeded.Name, name, StringComparison.Ordinal))
                updated = updated with { Name = name };
            if (seeded.PaletteIndex != wizard.PaletteIndex)
                updated = updated with { PaletteIndex = wizard.PaletteIndex };
            if (!ReferenceEquals(updated, seeded)) await SaveModlistAsync(updated);
        }

        if (wizard.DownloadCommunityRules) await SyncRulesCommand.ExecuteAsync(null);
        if (wizard.GroupWithSeparators) AutoLayoutCommand.Execute(null);
        if (wizard.SortImmediately) SortCommand.Execute(null);

        _log.Info(LogSubsystem.Ui,
            $"First run finished: modlist '{name}', rules={wizard.DownloadCommunityRules}, "
            + $"groups={wizard.GroupWithSeparators}, sort={wizard.SortImmediately}");
    }

    /// <summary>Measures the install the wizard is about to import, for its step 3 card.</summary>
    public void MeasureFirstRunImport(FirstRunViewModel wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);

        var active = ActiveRows.OfType<ModRowViewModel>().Select(r => r.Mod).ToList();
        var sources = _byId.Values
            .GroupBy(m => m.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        var domain = _byId.Values.ToList();
        var sorted = new ModSorter().Sort(
            domain, RuleGraphBuilder.Build(domain, _communityRules, overrides: _ruleOverrides));

        wizard.WorkshopItemCount = sources.GetValueOrDefault(ModSource.Workshop);
        wizard.Import = new FirstRunImport(
            active.Count,
            InactiveRows.OfType<ModRowViewModel>().Count(),
            FirstRunPresenter.SourcesLine(sources),
            _scanWarnings.Length,
            FirstRunPresenter.ProposedGroups(sorted));
    }

    /// <summary>
    /// Re-reads <c>paths.json</c> (e.g. after Settings edits paths) and reloads only if
    /// something changed — record equality is what spares a settings close that touched
    /// nothing from a full rescan, the same behaviour the instance reselect used to get
    /// from record equality on the setter.
    /// </summary>
    public async Task ReloadPathsAsync()
    {
        var fresh = _workspace.LoadPaths();
        if (fresh is null || Equals(fresh, _installPaths)) return;

        _installPaths = fresh;
        await ReloadAsync();
    }

    // --- 2k · game not found -------------------------------------------------

    /// <summary>
    /// Locate folder… — the primary. The picked folder is validated by the SAME probe
    /// that raised the state, and a folder that is not an install is refused with the
    /// reason rather than accepted and re-raising the state a second later.
    /// </summary>
    [RelayCommand]
    private async Task LocateGameFolder()
    {
        if (await _fileDialogs.PickFolderAsync("Where is RimWorld installed?") is not { } picked)
            return;

        await AdoptGameFolderAsync(picked, "That folder");
    }

    /// <summary>
    /// Auto-detect — the same locator first run uses. It reports finding nothing rather
    /// than silently doing nothing, because a button that appears inert is the failure
    /// this project has shipped most often.
    /// </summary>
    [RelayCommand]
    private async Task AutoDetectGameFolder()
    {
        StatusText = "Looking for a RimWorld install…";
        var detected = await Task.Run(() => _workspace.DetectPaths());

        if (detected.Game is not { } found)
        {
            StatusText = "No RimWorld install found automatically — use Locate folder…";
            return;
        }

        await AdoptGameFolderAsync(found, "The install we found");
    }

    private async Task AdoptGameFolderAsync(string folder, string subject)
    {
        var check = PathProbe.Game(_workspace.FileSystem, folder);
        if (check.IsMissing)
        {
            StatusText = $"{subject} is not usable — {check.Message}";
            return;
        }

        // The Workshop folder is Steam's and sits outside the game directory, so it is
        // deliberately left alone: re-deriving it here would silently discard a path the
        // user had pointed somewhere else.
        var moved = _installPaths is { } current
            ? current with { GameDir = folder }
            : new InstallPaths { GameDir = folder };
        await _workspace.SavePathsAsync(moved);
        _installPaths = moved;

        _log.Info(LogSubsystem.Scan, $"Game folder set to {folder}");
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_installPaths is not { } paths) return;
        if (IsBusy) return;
        IsBusy = true;

        // IsBusy stays what it is — a re-entrancy guard that Import also reads — and
        // the activity zone is claimed separately. Two meanings on one flag is how the
        // theme ended up with two stores in R6.
        using var activity = Activity("scanning mods…");

        // FIRST — it needs nothing from the scan, and everything after assumes it ran.
        await EnsureModlistStoreAsync();

        ActiveRows.Clear();
        InactiveRows.Clear();
        LoadFailed = false;

        // The layout store is opened BEFORE the game check, not after the scan (O17).
        // It was assigned below the early return, so a user whose game folder had moved
        // lost window memory entirely: ApplyLayout no-ops on a null _state and
        // QueueLayoutSave no-ops on _layoutApplied — silently, on the one launch where
        // the window is all the user has. It depends on nothing the scan produces.
        _state = _workspace.State();
        _layoutWriter = new SerialWriter<LayoutState>(
            s => _state!.SaveLayoutAsync(s),
            ex => _log.Warn(LogSubsystem.Io, $"could not save layout: {ex.Message}"));

        // 2k · game not found. Checked BEFORE the scan rather than inferred from its
        // result: a scan of a vanished install returns zero mods, which is also what a
        // brand-new empty install returns, and the two need completely different words.
        var gameCheck = PathProbe.Game(_workspace.FileSystem, paths.GameDir);
        IsGameMissing = GameMissingPresenter.IsMissing(gameCheck);
        if (IsGameMissing)
        {
            GameMissingDetail = GameMissingPresenter.Describe(gameCheck, paths.GameDir);
            GameMissingPath = GameMissingPresenter.Path(paths.GameDir);
            _log.Error(LogSubsystem.Scan,
                $"Game folder unusable: {gameCheck.Message}");
            StatusText = GameMissingDetail;
            RefreshCounts();
            IsBusy = false;
            // The dock and the filters still apply here (O17). There are no rows to
            // filter, but the window arrangement is not the game's to withhold, and
            // this is precisely the launch where the user is about to go to Settings.
            ApplyLayout();
            return;
        }

        // Reset before the state is raised, or a second scan opens showing the first
        // one's finished counts for the fraction of a second before the roots are read.
        ScanProgress = default;

        // A switch has already raised the state and walked it through the settings phases;
        // this is where it hands over to the scan, which is the one phase with a real total.
        LoadPhase = LoadPhase.ReadingMods;
        IsScanning = true;

        var progress = new Progress<string>(msg => StatusText = msg);
        var scanProgress = new Progress<ScanProgress>(p => ScanProgress = p);
        try
        {
            var snapshot = await Task.Run(() => _workspace.Load(paths, progress, scanProgress));

            GameVersion = snapshot.ModsConfig?.Version;
            _gameMajorMinor = snapshot.ModsConfig?.MajorMinor;
            _knownExpansions = snapshot.ModsConfig?.KnownExpansions ?? [];
            _byId = snapshot.Scan.ById;
            _modsConfig = snapshot.ModsConfig;
            _metadata = _workspace.Metadata();
            _snoozes = _state.LoadSnoozes();
            _ruleOverrides = _state.LoadRuleOverrides();
            _tagSet = _metadata.LoadTags();
            RefreshTagFilters();
            InstalledCount = snapshot.Scan.Mods.Length;
            _scanWarnings = snapshot.Scan.Warnings;

            // NF-10 · Workshop items whose payload is a mod list: one offer at a
            // time, once per item, from the persisted seen-set.
            _rwListSeen = _state.LoadRwListOffers();
            RwListOffer = RwListOfferPresenter.NextUnseen(snapshot.Scan.Mods, _rwListSeen);
            // Logged AFTER the counts are assigned — reading them a line early
            // reported "Scanned 0 mods" on every launch.
            _log.Info(LogSubsystem.Scan, $"Scanned {InstalledCount} mods from disk");

            _communityRules = UseCommunityRules ? _rulesService.LoadCached() : LoadOrderRules.Empty;
            UpdateRulesStatus();

            // N7 · the two Mlie databases, from cache like the rules — the version list
            // is per game version, so it can only be loaded once the version is known.
            _replacements = UseReplacementsDatabase
                ? _modDatabases.LoadCachedReplacements()
                : RimManager.Core.ModDatabases.ReplacementDatabase.Empty;
            _knownGood = UseKnownGoodDatabase
                ? _modDatabases.LoadCachedKnownGood(_gameMajorMinor)
                : RimManager.Core.ModDatabases.KnownGoodDatabase.Empty;
            OnPropertyChanged(nameof(ReplacementsStatus));
            OnPropertyChanged(nameof(KnownGoodStatus));

            // Counts logged like the rules are: a suppression that silently fails to
            // load is invisible on every other surface — the warnings it should have
            // removed simply stay, looking ordinary.
            _log.Info(LogSubsystem.Rules,
                $"Mod databases: {_replacements.Count} replacements · "
                + $"{_knownGood.Count} known-good for {_gameMajorMinor ?? "unknown version"}");

            // N7d · sync on startup, once per session, UNAWAITED — the same line the
            // update check draws: a network call must not hold the loader hostage, and
            // the caches above mean the session starts on yesterday's data rather than
            // none. Results self-apply and revalidate when they land. No preference:
            // syncing is the default, and the toggles are the only control.
            if (!_databaseSyncStarted)
            {
                _databaseSyncStarted = true;
                _ = SyncDatabasesAsync(announce: false);

                // The app's own update check rides the same once-per-session,
                // unawaited line. Quiet by default; the auto-install preference is
                // the only thing that turns it into an action.
                _ = CheckForAppUpdateOnLaunchAsync();
            }

            // Seeded once, never overwritten: the default depends on whether this is a
            // Steam install, which is not known until the install loads. Re-deriving it
            // on every load would discard a command the user had edited.
            //
            // The one exception is the bare-"steam" default, which shipped briefly and
            // cannot run on Windows or macOS. Preserving an un-runnable command out of
            // respect for "the user might have typed it" helps nobody.
            if (GameLaunch.NeedsReseeding(LaunchCommand)) LaunchCommand = DefaultLaunchCommand;

            // The modlist is the source of truth now; ModsConfig.xml is an output. It
            // still SEEDS the first list, because on a machine that has never run this
            // build the game's file is the only record of the user's arrangement — but
            // after that it is only ever compared against, never read back.
            var activeIds = snapshot.ModsConfig?.ActiveMods ?? [];
            await LoadModlistsAsync(activeIds);

            var plan = SelectedModlist is { } current
                ? ModlistStartup.Resolve(current, snapshot.Scan.ById, snapshot.Scan.Mods)
                : null;

            if (plan is null)
            {
                // No list at all: fall back to the flat file rather than showing nothing.
                var activeSet = activeIds.ToHashSet();
                foreach (var id in activeIds)
                {
                    if (snapshot.Scan.ById.TryGetValue(id, out var mod))
                        ActiveRows.Add(new ModRowViewModel(mod));
                }

                foreach (var mod in snapshot.Scan.Mods
                             .Where(m => !activeSet.Contains(m.PackageId))
                             .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    InactiveRows.Add(new ModRowViewModel(mod));
                }
            }
            else
            {
                foreach (var row in plan.Active)
                {
                    if (row.Entry.Kind == ModlistEntryKind.Separator)
                    {
                        ActiveRows.Add(new SeparatorRowViewModel(
                            row.Entry.Id, row.Entry.DisplayName, this,
                            row.Entry.PaletteIndex ?? 0, row.Entry.Collapsed));
                        continue;
                    }

                    // A mod the list names but the disk lacks keeps its row and its place.
                    // Skipping it made the pane quietly shorter than the list, which is
                    // the failure ModlistStartup goes out of its way to avoid.
                    ActiveRows.Add(row.Mod is { } mod
                        ? new ModRowViewModel(mod)
                        : ModRowViewModel.Missing(row.Entry));
                }

                foreach (var mod in plan.Inactive) InactiveRows.Add(new ModRowViewModel(mod));

                // A collapsed separator hides the span below it, and that state is stored
                // on the separator rather than on the rows it owns — so it has to be
                // re-applied after the rows exist, exactly as undo/redo does.
                foreach (var sep in ActiveRows.OfType<SeparatorRowViewModel>()
                             .Where(s => s.Collapsed).ToList())
                {
                    ActiveListOps.ApplyCollapsed(ActiveRows, sep, true);
                }

                if (plan.HasMissing)
                {
                    // NAMED, not counted. "names 1 mod(s) that are not installed" told
                    // the user a number and left them to search 202 rows for it — the
                    // report was true and useless at the same time.
                    foreach (var absent in plan.Missing)
                    {
                        _log.Warn(LogSubsystem.Scan,
                            $"Not installed: {absent.DisplayName} ({absent.Id})"
                            + (absent.PublishedFileId is { } w ? $" · Workshop {w}" : string.Empty));
                    }
                }
            }

            ActiveListOps.Renumber(ActiveRows);
            ActiveListOps.Renumber(InactiveRows);
            // AFTER the rows exist, like the collapsed-separator reapply above. This
            // used to run beside LoadTags, forty lines up — which is BEFORE the rows
            // are built, so it painted pills onto an empty collection and the load
            // came up bare. It looked done because any later tag edit re-ran it (and
            // the verification sandbox's auto-assign always did).
            ApplyTagStripesToRows();
            Validate();
            ApplyFilter();
            _undo = new UndoHistory<ModlistState>(ModlistStateFromRows());
            UpdateUndoState();
            RefreshCounts();
            RefreshHistory();
            // Computed from the rows just built rather than taken from plan.Drift, which
            // is the same comparison run over the STORED state. Two computations of one
            // question is how the footer and the status line came to disagree; and the
            // plan's own value never reached the fallback branch above, where there is no
            // modlist, so that branch inherited whatever the previous load had decided.
            RefreshDrift();

            // Armed here rather than in the constructor: the path comes from the instance,
            // which is not known until one is loaded, and it moves when the config folder
            // is edited in Settings. Cheap and idempotent — it no-ops when the path is
            // unchanged, which is every reload but the first.
            WatchGameOrder();

            // What the user has just been shown becomes the new truth. Without this our own
            // SteamCMD download, or a delete-from-disk, would surface two seconds later as
            // somebody else's news — the app reporting the thing the app just did.
            _rootProbe.Rebaseline(ModRoots);
            DismissModRootNotice();
            StartRootPolling();

            // The persisted layout, once per session — after the rows and the tag
            // chips exist, before the user can touch anything.
            ApplyLayout();

            // Drift wins the line when there is any. RimWorld rewrites ModsConfig.xml
            // whenever a player accepts "load this save's mod list", and under
            // modlist-is-truth the next Apply would silently discard that — so it is
            // worth displacing the routine counts for.
            var driftLine = ModlistDrift.Describe(
                Drift, snapshot.ModsConfig?.ActiveMods.Length ?? 0, ActiveCount);

            if (driftLine.Length > 0)
            {
                StatusText = driftLine;
                _log.Warn(LogSubsystem.Scan, driftLine);
            }
            else
            {
                StatusText = SelectedModlist is { } open
                    ? $"{ActiveCount} active · {InstalledCount} installed · {open.Name}"
                    : $"{ActiveCount} active · {InstalledCount} installed";
            }

            // Deliberately not awaited: with "fetch on startup" on this is a network
            // call per repo, and a scan of 371 mods must not sit behind it. It applies
            // its own result when it finishes.
            _ = RefreshGitAsync(paths);

            // Same reasoning, and the same promise the first-run card makes: a batched
            // Workshop query that costs about two seconds and must not delay the lists.
            //
            // ONCE per session, and it does NOT open the dock. This runs inside a reload,
            // and reloads happen for reasons that have nothing to do with what is installed
            // — switching modlist, F5, re-selecting the install after Settings — so the
            // preference used to mean "check on every reload", with a network round-trip and
            // a dock takeover each time. The Updates tab's own count badge is how an
            // automatic result announces itself.
            if (CheckModUpdatesOnStartup && !_autoUpdateCheckDone)
            {
                _autoUpdateCheckDone = true;
                _ = CheckUpdatesAsync(announce: false);
            }

            // Conflicts run on EVERY reload, which is startup and modlist switch both —
            // unlike updates, because a conflict is a property of the ACTIVE list and that
            // is exactly what a reload rebuilds. A cached result from the previous list
            // would be a badge describing mods that are no longer loaded.
            //
            // AWAITED, under the load state, and that is the owner's call with a better
            // reason than the one I had: N6 puts a ⚡ badge on every row, so a list rendered
            // before this finishes is a list that changes under the user a second later.
            // Work whose answer lands on a ROW waits; work whose answer lands in a TAB does
            // not — which is exactly why the update check above is started and not awaited.
            //
            // Starting updates first is the whole of the middle ground: the two run
            // concurrently, and since this phase is ~1s warm the network check usually
            // finishes inside it for free. Nobody waits on the network, and in the common
            // case nobody sees a half-populated window either.
            //
            // Skippable, because warm this is a second and the first run after a reboot is
            // eighteen — and a load state with no way out is a hang with a logo. Skipping
            // abandons the WAIT, never the work: the analysis finishes in the background and
            // the rows pick it up.
            LoadPhase = LoadPhase.AnalysingConflicts;
            ScanProgress = default;
            await AnalyzeConflictsAsync(announce: false);
        }
        catch (Exception ex)
        {
            // This is the catch around the WHOLE load — scan, modlist rebuild, validation,
            // conflicts — and it was the only one in the hub that did not log. So the app's
            // most important failure left nothing in the Activity tab or the on-disk file,
            // and "Copy diagnostics bundle" handed over a log that never mentioned it.
            _log.Error(LogSubsystem.Scan, $"Load failed: {ex}");

            // Names the step and a move, rather than eliding a bare framework string in a
            // 24px trimmed bar ("Access to the path '…294100' is denied.").
            StatusText = $"Could not read the mod folders — {ex.Message} "
                + "Check Settings ▸ Paths, then press Refresh.";

            // Both panes were cleared before the scan and neither RefreshCounts nor
            // ApplyFilter is reached on this path, so RefreshEmptyStates never ran and the
            // window sat blank and silent while the one status line scrolled away on the
            // next action. A dedicated state rather than RefreshEmptyStates(): that reads
            // InstalledCount, which this path never computed, so it would happily pick
            // "Everything is active" over a pane blanked by a failed scan.
            LoadFailed = true;
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
        }
    }

    /// <summary>
    /// The panes' load-failed state. Distinct from every count-derived empty state,
    /// because after a failed scan the counts are not merely zero — they were never
    /// computed, and inferring a story from them is how a blanked pane comes to claim
    /// everything is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InactiveIsEmptySuccess), nameof(InactiveIsEmptyNotYet))]
    [NotifyPropertyChangedFor(nameof(InactiveIsEmptyFiltered))]
    [NotifyPropertyChangedFor(nameof(ActiveIsEmptyFiltered), nameof(ActiveIsEmptySuccess))]
    [NotifyPropertyChangedFor(nameof(ActiveIsEmptyNothingActive))]
    private bool _loadFailed;

    /// <summary>
    /// Finds the installed mods that are git working trees and reads each one's state,
    /// for the ⎇ row glyph and the Integrations card. Runs after every scan.
    /// <para>
    /// Cheap on a normal install: discovery is a <c>.git</c> probe per mod folder, so a
    /// library with no repositories launches no processes at all. Only actual repos cost
    /// a git invocation.
    /// </para>
    /// </summary>
    private async Task RefreshGitAsync(InstallPaths paths)
    {
        var mods = _byId.Values.ToList();
        try
        {
            var repos = await Task.Run(() => _git.TrackedRepos(mods));

            // The install's paths can change while this is in flight (Settings, adopt);
            // applying a previous install's git state to the rows now on screen would be
            // silent nonsense.
            if (!Equals(_installPaths, paths)) return;

            _gitTrackedCount = repos.Length;
            if (repos.Length == 0)
            {
                _gitStatuses = ImmutableDictionary<ModId, GitStatus>.Empty;
                return;
            }

            _log.Info(LogSubsystem.Git, $"{repos.Length} installed mod(s) are git working trees");

            if (FetchReposOnStartup)
            {
                var fetched = await _git.FetchAllAsync(repos);
                _log.Info(LogSubsystem.Git, $"Fetched {fetched} of {repos.Length} tracked repo(s)");
            }

            var statuses = await _git.StatusesAsync(repos);
            if (!Equals(_installPaths, paths)) return;

            _gitStatuses = statuses;
            var dirty = statuses.Count(kv => kv.Value.IsDirty);
            _log.Info(LogSubsystem.Git,
                $"git: {statuses.Count} repo(s) read, {dirty} with uncommitted changes");

            // Only revalidate when there is a glyph to add — a revalidate rewrites every
            // row's status slot and rebuilds the Warnings tab, which is not free.
            if (dirty > 0 && ShowGitDirtyOnRows)
            {
                _lastValidationReason = "git status";
                Validate();
            }
        }
        catch (Exception ex)
        {
            // Git is optional and degrades on its own (2k): no glyphs, everything else works.
            _log.Warn(LogSubsystem.Git, $"git scan failed: {ex.Message}");
        }
    }

    // --- noticing a mod folder arrive or leave (N5a4) ------------------------

    private readonly ModRootProbe _rootProbe;
    private readonly ModRootNotice _rootNotice = new();
    private DispatcherTimer? _rootPoll;
    private bool _polling;

    /// <summary>
    /// How often the mod roots are listed. Two seconds: the change was made in another
    /// window, so latency is irrelevant, and listing the real 558-entry Steam root measures
    /// 0.13ms — about 0.007% of a core at this rate.
    /// </summary>
    private static readonly TimeSpan RootPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>The mod folders, as the probe sees them. Empty until an install is loaded.</summary>
    private IReadOnlyList<string> ModRoots =>
        _installPaths is { } paths && !string.IsNullOrWhiteSpace(paths.GameDir)
            ? (paths.WorkshopDir is { } workshop
                ? [paths.LocalModsDir, workshop]
                : [paths.LocalModsDir])
            : [];

    /// <summary>"3 added, 1 removed on disk" — empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModRootStrip))]
    private string _modRootNoticeText = string.Empty;

    public bool ShowModRootStrip => ModRootNoticeText.Length > 0;

    /// <summary>
    /// Starts the poll. Idempotent, so a reload can call it without stacking timers.
    /// </summary>
    private void StartRootPolling()
    {
        if (_rootPoll is not null) return;

        _rootPoll = new DispatcherTimer { Interval = RootPollInterval };
        _rootPoll.Tick += (_, _) => _ = PollRootsAsync();
        _rootPoll.Start();
    }

    /// <summary>
    /// Lists the roots on a background thread and folds the result into the notice.
    /// <para>
    /// Off the UI thread deliberately. The listing is 0.13ms warm, but a Steam library on a
    /// sleeping external drive can block for seconds on the first stat — and a two-second
    /// timer that occasionally freezes the window would be a worse bug than the one this
    /// feature fixes.
    /// </para>
    /// </summary>
    private async Task PollRootsAsync()
    {
        // One at a time: the probe holds per-root state and a slow listing must not have a
        // second poll start behind it.
        if (_polling || IsBusy) return;

        var roots = ModRoots;
        if (roots.Count == 0) return;

        _polling = true;
        try
        {
            var changes = await Task.Run(() => _rootProbe.Poll(roots));
            if (!changes.Any) return;

            _rootNotice.Record(changes);
            ModRootNoticeText = _rootNotice.Text;

            _log.Info(LogSubsystem.Scan,
                $"Mod folders changed: {_rootNotice.Text} ({_rootNotice.Detail})");
        }
        catch (Exception ex)
        {
            // An unplugged drive or a permission change degrades to Refresh, in silence.
            // Vortex paints a watch error on every launch for exactly this and it is noise,
            // not information.
            _log.Warn(LogSubsystem.Scan, $"could not list the mod folders: {ex.Message}");
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>
    /// The strip's verb. Drains the modlist writer <b>first</b>, and that is not tidiness.
    /// <para>
    /// Every edit persists through <c>SerialWriter.Queue</c>, which is fire-and-forget, while
    /// <c>LoadModlistsAsync</c> re-reads the lists from disk. <c>PersistModlist</c>'s
    /// <c>IsBusy</c> guard runs one way only — it stops a reload writing an empty pane over a
    /// real list, and nothing stops the reverse. A reload landing in the flush window would
    /// rebuild the panes from the pre-edit file and silently revert the drag the user just
    /// made. F5 lets them pick a moment when they have stopped dragging; this button does
    /// not, so it has to close the window itself.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RescanModFolders()
    {
        if (_modlistWriter is { } writer) await writer.DrainAsync();

        DismissModRootNotice();
        await Refresh();
    }

    /// <summary>
    /// Dismisses the strip. It comes back on the next change — the offline strip's rule, for
    /// the same reason: a notice that can be silenced for ever stops meaning anything.
    /// </summary>
    [RelayCommand]
    private void DismissModRootNotice()
    {
        _rootNotice.Clear();
        ModRootNoticeText = string.Empty;
    }

    private IDisposable? _gameOrderWatch;
    private string? _watchedConfigPath;

    /// <summary>
    /// Watches the game's <c>ModsConfig.xml</c>, so the drift verdict is live rather than
    /// as old as the last reload.
    /// <para>
    /// <c>N4c</c> recorded the asymmetry this closes: the <em>list</em> side of the
    /// comparison recomputed on every edit while the <em>game</em> side only moved on a full
    /// reload. So the footer could sit reading "in sync" while RimWorld had rewritten the
    /// file minutes earlier — truthful about one side of a question with two.
    /// </para>
    /// <para>
    /// Re-armed on the path, not on every reload: switching install or fixing the config
    /// folder in Settings moves the file being watched, and a watcher left on the old
    /// directory is a watcher reporting someone else's changes.
    /// </para>
    /// </summary>
    private void WatchGameOrder()
    {
        var path = _installPaths?.ConfigDir is { } dir
            ? System.IO.Path.Combine(dir, "ModsConfig.xml")
            : null;

        if (path == _watchedConfigPath) return;

        _gameOrderWatch?.Dispose();
        _watchedConfigPath = path;
        _gameOrderWatch = null;

        if (path is null) return;

        _gameOrderWatch = _watcher.Watch(path, () => Dispatcher.UIThread.Post(OnGameOrderChanged));
    }

    /// <summary>
    /// The game's file changed under us. Re-read, reclassify, and refresh the N5b strip.
    /// <para>
    /// Deliberately not a rescan: the mods on disk have not moved, only which of them the
    /// game has active, and a 548-mod rescan triggered by a file write would make RimWorld's
    /// own save-loading freeze this window. Deliberately not a prompt — the strip does not
    /// block, and the question is still asked at the write (the commit bar), which is the
    /// editor precedent N5b adopted: detect quietly, ask at the write.
    /// </para>
    /// </summary>
    private void OnGameOrderChanged()
    {
        // Our own Apply writes this file, and the watcher cannot tell whose write it was.
        // Recomputing anyway is correct and cheap: after our write the verdict is InSync,
        // which is exactly what it should be.
        if (IsBusy) return;

        var before = Drift;
        RefreshGameOrder();

        // A REPEAT external change while the verdict already reads ChangedOutside is a
        // new event, not a stale one: the counts refresh and a dismissal is lifted — the
        // same "comes back on the next change" rule as the other two strips. Transitions
        // are handled in OnDriftChanged; this covers the no-transition case.
        if (Drift == DriftKind.ChangedOutsideRimManager && Drift == before)
        {
            _gameMovedDismissed = false;
            RefreshGameMovedNotice();
        }

        if (Drift == before) return;

        _log.Info(LogSubsystem.Io,
            $"ModsConfig.xml changed on disk — {before} → {Drift}.");
    }

    private void RefreshGameOrder()
    {
        if (_installPaths?.ConfigDir is not { } configDir) return;

        var path = System.IO.Path.Combine(configDir, "ModsConfig.xml");
        if (!_workspace.FileSystem.FileExists(path)) return;

        try
        {
            _modsConfig = ModsConfigParser.Parse(_workspace.FileSystem.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // A malformed file must not stop the user applying — the write replaces it
            // wholesale anyway. Report it and carry the stale copy, which is no worse
            // than the position before this method existed.
            _log.Warn(LogSubsystem.Io, $"could not re-read ModsConfig.xml: {ex.Message}");
            return;
        }

        RefreshDrift();
    }

    /// <summary>The bar's "Write" — the only path that reaches the game folder.</summary>
    [RelayCommand]
    private async Task ConfirmCommit()
    {
        Commit.Hide();
        await ApplyCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void CancelCommit()
    {
        Commit.Hide();
        _launchAfterWrite = false;   // cancelling ⌘↵ must not still start the game
    }

    /// <summary>The blocked bar's escape hatch: show the user what is in the way.</summary>
    [RelayCommand]
    private void ShowBlockingWarnings() => RevealDock(DockWarnings);
}
