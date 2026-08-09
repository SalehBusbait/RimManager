using RimManager.Core.Abstractions;
using RimManager.Storage.Persistence;

namespace RimManager.Storage.Repositories;

/// <summary>
/// The app-wide UI preferences, as they sit on disk.
/// <para>
/// App-wide, not per-instance: theme, accent, density and sort behaviour follow the
/// person, not the mod list they happen to have open. Paths stay on the instance, which
/// is why Settings ▸ Paths has a Save button and no other page does.
/// </para>
/// <para>
/// <b>Theme is stored by name, not by ordinal.</b> An enum's numeric value is an
/// implementation detail; inserting a fourth theme one day would silently reinterpret
/// every saved file. The name survives that, and an unrecognised one falls back.
/// </para>
/// </summary>
public sealed record AppSettings
{
    // --- appearance (2g; theme roster from design handoff v2) ---------------
    // Theme names: FollowSystem plus the ten AppTheme members; the legacy
    // "Light"/"Dark" still parse (mapped to the Drop Pods pair at load).
    // AccentIndex was removed with the accent picker — an old file's value is
    // simply ignored by deserialization.
    public string Theme { get; init; } = "FollowSystem";
    public int FontIndex { get; init; }
    public bool IsComfortableDensity { get; init; }

    /// <summary>
    /// UI scale as a percentage (<c>2g</c>). Stored as an int so the persisted value is
    /// exactly what the slider showed — a double round-trips as 1.2000000000000002 and
    /// then reads back as 120.00000000000001% on the label.
    /// </summary>
    public int UiScalePercent { get; init; } = 100;
    public bool ShowTagStripes { get; init; } = true;
    public bool ZebraStriping { get; init; }
    public bool ShowPreviewImages { get; init; } = true;

    // --- sorting & rules (2g) -----------------------------------------------
    public bool UseTopologicalSort { get; init; } = true;
    public bool SnapshotBeforeSorting { get; init; } = true;
    public bool OpenDockOnCycleBreak { get; init; } = true;

    /// <summary>Design non-negotiable #8: off, and it stays off.</summary>
    public bool AutoSortAfterActivate { get; init; }

    // WeeklyRuleCheck is gone (N7d): databases sync on every startup now, so a
    // schedule preference would be a knob on a decision the product already made.
    // Old files still carrying the property deserialize fine; unknown JSON members
    // are ignored.

    // --- community databases (N7c) ------------------------------------------
    // All three default ON: each degrades a capability when off, and the page says
    // exactly which one.
    public bool UseCommunityRules { get; init; } = true;
    public bool UseReplacementsDatabase { get; init; } = true;
    public bool UseKnownGoodDatabase { get; init; } = true;

    // Custom source URLs (N7d). Empty = the built-in default; the known-good one is a
    // BASE, because the path under it is per game version.
    public string CommunityRulesUrl { get; init; } = string.Empty;
    public string ReplacementsUrl { get; init; } = string.Empty;
    public string KnownGoodBaseUrl { get; init; } = string.Empty;

    // --- integrations (2g) --------------------------------------------------
    public bool ShowGitDirtyOnRows { get; init; } = true;
    public bool FetchReposOnStartup { get; init; }

    /// <summary>Check the Workshop for mod updates when an instance loads (2j step 4).</summary>
    public bool CheckModUpdatesOnStartup { get; init; }

    /// <summary>Empty means "never chosen" — the default depends on the instance, so it is
    /// filled in once one is loaded rather than baked in here.</summary>
    public string LaunchCommand { get; init; } = string.Empty;

    public string LaunchExtraArguments { get; init; } = string.Empty;

    // --- tags & metadata (2g) -----------------------------------------------

    // --- advanced (2g) ------------------------------------------------------
    public bool ConfirmBeforeApply { get; init; } = true;
    public bool RefuseApplyWithBlockingWarnings { get; init; } = true;

    /// <summary>Index into the log-level control, not the enum's numeric value —
    /// reordering the control must not reinterpret a saved setting.</summary>
    public int LogLevelIndex { get; init; } = 2;

    /// <summary>Unprotected snapshots kept per profile; named and pinned are exempt.</summary>
    public int KeepSnapshots { get; init; } = 100;
}

/// <summary>
/// Persists <see cref="AppSettings"/> at <c>&lt;AppPaths.Root&gt;/settings.json</c>.
/// <para>
/// Written without a backup: it is small, changes on every toggle flip, and is entirely
/// recreatable from its defaults. Backing it up per flip would bury the profile and tag
/// backups that constraint #5 actually exists to protect.
/// </para>
/// </summary>
public sealed class AppSettingsRepository
{
    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly JsonDocumentStore<AppSettings> _store;

    public AppSettingsRepository(IFileSystem fs, string? path = null)
    {
        _fs = fs;
        _path = path ?? Path.Combine(AppPaths.Root, "settings.json");
        _store = new JsonDocumentStore<AppSettings>(fs);
    }

    public string Path_ => _path;

    /// <summary>
    /// Reads the saved preferences, or the defaults when there is no file yet. A corrupt
    /// file must never stop the app starting — losing a theme choice is a nuisance,
    /// failing to launch over one is not a trade worth making.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            return _store.Load(_path) ?? new AppSettings();
        }
        catch (PersistenceException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        _fs.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        await _store.SaveAsync(_path, settings, backup: false, ct).ConfigureAwait(false);
    }
}
