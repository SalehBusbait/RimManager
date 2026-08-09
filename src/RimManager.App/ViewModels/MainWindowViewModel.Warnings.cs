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
    /// <summary>
    /// Warnings toolbar ⌘R. Re-runs validation against the current load order without
    /// rescanning disk — the question it answers is "are these numbers still true",
    /// which is cheap, where a rescan is not.
    /// </summary>
    [RelayCommand]
    private void Revalidate()
    {
        _lastValidationReason = "manual";
        Validate();
    }

    /// <summary>
    /// Copies the whole warnings table as plain text, group headers included, so it
    /// can be pasted into a bug report — the same reason the Activity panel exists.
    /// </summary>
    [RelayCommand]
    private async Task CopyWarningsReport()
    {
        var lines = new List<string>();
        foreach (var row in WarningsPanel.Rows)
        {
            lines.Add(row.IsGroupHeader
                ? $"{row.Issue} ({row.Count})"
                : $"  [{row.Category}] {row.Issue}{row.IssueMono}{row.IssueTail}"
                  + (row.ModName.Length > 0 ? $" — {row.ModName}" : string.Empty));
        }

        var text = string.Join(Environment.NewLine, lines);
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow.Clipboard: { } clipboard })
        {
            var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(Avalonia.Input.DataTransferItem.Create(
                Avalonia.Input.DataFormat.Text, text));
            await clipboard.SetDataAsync(transfer);
            StatusText = $"Copied {WarningsPanel.Rows.Count} warning rows.";
        }
    }

    public void Validate()
    {
        var activeMods = ActiveRows.OfType<ModRowViewModel>().Select(r => r.Mod).ToList();
        // The inactive pane is passed too, so its rows carry the INTRINSIC warnings —
        // does this mod declare my game version — while none of the relational ones
        // reach it. An inactive mod has no position, breaks nothing, and its dependency
        // is not missing because nothing is asking for it (N2 · UI-7.2).
        var inactiveMods = InactiveRows.OfType<ModRowViewModel>().Select(r => r.Mod).ToList();

        // _communityRules, which the validator was NOT being given while the sorter was.
        // The two have to see the same rules or the dock lies about a sorted list: Sort
        // honoured 629 community rules, and Warnings — reading About.xml alone — could
        // neither report a violated community rule nor explain a move the sorter made
        // because of one. The CLI passed them to both all along; only the GUI diverged.
        var report = new ModListValidator().Validate(
            activeMods, _knownExpansions, _gameMajorMinor, _communityRules, inactiveMods,
            // N7 · the two Mlie databases: known-good suppresses unsupported-version
            // for listed mods; replacements add the intrinsic Replaceable finding.
            _knownGood, _replacements.Replacements,
            // The rule editor's output rides every validation the same way it rides
            // every sort: a rule the user disabled must stop warning, and one they
            // wrote must start.
            overrides: _ruleOverrides);

        Warnings.Clear();
        foreach (var issue in report.Issues) Warnings.Add(issue);

        // Populate BEFORE pushing to the rows: the panel merges the validation report
        // with the sort's broken edges and the scan's duplicates, and the rows want all
        // three. Pushing first would give every row the report's view of itself and
        // leave a cycle or a duplicate showing in the dock and on no row at all.
        WarningsPanel.Populate(
            report.Issues, _scanWarnings, PositionOf, ModNames(),
            $"Revalidated {DateTime.Now:HH:mm:ss} · {_lastValidationReason}");

        ApplyValidationToRows(report);

        // Mod info is looking at one of those rows, and a revalidate is exactly when
        // its warnings change. Without this the pane keeps the previous pass's list —
        // the "computed but never announced" shape that R9 found four times.
        RefreshWarningsForSelection();

        // Taken from the panel, not the report: the panel also carries the sort's
        // broken edges and the scan's duplicates. A strip pill that disagrees with
        // the tab it opens is worse than no pill.
        WarningCount = WarningsPanel.AllCount;
        _log.Info(LogSubsystem.Validate,
            $"Validation: {report.ErrorCount} blocking, {report.WarningCount} warning");

        // The Warnings filter depends on this result, so refresh it when active.
        if (WarningsOnly) ApplyFilter();
    }

    /// <summary>
    /// Pushes validation results into each row's single status slot (1f). An error
    /// raises Broken, a warning raises Warning, and RaiseStatus keeps the highest —
    /// so a mod that is both broken and out of date still reads as broken.
    /// </summary>
    private void ApplyValidationToRows(ValidationReport report)
    {
        var rows = ActiveRows.OfType<ModRowViewModel>()
            .Concat(InactiveRows.OfType<ModRowViewModel>())
            .ToDictionary(r => r.PackageId);

        foreach (var row in rows.Values)
            row.Status = row.Mod.HasErrors ? RowStatus.Warning : RowStatus.None;

        // Git is the LOWEST-priority status, so it is raised first and anything the
        // validator finds outranks it. Applied here rather than where it is measured
        // because the reset above would otherwise wipe it on the next revalidate —
        // which is how the ⎇ glyph would have appeared once and then vanished.
        if (ShowGitDirtyOnRows)
        {
            foreach (var (id, status) in _gitStatuses)
            {
                if (status.IsDirty && rows.TryGetValue(id, out var row))
                    row.RaiseStatus(RowStatus.GitDirty);
            }
        }

        // The rows read the PANEL's entries, not the report's issues: the panel is the
        // only place all three sources meet (validation, the sort's broken edges, the
        // scan's duplicates), and a row that disagreed with the dock about whether it
        // has a warning is worse than a row with no warning at all.
        //
        // ONE attribution rule: a warning belongs to the mod that DECLARED it. A row's
        // glyph therefore means "something this mod declared is not satisfied" — never
        // "somebody else wrote a rule about you".
        //
        // It was subject-and-related, which put four warnings on RimWorld, a mod that
        // declares nothing whatsoever. Subject alone is not enough either: an edge is
        // built from whichever mod wrote the rule, so XmlExtensions' own
        // `loadAfter Ludeon.RimWorld` produces the edge rimworld -> xmlextensions, whose
        // SUBJECT is the base game. Only the provenance knows who wrote it.
        var carried = new Dictionary<ModId, List<RowWarning>>();

        foreach (var entry in WarningsPanel.All.Where(e => !e.IsGroupHeader))
        {
            if (entry.Owner is not { } id || !rows.TryGetValue(id, out var row)) continue;

            row.RaiseStatus(entry.Tone == WarningTone.Blocking
                ? RowStatus.Broken
                : RowStatus.Warning);

            if (!carried.TryGetValue(id, out var list)) carried[id] = list = [];

            // MessageFor, not FullIssue: the stored text has its subject elided for the
            // dock's table, and the owner is frequently NOT the subject — on
            // XmlExtensions' row the sentence would otherwise read "Should load before
            // imranfish.xmlextensions", which is its own packageId.
            list.Add(new RowWarning(WarningsPresenter.MessageFor(entry, id), entry.Tone));
        }

        foreach (var row in rows.Values)
        {
            row.Warnings = carried.TryGetValue(row.PackageId, out var found)
                ? RowWarnings.ForList(found)
                : [];
        }
    }

    private ImmutableArray<RimManager.Core.Analysis.ModConflict> _lastConflicts = [];

    /// <summary>
    /// Runs Tier-2 conflict analysis over the active list (spec §4.5 differentiator). The
    /// user asked, so the answer is shown.
    /// </summary>
    [RelayCommand]
    private Task AnalyzeConflicts() => AnalyzeConflictsAsync(announce: true);

    /// <param name="reveal">
    /// Whether to open the dock on the result — true only when the user asked, the same rule
    /// the update check follows. The tab's count badge is how an automatic result announces
    /// itself.
    /// </param>
    private async Task AnalyzeConflictsAsync(bool announce)
    {
        // Queue rather than drop. Dropping was right while this was manual-only — a
        // double-click should not start a second Cecil pass — but it became a bug the
        // moment it ran automatically: switching lists while the previous list's scan is
        // still going would silently skip the new one, leaving the tab describing mods that
        // are no longer loaded. Which is the exact failure "runs on every reload" exists to
        // prevent. Latest-wins, like SerialWriter.
        if (Conflicts.IsAnalyzing)
        {
            _conflictScanQueued = true;
            return;
        }

        var mods = ActiveRows.OfType<ModRowViewModel>().Select(r => r.Mod).ToList();
        if (mods.Count == 0)
        {
            if (announce) StatusText = "No active mods to analyze.";
            return;
        }

        Conflicts.IsAnalyzing = true;
        if (announce) StatusText = "Analyzing conflicts…";

        // The slowest thing in the app — Cecil over every active assembly — and it
        // was the one that most looked like a hang, because zone 5 said "idle".
        using var activity = Activity($"scanning {mods.Count} mods…");
        try
        {
            var version = _gameMajorMinor;
            var gameDir = _installPaths?.GameDir;
            var started = DateTime.Now;
            // Progress only when the load state is up to show it. A user-invoked scan runs
            // with the lists on screen, where the card does not exist and the status bar's
            // activity zone is the surface.
            var progress = announce ? null : LoadProgress();
            var report = await Task.Run(() => _conflictAnalysis.Analyze(mods, version, gameDir, progress));
            _lastConflicts = report.Conflicts;
            Conflicts.Populate(report);
            _log.Info(LogSubsystem.Scan,
                $"Conflicts: scanned {mods.Count} mods · {(DateTime.Now - started).TotalSeconds:0.#}s · {Conflicts.Summary}");
            ApplyConflictBadgesToRows();

            // No dock to reveal since N6c: the ⚡ marks on the rows ARE the result, and
            // the status line answers the user who asked.
            if (announce) StatusText = Conflicts.Summary;
        }
        catch (Exception ex)
        {
            // A background failure still goes in the log and leaves the tab's own state
            // alone; it does not overwrite the line describing the list the user just
            // opened. A failure they asked for is theirs to read.
            if (announce) StatusText = $"Conflict analysis failed: {ex.Message}";
            else _log.Warn(LogSubsystem.Scan, $"Conflict analysis failed: {ex.Message}");
        }
        finally
        {
            Conflicts.IsAnalyzing = false;

            if (_conflictScanQueued)
            {
                _conflictScanQueued = false;

                // Fire-and-forget, unlike the reload's call: nobody is waiting on this one.
                // The load state has already come down by the time a queued re-run starts.
                _ = AnalyzeConflictsAsync(announce: false);
            }
        }
    }

    /// <summary>A reload arrived while a scan was running; run once more when it finishes.</summary>
    private bool _conflictScanQueued;

    /// <summary>
    /// The import wizard (<c>2i</c>-3). Raised as an event rather than opening a
    /// window, for the same reason the XML diff is: the view model has to stay
    /// constructible without one.
    /// </summary>
    public event Action<ImportCollectionViewModel>? ImportCollectionRequested;

    /// <summary>File ▸ Import Steam collection… — opens the modal wizard.</summary>
    /// <remarks>
    /// Refuses while the scan is still running. Every number the wizard states — the
    /// four counts, and how many mods Replace would deactivate — is measured against
    /// the scan; without one, all 476 members read "need downloading" and the Replace
    /// radio offers to clear a load order it cannot see. A wrong number that looks
    /// right is worse than a wait.
    /// </remarks>
    [RelayCommand]
    private void ImportCollection()
    {
        if (IsBusy || _byId.IsEmpty)
        {
            StatusText = "Still reading your mod folders — the import needs the scan to tell installed from missing.";
            return;
        }

        var wizard = new ImportCollectionViewModel(
            url => ResolveCollectionAsync(url),
            PositionOf,
            memberIds => ImportCollectionPresenter.WouldDeactivate(ActiveRows, memberIds).Length,
            openCollectionPage: id => WorkshopLinks.Open(id),
            steamClientRunning: new SteamClientDetector().IsClientRunning());

        wizard.Collection.Strategy = Collection.Strategy;   // last choice, not a reset
        wizard.Url = Collection.Url;
        ImportCollectionRequested?.Invoke(wizard);
    }

    /// <summary>
    /// The wizard's fetch, routed through the offline state so a collection lookup that
    /// cannot reach Steam raises the strip like any other network call. The wizard keeps
    /// reporting the failure in its own hint line — it is modal, and a strip behind a
    /// modal is a message nobody reads.
    /// </summary>
    private async Task<CollectionResolution> ResolveCollectionAsync(string url)
    {
        // Branching on the RESULT, not on an exception. CollectionService reports
        // failure as a value — the wizard prints it in its own hint line — so the
        // catch that used to be here could never run, and the success path below it
        // ran on a failed lookup and cleared the offline state. Offline, pasting a
        // collection URL took the strip down instead of putting it up.
        var result = await _collection.ResolveAsync(url, _byId.Values.ToList());

        if (result.Offline) NoteNetworkUnreachable("Collection lookup could not reach Steam");
        else if (result.Ok) NoteNetworkSuccess();

        return result;
    }

    /// <summary>
    /// Carries out a committed wizard. Adopts its collection model wholesale rather
    /// than re-projecting the report — the ticks the user made on step 2 are the input
    /// to both halves of the commit, and rebuilding the rows here would silently
    /// discard them.
    /// <para>
    /// The activation happens now; the SteamCMD batch is started and left to run. It
    /// reports to the status bar's activity zone, which <c>1a</c> makes the only place
    /// background progress appears — a modal that owned a twenty-minute download would
    /// be exactly the thing "nothing modal for background work" forbids.
    /// </para>
    /// </summary>
    public void CompleteCollectionImport(ImportCollectionViewModel wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        if (!wizard.Accepted || wizard.Report is not { } report) return;

        Collection = wizard.Collection;
        _log.Info(LogSubsystem.Ui,
            $"Collection import: {report.Members.Length} members, strategy {Collection.Strategy}");

        // NF-10's landing, for coherence: both sharing vehicles can arrive as a new
        // modlist. Fire-and-forget like the download below — the wizard has closed,
        // and the switch reports through the load state like any other.
        if (Collection.Strategy == ImportStrategy.NewModlist)
            _ = CreateModlistFromCollectionAsync();
        else
            ActivateCollectionPresent();

        // The route the user picked, carried out — never swapped for the other one.
        // Subscribe and SteamCMD leave the install in different states, so a silent
        // fallback would hand back a result nobody chose.
        if (wizard.Route == ImportRoute.SubscribeInSteam)
        {
            if (Collection.ToDownloadCount > 0 && Collection.CollectionId is { } id) OpenCollectionPage(id);
        }
        else if (Collection.CanDownload)
        {
            _ = DownloadSelectedAsync();
        }
    }

    /// <summary>Hands the collection to the Steam client for its native "Subscribe to all".</summary>
    private void OpenCollectionPage(string id)
    {
        StatusText = WorkshopLinks.OpenToSubscribe(id) switch
        {
            { } url when url.StartsWith("steam://", StringComparison.Ordinal) =>
                "Opened the collection in Steam — its \"Subscribe to all\" keeps the mods Steam-managed.",
            not null => "Steam didn't take the link, so the collection is open in your browser instead.",
            _ => $"Couldn't open the collection. URL: {SteamUrls.WebFilePage(id)}",
        };
    }

    /// <summary>
    /// Carries out the strategy chosen in the wizard (<c>2i</c>-3) against the ticked
    /// members that are already installed — the "present" third of <c>2e</c>'s
    /// reconcile, which needs no download at all.
    /// <list type="bullet">
    /// <item>Append as a group: they arrive under one separator named after the
    /// collection, so an import that turned out to be a mistake can be undone by eye
    /// rather than by memory. The default.</item>
    /// <item>Merge and sort: no separator, then a full sort.</item>
    /// <item>Replace: everything this collection does not name goes inactive first.</item>
    /// </list>
    /// All three are one undo entry and one snapshot, because they are one act.
    /// </summary>
    private void ActivateCollectionPresent()
    {
        var wanted = Collection.Members
            .Where(m => m is { IsSelected: true, IsPresent: true })
            .Select(m => m.PublishedFileId)
            .ToHashSet(StringComparer.Ordinal);

        var rows = InactiveRows.OfType<ModRowViewModel>()
            .Where(r => r.Mod.PublishedFileId is { } id && wanted.Contains(id))
            .ToList();

        // Replace clears first, and uses the SAME predicate the wizard counted with —
        // the sentence there ("deactivates the 155 mods not in this collection") is a
        // promise this line has to keep.
        var deactivated = 0;
        if (Collection.Strategy == ImportStrategy.Replace)
        {
            var memberIds = Collection.Members
                .Select(m => m.PublishedFileId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var row in ImportCollectionPresenter.WouldDeactivate(ActiveRows, memberIds))
            {
                ActiveRows.Remove(row);
                InactiveRows.Add(row);
                deactivated++;
            }
        }

        if (rows.Count == 0 && deactivated == 0) return;

        if (Collection.Strategy == ImportStrategy.AppendGroup && rows.Count > 0)
            ActiveRows.Add(NewSeparator(Collection.Title));

        foreach (var row in rows)
        {
            InactiveRows.Remove(row);
            ActiveRows.Add(row);
        }

        ActiveListOps.Renumber(ActiveRows);
        ActiveListOps.Renumber(InactiveRows);
        RefreshCounts();
        ApplyFilter();
        Validate();
        CommitChange();

        StatusText = deactivated > 0
            ? $"Replaced the load order: {rows.Count} in, {deactivated} deactivated."
            : $"Activated {rows.Count} mod{(rows.Count == 1 ? "" : "s")} from the collection.";
        _log.Info(LogSubsystem.Ui,
            $"Collection ({Collection.Strategy}): activated {rows.Count}, deactivated {deactivated}");

        // After the move, so the sort sees the finished arrangement. It takes its own
        // snapshot, which is what "snapshot taken first" on that radio refers to.
        if (Collection.Strategy == ImportStrategy.MergeAndSort) SortCommand.Execute(null);
    }

    /// <summary>
    /// The wizard's fourth strategy (NF-10 coherence): a NEW modlist from the ticked
    /// members that are installed, in collection order, switched to — the current
    /// list untouched. Only present members can be entries: a collection names mods
    /// by Workshop id alone, and an entry needs a packageId to be addressable, which
    /// arrives with the install. The missing ticked members still download or
    /// subscribe per the chosen route and land in the new list's inactive pane.
    /// </summary>
    private async Task CreateModlistFromCollectionAsync()
    {
        if (_modlistRepo is null) return;
        if (IsBusy)
        {
            StatusText = "Still reading your mod folders — the new list needs the scan to finish.";
            return;
        }

        var byFileId = _byId.Values
            .Where(m => m.PublishedFileId is not null)
            .GroupBy(m => m.PublishedFileId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = Collection.Members
            .Where(m => m is { IsSelected: true, IsPresent: true })
            .Select(m => byFileId.GetValueOrDefault(m.PublishedFileId))
            .Where(m => m is not null)
            .Select(m => ModlistEntry.Mod(m!))
            .ToList();

        var name = RwListWorkshopImport.UniqueName(
            Collection.Title, "collection.rwlist", Modlists.Select(l => l.Name));
        var created = await _modlistRepo.CreateAsync(
            name, ModlistState.Empty.WithEntries(entries));
        _log.Info(LogSubsystem.Ui,
            $"Collection import: created modlist '{name}' with {entries.Count} mods");

        await RefreshModlistsAsync();
        await SwitchModlistAsync(created);

        StatusText = SelectedModlist?.Id == created.Id
            ? $"Created “{name}” with {entries.Count} mods from the collection."
            : $"Created “{name}” — open it from the switcher.";
    }

    // --- rule editor (2i-5) --------------------------------------------------

    /// <summary>The user's rule overrides for this instance, loaded with the scan.</summary>
    private RuleOverrides _ruleOverrides = RuleOverrides.Empty;

    /// <summary>Raised so the view owns the window; the editor is NON-modal (2i-5).</summary>
    public event Action<RuleEditorViewModel>? RuleEditorRequested;

    [RelayCommand]
    private void OpenRuleEditor()
    {
        if (_byId.IsEmpty)
        {
            StatusText = "Nothing scanned yet — the rule editor needs a mod list.";
            return;
        }

        var editor = new RuleEditorViewModel(
            [.. _byId.Values],
            _communityRules,
            _ruleOverrides,
            async overrides =>
            {
                _ruleOverrides = overrides;
                if (_state is not null) await _state.SaveRuleOverridesAsync(overrides);

                // The sorter reads these, so the status line says the change is pending
                // rather than pretending the order already reflects it.
                UpdateRulesStatus();
                StatusText = "Rules changed — sort to apply them.";
            });

        editor.Initialise();
        RuleEditorRequested?.Invoke(editor);
    }

    /// <summary>Help ▸ About. Raised so the view owns the window (2i-9).</summary>
    public event Action<AboutViewModel>? AboutRequested;

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(new AboutViewModel());

    // --- dependency resolver (2i-4) -----------------------------------------

    /// <summary>Raised so the view can show the modal; the view model stays window-free.</summary>
    public event Action<DependencyResolverViewModel>? DependencyResolverRequested;

    /// <summary>
    /// A warning group's bulk action. Missing dependencies open the resolver;
    /// "Fix all N" on the order group runs Sort — the machine that fixes order rules
    /// is the sorter, and a bulk button pretending otherwise would hand-move rows
    /// the next sort moves back.
    /// </summary>
    [RelayCommand]
    private async Task BulkResolve(WarningEntry? entry)
    {
        if (entry is null || !entry.HasBulkAction) return;

        if (entry.Group == WarningGroup.LoadOrder) await Sort();
        else if (entry.Group == WarningGroup.MissingDependencies) OpenDependencyResolver();
    }

    // --- the per-warning fixes (2a, wired at last) ---------------------------

    /// <summary>Raised so the view can scroll the revealed row into frame — selection
    /// is the hub's, scrolling is the ListBox's.</summary>
    public event Action<RowViewModel>? WarningRevealRequested;

    /// <summary>
    /// The row FIX button and the detail's primary. Three verbs, each doing what its
    /// label says: Find opens a Workshop search for the dependency's packageId,
    /// Activate moves the inactive dependency into the order (one undoable edit),
    /// Review reveals the affected row in the lists.
    /// </summary>
    [RelayCommand]
    private void RunWarningFix(WarningEntry? entry)
    {
        if (entry is null || entry.IsGroupHeader || !entry.HasFix) return;

        switch (entry.Fix)
        {
            case "Find":
                var term = (entry.Related ?? entry.Subject)?.Display ?? entry.IssueMono;
                if (WorkshopLinks.OpenSearch(term) is null)
                    StatusText = "Could not open the Workshop search.";
                else
                    _log.Info(LogSubsystem.Ui, $"Workshop search opened for {term}");
                break;

            case "Activate":
                if (entry.Related is not { } dep) return;
                if (ActivateDependency(dep) is { } index)
                {
                    StatusText = $"Activated {NameFor(dep)} — now #{index}.";
                    RevealByPackageId(dep);
                }
                else
                {
                    StatusText = $"{NameFor(dep)} is not in the inactive pane — rescan?";
                }
                break;

            case "Review":
                if (entry.Subject is { } subject) RevealByPackageId(subject);
                break;
        }
    }

    /// <summary>
    /// The detail panel's resolution buttons. "fix" is the row fix again (the panel
    /// restates it with room for a sentence); "accept" pins the sorter's dropped edge;
    /// "edit-rule" opens the editor the tip promises.
    /// </summary>
    [RelayCommand]
    private void RunWarningAction(WarningAction? action)
    {
        switch (action?.Id)
        {
            case "fix": RunWarningFix(WarningsPanel.Selected); break;
            case "accept": AcceptDroppedEdge(); break;
            case "edit-rule": OpenRuleEditor(); break;
        }
    }

    /// <summary>
    /// Pins the sorter's choice on the MODLIST (R1b's EdgeSuppressions — persisted
    /// with the list, passed into every sort): every later sort drops the same edge
    /// instead of re-deciding, which is what makes a broken cycle stay resolved the
    /// same way. The suppression rides <see cref="Modlist.Suppressions"/>, so
    /// PersistModlist writes it with the arrangement.
    /// </summary>
    private void AcceptDroppedEdge()
    {
        if (WarningsPanel.Selected is not { Group: WarningGroup.Cycles, CycleIndex: >= 0 } entry
            || WarningsPanel.LastSort is not { } sort
            || entry.CycleIndex >= sort.BrokenEdges.Length
            || SelectedModlist is not { } list)
        {
            return;
        }

        var edge = sort.BrokenEdges[entry.CycleIndex].Edge;
        SelectedModlist = list with
        {
            Suppressions = list.Suppressions.With(
                edge.Before, edge.After, "accepted from Warnings"),
        };
        PersistModlist();

        StatusText = $"Pinned — every sort of this list now drops "
            + $"{NameFor(edge.Before)} → {NameFor(edge.After)}.";
        _log.Info(LogSubsystem.Sort,
            $"Edge suppression pinned: {edge.Before.Display} → {edge.After.Display}");
    }

    /// <summary>An affected row in the warning detail: click reveals it in the lists.</summary>
    [RelayCommand]
    private void RevealAffected(WarningAffectedRow? row)
    {
        if (row?.Id is { } id) RevealByPackageId(id);
    }

    private void RevealByPackageId(ModId id)
    {
        if (SelectByPackageId(id) is { } row) WarningRevealRequested?.Invoke(row);
    }

    private string NameFor(ModId id) =>
        _byId.TryGetValue(id, out var mod) ? mod.Name : id.Display;

    /// <summary>
    /// Builds the resolver from the CURRENT validation, so it can never offer to fix
    /// something that has already been fixed since the warnings were drawn.
    /// </summary>
    public void OpenDependencyResolver()
    {
        var active = ActiveRows.OfType<ModRowViewModel>().Select(r => r.PackageId).ToHashSet();

        // Every dependency any active mod declares, so a card can show the friendlier
        // name and the Workshop link the requiring mod provided.
        var declared = new Dictionary<ModId, ModDependency>();
        foreach (var mod in ActiveRows.OfType<ModRowViewModel>().Select(r => r.Mod))
        {
            foreach (var dep in mod.Dependencies) declared.TryAdd(dep.PackageId, dep);
        }

        var cards = DependencyResolver.Plan(Warnings, _byId, active, _knownExpansions, declared);
        if (cards.IsEmpty)
        {
            StatusText = "No unmet dependencies.";
            return;
        }

        var vm = new DependencyResolverViewModel(
            cards,
            ActivateDependency,
            DownloadDependencyAsync,
            id => WorkshopLinks.Open(id));

        DependencyResolverRequested?.Invoke(vm);
    }

    /// <summary>Moves an installed-but-inactive dependency into the order; returns its index.</summary>
    private int? ActivateDependency(ModId packageId)
    {
        var row = InactiveRows.OfType<ModRowViewModel>().FirstOrDefault(r => r.PackageId == packageId);
        if (row is null) return null;

        ActivateMods([row]);
        return ActiveRows.OfType<ModRowViewModel>()
            .FirstOrDefault(r => r.PackageId == packageId)?.Index;
    }

    private async Task<bool> DownloadDependencyAsync(string workshopId)
    {
        if (_installPaths is not { } paths) return false;

        try
        {
            var provisioner = new SteamCmdProvisioner(AppPaths.SteamCmdDir);
            var exe = await provisioner.EnsureProvisionedAsync();
            var outcomes = await Task.Run(() => _download.DownloadAndInstallAsync(
                [workshopId], exe, paths.LocalModsDir, AppPaths.SteamCmdDownloadsDir));

            return outcomes.Count > 0 && outcomes[0].Installed;
        }
        catch (Exception ex)
        {
            _log.Warn(LogSubsystem.Steam, $"dependency download failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The data the tag filter runs against, keyed by <see cref="ModId"/> — cached here
    /// because <c>LoadModMetadata</c> reads disk and <c>ApplyFilter</c> runs per
    /// keystroke. Rebuilt in <see cref="RefreshTagFilters"/>, which every
    /// metadata-changing path already calls.
    /// </summary>
    private ImmutableDictionary<ModId, ImmutableHashSet<string>> _tagsByMod =
        ImmutableDictionary<ModId, ImmutableHashSet<string>>.Empty;

    /// <summary>The Favourites pseudo-tag's data, built in the same pass as
    /// <see cref="_tagsByMod"/> from the same metadata records (O14).</summary>
    private ImmutableHashSet<ModId> _favouriteIds = [];

    /// <summary>Rebuilds the Tags ▾ rows after a scan or a tag edit. Ticked tags survive
    /// the rebuild by id — a rescan must not silently lift the user's filter.</summary>
    private void RefreshTagFilters()
    {
        var wasSelected = AllTags.Where(t => t.IsSelected).Select(t => t.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);

        AllTags.Clear();
        _tagsByMod = ImmutableDictionary<ModId, ImmutableHashSet<string>>.Empty;
        _favouriteIds = [];
        _taggedModCount = 0;

        if (_metadata is not null)
        {
            var entries = _metadata.LoadModMetadata().Entries;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var byMod = ImmutableDictionary.CreateBuilder<ModId, ImmutableHashSet<string>>();
            var favourites = ImmutableHashSet.CreateBuilder<ModId>();

            foreach (var (key, meta) in entries)
            {
                // Favourite is read from the SAME pass as the tags — it is the same
                // metadata record, and a second walk over 565 entries to answer a
                // question this loop already has in hand would be pure cost.
                if (meta.Favorite) favourites.Add(ModId.From(key));

                if (meta.TagIds.IsDefaultOrEmpty) continue;
                _taggedModCount++;
                byMod[ModId.From(key)] = [.. meta.TagIds];
                foreach (var id in meta.TagIds)
                    counts[id] = counts.GetValueOrDefault(id) + 1;
            }

            _tagsByMod = byMod.ToImmutable();
            _favouriteIds = favourites.ToImmutable();

            foreach (var tag in _tagSet.Tags)
            {
                // IsSelected before the subscription, so restoring it is not an event.
                var row = new TagFilterViewModel(tag, counts.GetValueOrDefault(tag.Id))
                    { IsSelected = wasSelected.Contains(tag.Id) };
                row.PropertyChanged += OnTagFilterPropertyChanged;
                AllTags.Add(row);
            }
        }

        OnPropertyChanged(nameof(TagFilterLabel));
        OnPropertyChanged(nameof(UntaggedCount));
        OnPropertyChanged(nameof(FavouriteCount));
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(HasTagFilter));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterButtonText));

        // A ticked tag can vanish here — deleted in Settings while filtering on it.
        // That changes what the filter hides, so it must re-run, not linger half-applied.
        var nowSelected = AllTags.Where(t => t.IsSelected).Select(t => t.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (!wasSelected.SetEquals(nowSelected)) ApplyFilter();
    }

    /// <summary>"214 mods · 6 separators" (1a) — the separator count matters because
    /// separators are the user's own structure, not the game's.</summary>
    public string ActiveSummary
    {
        get
        {
            var separators = ActiveRows.OfType<SeparatorRowViewModel>().Count();
            return separators == 0
                ? $"{ActiveCount} mods"
                : $"{ActiveCount} mods · {separators} separators";
        }
    }

    /// <summary>
    /// The active pane's footer line: what is true of this list against <b>the game</b>,
    /// or empty when they agree.
    /// <para>
    /// Renamed from <c>UnsavedChangesText</c> along with its wording. It read
    /// <c>CanUndo ? "◆ unsaved changes" : ""</c> — derived from "have you edited anything
    /// this session", comparing against nothing — and both halves were wrong. Nothing about
    /// the modlist is unsaved: it commits on every edit and has no Save button. What may be
    /// unwritten is the game's <c>ModsConfig.xml</c>, and <see cref="Drift"/> is the
    /// comparison that knows.
    /// </para>
    /// </summary>
    // DriftText (the pane-footer line) is GONE: S-DRIFT moved the indicator to its
    // status-bar zone, where DriftZoneText carries all four states including in-sync.

    // The four chrome hints — Dock ⌘J, Rescan ⌘⇧C, Revalidate ⌘R, Mod Info ⌘3 — went
    // with the labels that carried them (N3 · UI-12). A gesture stapled to a button you
    // are already looking at teaches nothing: you are about to click it. Menus, the
    // palette's key column and the status bar's "Undo ⌘Z" keep theirs, because each
    // says the key at the moment it is worth knowing, and Help ▸ Keyboard shortcuts is
    // the complete list.
}
