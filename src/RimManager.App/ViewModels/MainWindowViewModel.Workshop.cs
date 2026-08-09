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
    // --- Updates tab (2b) ---------------------------------------------------

    /// <summary>
    /// The header checkbox. Selects the safe set only, or — when the safe set is
    /// already fully ticked — clears everything. <see cref="UpdatesPresenter.IsSafeToBatch"/>
    /// holds the rule; the header never speaks for anything outside it.
    /// </summary>
    [RelayCommand]
    private void ToggleUpdateHeader() => Updates.ToggleHeader(Updates.HeaderChecked != true);

    /// <summary>
    /// Snooze ▾ · 1 week / until next version / until next game version. Persisted, so
    /// the row stays quiet across restarts; snoozes expire by comparison, not a timer.
    /// </summary>
    [RelayCommand]
    private async Task SnoozeUpdate(string kind)
    {
        if (!Enum.TryParse<SnoozeKind>(kind, out var parsed)) return;

        _snoozes = Updates.Snooze(parsed);
        WarningCountsChanged();
        if (_state is not null) await _state.SaveSnoozesAsync(_snoozes);
        StatusText = $"Snoozed {Updates.SelectedTitle}.";
    }

    [RelayCommand]
    private async Task UnsnoozeUpdate()
    {
        _snoozes = Updates.Unsnooze();
        WarningCountsChanged();
        if (_state is not null) await _state.SaveSnoozesAsync(_snoozes);
    }

    /// <summary>Detail panel · "Reveal in list" — select the mod in whichever pane holds it.</summary>
    [RelayCommand]
    private void RevealUpdateRow()
    {
        // Reveal means SEEN: select AND scroll (the warnings' reveal split — the hub
        // selects, the view scrolls). Selecting alone left the row off screen, which
        // reads as the button doing nothing.
        if (Updates.SelectedRow is { } row) RevealByPackageId(row.Id);
    }

    /// <summary>
    /// "Update N selected" — the update goes THROUGH the Workshop, owner's call,
    /// reversing an earlier SteamCMD design that downloaded into the local Mods
    /// folder. That version did produce current bytes, but only by SHADOWING the
    /// subscription (Local &gt; Workshop precedence): the mod silently changed owner
    /// and stopped tracking Workshop updates. Update must mean "the subscription is
    /// now current", so <see cref="IWorkshopUpdater"/> asks the running Steam client
    /// to update the subscribed items in place and the client keeps its own
    /// bookkeeping. What the confirm warns about instead: the Workshop serves only
    /// the latest version, so an update cannot be rolled back — which is the entire
    /// reason this tab is a worklist with checkboxes rather than an auto-updater.
    /// </summary>
    [RelayCommand]
    private async Task UpdateSelected()
    {
        var ids = Updates.Rows
            .Where(r => r.IsSelected && r.PublishedFileId is not null)
            .Select(r => r.PublishedFileId!)
            .Distinct()
            .ToArray();
        await UpdateViaWorkshopAsync(ids);
    }

    /// <summary>The detail panel's per-row "Update this": the same batch, size one.</summary>
    [RelayCommand]
    private async Task UpdateThis()
    {
        if (Updates.SelectedRow?.PublishedFileId is { } id)
            await UpdateViaWorkshopAsync([id]);
    }

    private async Task UpdateViaWorkshopAsync(string[] ids)
    {
        if (ids.Length == 0 || Updates.IsBatchRunning || Confirm is null) return;
        if (_installPaths is not { } paths)
        {
            StatusText = "There is no game install to update against.";
            return;
        }

        var plural = ids.Length == 1 ? "1 mod" : $"{ids.Length} mods";
        var result = await Confirm(new ConfirmRequest(
            $"Update {plural} through Steam?",
            "Asks the Steam client to download today's version of each mod. Your "
            + "subscriptions are not touched; nothing is copied into your Mods folder; "
            + "mod settings and saves are untouched. Workshop updates cannot be rolled "
            + "back: Steam serves only the latest version. Steam may show you as "
            + "in-game for a few seconds while the request is made.",
            Verb: $"Update {plural}"));
        if (!result.Confirmed) return;

        Updates.IsBatchRunning = true;
        using var activity = Activity($"updating · {ids.Length}");
        try
        {
            StatusText = $"Updating {plural} through Steam…";
            Updates.Summary = $"Updating {plural} through Steam…";
            _log.Info(LogSubsystem.Steam, $"Workshop update batch: {ids.Length} item(s)");

            if (Environment.ProcessPath is not { } selfExe)
                throw new InvalidOperationException("Can't locate our own executable for the Steam helper.");
            var updater = new SteamworksWorkshopUpdater(
                selfExe, paths.GameDir, SteamWorkshopClient.RimWorldAppId,
                paths.WorkshopDir);
            var progress = new Progress<string>(line => Updates.Summary = line);
            var requests = ids
                .Select(id => new WorkshopUpdateRequest(id, Updates.RemoteUtcFor(id)))
                .ToArray();
            var outcomes = await updater.UpdateAsync(requests, progress);

            var updated = outcomes.Count(o => o.Updated);
            StatusText = $"Updated {updated}/{ids.Length}; rescanning…";
            await ReloadAsync();

            // Re-check so the rows tell the new truth: an updated subscription is
            // simply current again, and its row leaves the worklist.
            await CheckUpdatesCommand.ExecuteAsync(null);
            StatusText = updated == ids.Length
                ? $"Updated {plural} through Steam."
                : $"Updated {updated}/{ids.Length} — "
                  + (outcomes.FirstOrDefault(o => !o.Updated)?.Detail ?? "some items failed.");
            _log.Info(LogSubsystem.Steam, $"Workshop update batch finished: {updated}/{ids.Length}");
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
            _log.Warn(LogSubsystem.Steam, $"Workshop update batch failed: {ex.Message}");
        }
        finally
        {
            Updates.IsBatchRunning = false;
        }
    }

    /// <summary>
    /// Opens the selected mod's Workshop page. Steam publishes no machine-readable
    /// changelog, so the author's own notes on that page are the real thing the
    /// detail panel would otherwise be pretending to show.
    /// </summary>
    [RelayCommand]
    private void OpenWorkshopPage()
    {
        if (Updates.SelectedRow?.PublishedFileId is not { } id) return;

        if (WorkshopLinks.Open(id) is null) StatusText = "Could not open the Workshop page.";
    }

    /// <summary>Keeps the strip's count pill in step after a snooze changes it.</summary>
    private void WarningCountsChanged() => OnPropertyChanged(nameof(Updates));

    /// <summary>
    /// True once the automatic check has run. Updates are about what is <b>installed</b>,
    /// and a reload happens for reasons that do not change that — switching modlist, F5,
    /// re-selecting the install after Settings. Without this the preference meant "check on
    /// every reload", so switching lists cost a Workshop round-trip and threw the dock open.
    /// The switch path's own comment says it: "switching does not change what is installed
    /// but does change which of it is active."
    /// </summary>
    private bool _autoUpdateCheckDone;

    /// <summary>
    /// Checks all installed Workshop mods against the live Workshop (spec §4.4). The user
    /// asked, so the answer is shown.
    /// </summary>
    [RelayCommand]
    private Task CheckUpdates() => CheckUpdatesAsync(announce: true);

    /// <param name="reveal">
    /// Whether to open the dock on the result. <b>True only when the user asked.</b> It used
    /// to be unconditional, so the startup check took over the bottom of the window before
    /// the lists had settled — an answer to a question nobody had asked yet. The tab strip's
    /// count badge is the notification for an automatic check; the pane is not.
    /// </param>
    private async Task CheckUpdatesAsync(bool announce)
    {
        if (Updates.IsChecking || _byId.IsEmpty) return;
        Updates.IsChecking = true;
        if (announce) StatusText = "Checking for updates…";
        using var activity = Activity("checking updates…");
        try
        {
            var mods = _byId.Values.ToList();
            var workshopDir = _installPaths?.WorkshopDir;
            var result = await Task.Run(() => _updateCheck.CheckAsync(mods, workshopDir));
            Updates.Populate(result, _snoozes, DateTimeOffset.UtcNow, _gameMajorMinor);
            Updates.IsStale = false;
            NoteNetworkSuccess();
            if (announce)
            {
                StatusText = Updates.Summary;
                RevealDock(DockUpdates);
            }
        }
        catch (Exception ex)
        {
            NoteNetworkFailure(ex);

            // NoteNetworkFailure only fires for CONNECTIVITY exceptions, so without this
            // line every other kind — a parse failure, a Steam error payload — reported
            // to the status bar and left nothing behind at all.
            _log.Warn(LogSubsystem.Steam, $"Update check failed: {ex}");

            // 2k: the tab keeps its cached result and BADGES it. Clearing the rows
            // would be a global failure dressed as a per-feature one — the last answer
            // is still the best one we have, it is just no longer known to be current.
            Updates.IsStale = Updates.HasChecked;
            StatusText = NetworkFailure.IsConnectivity(ex)
                ? "Update check could not reach Steam — showing the last result."
                : $"Update check failed: {ex.Message}";
        }
        finally
        {
            Updates.IsChecking = false;
        }
    }

    /// <summary>
    /// Fetches the latest community databases — the load-order rules, and since N7
    /// Mlie's replacements and known-good lists — caches them, and revalidates.
    /// One verb for all three on purpose: they are the same kind of thing, and a user
    /// asked to "sync" should not have to know our taxonomy of upstreams.
    /// </summary>
    [RelayCommand]
    private Task SyncRules() => SyncDatabasesAsync(announce: true);

    /// <summary>The loader started this session's automatic sync (N7d).</summary>
    private bool _databaseSyncStarted;

    /// <param name="announce">
    /// The N5a rule verbatim: the user asked, so answer out loud; nobody asked, so use
    /// the activity zone and the log. The startup sync is the nobody-asked case.
    /// </param>
    private async Task SyncDatabasesAsync(bool announce)
    {
        if (announce) StatusText = "Syncing community databases…";
        using var activity = Activity("syncing databases…");
        try
        {
            // Sync fetches and caches even for a database that is toggled off — caching
            // is not consuming — but only enabled ones reach the fields the app reads.
            // Custom source URLs (N7d) apply here and only here: the caches they fill
            // are read back without caring where the bytes came from.
            var rules = await Task.Run(() => _rulesService.SyncAsync(EffectiveUrl(CommunityRulesUrl)));
            _communityRules = UseCommunityRules ? rules : LoadOrderRules.Empty;
            _rulesSyncError = null;
            NoteNetworkSuccess();
            UpdateRulesStatus();

            // N7 · best-effort after the rules: a failure here must not undo the rules
            // sync that just succeeded, so each falls back to its cache independently.
            var counts = $"Synced {rules.Rules.Count} rule entries";
            var replacementsSynced = false;
            var knownGoodSynced = false;
            try
            {
                var replacements = await Task.Run(
                    () => _modDatabases.SyncReplacementsAsync(EffectiveUrl(ReplacementsUrl)));
                _replacements = UseReplacementsDatabase
                    ? replacements : RimManager.Core.ModDatabases.ReplacementDatabase.Empty;
                _replacementsSyncError = null;
                replacementsSynced = true;
                counts += $" · {replacements.Count} replacements";
                if (_gameMajorMinor is { } version)
                {
                    var knownGood = await Task.Run(
                        () => _modDatabases.SyncKnownGoodAsync(version, EffectiveUrl(KnownGoodBaseUrl)));
                    _knownGood = UseKnownGoodDatabase
                        ? knownGood : RimManager.Core.ModDatabases.KnownGoodDatabase.Empty;
                    _knownGoodSyncError = null;
                    knownGoodSynced = true;
                    counts += $" · {knownGood.Count} known-good";
                }
            }
            catch (Exception ex)
            {
                _log.Warn(LogSubsystem.Rules, $"Mod databases sync failed: {ex.Message}");
                counts += " · mod databases unchanged";

                // The card's own news, but only for the database(s) that did not get
                // their clear this run — a knownGood failure must not repaint the
                // replacements pill that just succeeded. Connectivity stays the
                // offline system's story.
                if (!NetworkFailure.IsConnectivity(ex))
                {
                    if (!replacementsSynced) _replacementsSyncError = ex.Message;
                    if (!knownGoodSynced) _knownGoodSyncError = ex.Message;
                }
            }

            OnPropertyChanged(nameof(ReplacementsStatus));
            OnPropertyChanged(nameof(KnownGoodStatus));
            OnPropertyChanged(nameof(ReplacementsPill));
            OnPropertyChanged(nameof(KnownGoodPill));

            // The new data must reach the rows now, not on the next reload.
            Validate();
            if (announce) StatusText = $"{counts}. Sort to apply rules.";
            else _log.Info(LogSubsystem.Rules, $"Startup sync: {counts}.");
        }
        catch (Exception ex)
        {
            NoteNetworkFailure(ex);

            // The cached rules stay loaded and stay in use — the sorter is not degraded,
            // only its input's age is. That is the whole of "per-feature, never global".
            // A NON-connectivity failure is the rules card's own news (S-INTEG's error
            // pill); connectivity stays the offline strip's.
            if (!NetworkFailure.IsConnectivity(ex)) _rulesSyncError = ex.Message;
            OnPropertyChanged(nameof(RulesPill));

            if (announce)
            {
                StatusText = NetworkFailure.IsConnectivity(ex)
                    ? $"Could not reach the rules database — {NetworkFailure.Age(RulesAge)}'s cache is still in use."
                    : $"Rules sync failed: {ex.Message}";
            }
            else
            {
                _log.Warn(LogSubsystem.Rules, $"Startup sync failed: {ex.Message} — caches still in use.");
            }
        }
    }

    /// <summary>
    /// Status bar zone 2 (<c>1a</c>): the count AND when it was synced, because a count
    /// alone cannot tell you whether the rules are a week or a year old. Reads "cached"
    /// and loses its tick while offline (<c>2k</c>).
    /// </summary>
    private void UpdateRulesStatus()
    {
        _rulesCachedAt = _rulesService.CachedAtUtc();
        RulesStatus = NetworkFailure.RulesStatus(_communityRules.Rules.Count, RulesAge, IsOffline);
        HasCommunityRules = _communityRules.Rules.Count > 0 && !IsOffline;
        OfflineDetail = NetworkFailure.Detail(_communityRules.Rules.Count, RulesAge);
        OnPropertyChanged(nameof(RulesPill));
    }

    private DateTimeOffset? _rulesCachedAt;

    private TimeSpan? RulesAge => _rulesCachedAt is { } at ? DateTimeOffset.UtcNow - at : null;

    // --- 2k · offline, per feature and never global --------------------------

    /// <summary>
    /// Called from every catch around a network call. Only a request that never got an
    /// answer counts — a server that replied "no" is proof we are online.
    /// </summary>
    private void NoteNetworkFailure(Exception ex)
    {
        if (!NetworkFailure.IsConnectivity(ex)) return;
        NoteNetworkUnreachable($"Network unreachable: {ex.Message}");
    }

    /// <summary>
    /// The same thing for a caller that reports failure as a value rather than by
    /// throwing — the collection resolver does, and its exception path was dead.
    /// </summary>
    private void NoteNetworkUnreachable(string reason)
    {
        // Un-dismissed on EVERY fresh failure, not only the first. This line used to
        // sit below the guard, so once you dismissed the strip it stayed hidden for
        // the rest of the outage however many requests failed — which is the "silenced
        // for ever" that the comment on OfflineStripDismissed says it is not. Every
        // network call here is user-initiated, so this cannot nag on its own.
        OfflineStripDismissed = false;

        if (IsOffline) return;

        IsOffline = true;
        UpdateRulesStatus();
        _log.Warn(LogSubsystem.Steam, reason);
    }

    /// <summary>Any answered request clears it, whatever it was for.</summary>
    private void NoteNetworkSuccess()
    {
        if (!IsOffline) return;

        IsOffline = false;
        UpdateRulesStatus();
        _log.Info(LogSubsystem.Steam, "Network reachable again");
    }

    /// <summary>
    /// Retry — the strip names updates and rule sync, so it retries exactly those two.
    /// A "Retry" that quietly re-ran something else would be a third thing to reason about.
    /// </summary>
    [RelayCommand]
    private async Task RetryNetwork()
    {
        StatusText = "Retrying…";
        await SyncRulesCommand.ExecuteAsync(null);
        if (!IsOffline) await CheckUpdatesCommand.ExecuteAsync(null);
    }

    /// <summary>Hides the strip until the next failure — never for ever.</summary>
    [RelayCommand]
    private void DismissOfflineStrip() => OfflineStripDismissed = true;

    /// <summary>
    /// Downloads the <b>ticked</b> members anonymously via SteamCMD into the Mods
    /// folder — the ticks the user made on the wizard's step 2, not everything that
    /// happens to be missing. Runs after the wizard has closed, reporting to the
    /// status bar's activity zone (<c>1a</c>: the only place background progress
    /// appears), so the window is never blocked by it.
    /// </summary>
    private async Task DownloadSelectedAsync()
    {
        if (Collection.IsDownloading || _installPaths is not { } paths) return;

        var wanted = Collection.SelectedForDownload;
        if (wanted.IsDefaultOrEmpty) return;
        var modsDir = paths.LocalModsDir;

        Collection.IsDownloading = true;
        using var activity = Activity($"SteamCMD · {wanted.Length}");
        try
        {
            var provisioner = new SteamCmdProvisioner(AppPaths.SteamCmdDir);
            string exe;
            if (provisioner.IsProvisioned)
            {
                exe = provisioner.ExePath;
            }
            else
            {
                activity.Label = "setting up SteamCMD…";
                StatusText = "Setting up SteamCMD (first run downloads ~200 MB)…";
                exe = await provisioner.EnsureProvisionedAsync();
                activity.Label = $"SteamCMD · {wanted.Length}";
            }

            StatusText = $"Downloading {Plural.Of(wanted.Length, "mod")} via SteamCMD (anonymous)…";
            _log.Info(LogSubsystem.Steam, $"Collection download: {wanted.Length} item(s)");

            var outcomes = await Task.Run(() => _download.DownloadAndInstallAsync(
                wanted, exe, modsDir, AppPaths.SteamCmdDownloadsDir));

            var installed = outcomes.Count(o => o.Installed);

            // No "rescanning…" here any more: ReloadAsync claims the zone itself, and
            // its claim is the newer one, so it is what shows. Saying it twice was how
            // this operation's finally came to write "idle" over a live scan.
            StatusText = $"Downloaded {installed} of {Plural.Of(wanted.Length, "mod")}; rescanning…";

            await ReloadAsync();          // pick up the new mods
            StatusText = $"Installed {installed} of {Plural.Of(wanted.Length, "collection mod")}.";
            _log.Info(LogSubsystem.Steam, $"Collection download finished: {installed}/{wanted.Length}");
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
            _log.Warn(LogSubsystem.Steam, $"Collection download failed: {ex.Message}");
        }
        finally
        {
            Collection.IsDownloading = false;
        }
    }
}
