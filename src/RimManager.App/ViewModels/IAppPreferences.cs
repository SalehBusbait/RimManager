using System.ComponentModel;
using System.Windows.Input;

namespace RimManager.App.ViewModels;

/// <summary>
/// The three theme choices of <c>2g</c>. Three, not a bool: "follow system" is a real
/// third state — it is not "dark", it is "whatever the desktop is right now" — and a
/// bool cannot hold it.
/// </summary>
/// <summary>
/// The theme choice (design handoff v2): ten themes plus follow-system. Members
/// are persisted BY NAME, so renaming one silently resets every install that
/// chose it — the legacy names <c>Light</c>/<c>Dark</c> are mapped to the Drop
/// Pods pair at load by <see cref="Themes.ThemeCatalog.Parse"/>.
/// </summary>
public enum AppTheme
{
    FollowSystem,
    DropPodsDark,
    DropPodsLight,
    Tribal,
    Arid,
    Ice,
    Toxic,
    Mech,
    Royalty,
    Anomaly,
    Glitter,
}

/// <summary>
/// The preferences Settings edits and the main window acts on.
/// <para>
/// Settings binds straight through to the live view model rather than holding copies
/// it writes back on Save. Two stores for one preference is how a Sort flyout and a
/// Settings page end up disagreeing about whether separators are kept — and the one
/// the sorter reads is whichever was written last. There is one instance of this
/// state and everything edits it.
/// </para>
/// <para>
/// The consequence, stated on the page: these take effect immediately, unlike the
/// paths, which are committed by Save.
/// </para>
/// </summary>
public interface IAppPreferences : INotifyPropertyChanged
{
    // --- sorting & rules (2g) -----------------------------------------------
    bool UseTopologicalSort { get; set; }

    /// <summary>Display state only — the choice fires from the two commands below
    /// (the log-level shape; a TwoWay radio pair over an inverse property wedged
    /// after a mode switch).</summary>
    bool UseAlphabeticalSort { get; }

    System.Windows.Input.ICommand ChooseTopologicalSort { get; }
    System.Windows.Input.ICommand ChooseAlphabeticalSort { get; }
    bool SnapshotBeforeSorting { get; set; }

    /// <summary>Open the dock on Warnings when a sort breaks a cycle.</summary>
    bool OpenDockOnCycleBreak { get; set; }

    /// <summary>
    /// Off by default and stays off (design non-negotiable #8). The page prints the
    /// reason underneath rather than leaving it as an unexplained default.
    /// </summary>
    bool AutoSortAfterActivate { get; set; }

    string RulesStatus { get; }

    /// <summary>
    /// Fetching the community rules is the same action wherever it is triggered from,
    /// so Settings borrows the command rather than growing a second path to the same
    /// network call.
    /// </summary>
    ICommand SyncRules { get; }

    // --- community databases (N7c) ------------------------------------------
    // No schedule preference: the databases sync on every startup (N7d, owner's
    // call). The toggles below are the only control — use it, or don't.

    /// <summary>Feed the community rules to the sorter and validator. Off = About.xml
    /// and your own rules alone.</summary>
    bool UseCommunityRules { get; set; }

    /// <summary>Flag installed mods that have a maintained replacement (UseThisInstead).</summary>
    bool UseReplacementsDatabase { get; set; }

    /// <summary>Trust Mlie's known-good list: suppress the unsupported-version warning
    /// for mods reported working on the running game version (NoVersionWarning).</summary>
    bool UseKnownGoodDatabase { get; set; }

    /// <summary>"2,648 rules · synced 5h ago", "Not synced yet", or "Off".</summary>
    string ReplacementsStatus { get; }

    string KnownGoodStatus { get; }

    /// <summary>The card pills (T5): one word each — active / not synced / off —
    /// elaborated by the status line beneath.</summary>
    DatabasePill RulesPill { get; }

    DatabasePill ReplacementsPill { get; }

    DatabasePill KnownGoodPill { get; }

    /// <summary>Custom source URLs (N7d). Empty = the built-in default, which the field
    /// shows as its placeholder. The known-good one is a BASE URL — the path under it
    /// is per game version.</summary>
    string CommunityRulesUrl { get; set; }

    string ReplacementsUrl { get; set; }

    string KnownGoodBaseUrl { get; set; }

    // --- integrations (2g) --------------------------------------------------


    /// <summary>
    /// Show the <c>⎇</c> status glyph on rows whose git working tree has uncommitted
    /// changes. On by default, and it drives the row status directly — the glyph and
    /// this toggle are the same fact.
    /// </summary>
    bool ShowGitDirtyOnRows { get; set; }

    /// <summary>
    /// Fetch every tracked repo when an instance loads. Off by default: it is one
    /// network call per repo on every launch, and fetch is the only write git is ever
    /// allowed to make.
    /// </summary>
    bool FetchReposOnStartup { get; set; }

    /// <summary>Check the Workshop for mod updates when the install loads. Set on
    /// Settings ▸ Integrations, and offered again by first run's step 4.</summary>
    bool CheckModUpdatesOnStartup { get; set; }

    /// <summary>
    /// The command that starts RimWorld, holding <c>%args%</c> where
    /// <see cref="LaunchExtraArguments"/> is substituted (<c>2g</c>).
    /// </summary>
    string LaunchCommand { get; set; }

    /// <summary>Arguments passed to the game, e.g. <c>-logfile … -popupwindow</c>.</summary>
    string LaunchExtraArguments { get; set; }

    /// <summary>Puts the launch command back to this instance's default (<c>2g</c>: Reset).</summary>
    ICommand ResetLaunchCommand { get; }

    // --- tags & metadata (2g) -----------------------------------------------



    // --- advanced (2g) ------------------------------------------------------

    /// <summary>
    /// Raise the inline Apply bar rather than writing straight away. On by default, and
    /// the page says what it is: an inline bar, not a dialog (#4).
    /// </summary>
    bool ConfirmBeforeApply { get; set; }

    /// <summary>
    /// Refuse to Apply while blocking warnings exist. On by default: a missing dependency
    /// means the game fails to load, and finding that out from RimWorld's own error
    /// screen is far worse than being stopped here.
    /// </summary>
    bool RefuseApplyWithBlockingWarnings { get; set; }

    /// <summary>
    /// The log's level floor, indexing <see cref="LogLevels.Choices"/>. Info by default —
    /// Debug and Trace are for reproducing something, not for living at.
    /// </summary>
    /// <summary>
    /// How many unprotected snapshots to keep per profile. Named and pinned ones are
    /// exempt and do not count against it — the point of naming a state is that it
    /// outlives the rolling window (<c>2d</c>).
    /// </summary>
    int KeepSnapshots { get; set; }

    /// <summary>Opens the folder ModsConfig.xml backups are written to.</summary>
    ICommand OpenBackupFolder { get; }

    int LogLevelIndex { get; set; }

    /// <summary>What raising the level floor actually costs, said on the page.</summary>
    string LogLevelNote { get; }

    /// <summary>The parsed-mod cache's size, so "rebuild" states what it discards.</summary>
    string ScanCacheSummary { get; }

    // Borrowed rather than duplicated: each of these already exists as the one
    // action behind a menu item or a dock button, and a second copy is how two
    // routes to one effect come apart.
    ICommand OpenLogFolder { get; }
    ICommand CopyDiagnostics { get; }
    ICommand ResetLayout { get; }
    ICommand RebuildScanCache { get; }

    /// <summary>Danger zone (2i-6 confirms both).</summary>
    ICommand DeleteAllSnapshots { get; }
    ICommand ResetRimManager { get; }

    /// <summary>Settings ▸ Sorting borrows the editor rather than opening a second one.</summary>
    ICommand OpenRuleEditor { get; }

    // --- appearance (2g) ----------------------------------------------------

    /// <summary>
    /// The chosen theme, and the <b>only</b> place it is stored.
    /// <para>
    /// There used to be two: this flag and a <c>ThemeIndex</c> on the Settings view
    /// model. Neither knew about the other, which produced exactly the failure the
    /// one-store rule exists to prevent — the Appearance radios wrote a bool nothing
    /// applied, so they did nothing at all, and Save re-applied the index captured when
    /// the window opened, silently reverting a theme changed from the menu.
    /// </para>
    /// </summary>
    AppTheme Theme { get; set; }

    // AccentIndex is gone (design handoff v2): accents are theme-bound — each theme's
    // dictionary authors its full accent family, and the picker was removed with it.

    /// <summary>
    /// Which UI font is in use, indexing <see cref="UiFonts.Choices"/>. Only the UI font:
    /// <c>2g</c> states that the mono column font follows the system monospace, and it is
    /// not offered — a mod list's aligned columns depend on it actually being monospaced.
    /// </summary>
    int FontIndex { get; set; }

    /// <summary>UI scale as a percentage (2g). 100 is unscaled.</summary>
    int UiScalePercent { get; set; }

    /// <summary>"120%", for the readout beside the slider.</summary>
    string UiScaleText { get; }
    bool IsComfortableDensity { get; set; }
    bool ShowTagStripes { get; set; }
    bool ZebraStriping { get; set; }
    bool ShowPreviewImages { get; set; }
}
