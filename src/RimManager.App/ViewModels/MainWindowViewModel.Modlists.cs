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
    // --- S-ORDERDIFF · the order-diff preview (T7) ---------------------------

    /// <summary>Raised for the review dialog; opened modally by the view (confirm
    /// family: one decision, and only "Take theirs" changes anything).</summary>
    public event Action<OrderDiffViewModel>? OrderDiffRequested;

    /// <summary>
    /// The strip's "Review differences…" and the drift zone's ▲: what the game's
    /// order would change about this list, LCS-anchored so an insert at the top is
    /// one insert (<see cref="OrderDiff"/>), judged beside the evidence.
    /// </summary>
    [RelayCommand]
    private void OpenOrderDiff()
    {
        if (_modsConfig is null) return;

        var yours = ModlistStateFromRows().ActiveModIds().ToList();
        var diff = OrderDiff.Between(yours, _modsConfig.ActiveMods);

        OrderDiffRequested?.Invoke(new OrderDiffViewModel(
            diff, id => _byId.TryGetValue(id, out var mod) ? mod.Name : null));
    }

    /// <summary>
    /// Carries out a closed review: "Take theirs" runs the same snapshot-first
    /// replace the strip used to carry; "Keep mine" dismisses the strip — the user
    /// has judged, and the drift zone's ▲ stays as the honest record and the way
    /// back in.
    /// </summary>
    public async Task CompleteOrderDiffAsync(OrderDiffViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (dialog.Accepted)
        {
            await ReplaceListWithGameOrder();
            return;
        }

        _gameMovedDismissed = true;
        OnPropertyChanged(nameof(ShowGameMovedStrip));
    }

    /// <summary>
    /// Recomputes the drift from what is <b>on screen</b>, which is the only source that is
    /// right in all three cases that matter: it survives an undo (the persisted state does
    /// not — undo does not write), it counts every row Apply would write, and it is the
    /// same projection <see cref="ModlistStateFromRows"/> hands to Apply, so the two sides
    /// of the comparison are built the same way.
    /// <para>
    /// Through <see cref="ModlistDrift.Classify"/>, never a bare hash comparison against
    /// <c>LastAppliedHash</c>: <c>Classify</c> checks list-versus-game equality <em>before</em>
    /// it looks at the stored hash, so a list that matches the game reads <c>InSync</c> even
    /// when it has never been applied. The bare comparison would light on a byte-identical
    /// list.
    /// </para>
    /// </summary>
    private void RefreshDrift()
    {
        // No config read yet: there is no evidence about the game, which is what Unknown
        // means. Reporting InSync here would be a guess wearing a verdict's clothes.
        if (_modsConfig is null)
        {
            Drift = DriftKind.Unknown;
            return;
        }

        // The evidence is what RimManager last wrote by ANY list, not what this one last
        // wrote. SelectedModlist is appended because Apply stamps it without touching the
        // collection, so for the moments between an apply and the next reload it is the
        // freshest record of its own row.
        var lists = Modlists.Append(SelectedModlist).OfType<Modlist>().ToList();
        var lastWritten = ModlistDrift.LastWrittenToGame(lists);

        // The zone's in-sync timestamp: the most recent write by ANY list — the same
        // any-list rule as the hash evidence above, for the same reason.
        _lastAppliedAt = lists.Max(l => l.LastAppliedUtc);

        Drift = ModlistDrift.Classify(ModlistStateFromRows(), _modsConfig.ActiveMods, lastWritten);
        OnPropertyChanged(nameof(DriftZoneText));
    }

    // --- N5b · the game-moved strip -----------------------------------------

    /// <summary>What the strip says and offers, decided in <see cref="GameMovedNotice"/>
    /// — the Avalonia-free half, where the tests are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGameMovedStrip))]
    private GameMovedNotice _gameMoved = GameMovedNotice.Hidden;

    private bool _gameMovedDismissed;

    public bool ShowGameMovedStrip => GameMoved.Show && !_gameMovedDismissed;

    partial void OnDriftChanged(DriftKind value)
    {
        // Entering the state lifts any earlier dismissal — this is a new event. Leaving
        // it hides the strip without one: the state resolving IS the dismissal.
        if (value == DriftKind.ChangedOutsideRimManager) _gameMovedDismissed = false;
        RefreshGameMovedNotice();
    }

    private void RefreshGameMovedNotice()
    {
        GameMoved = _modsConfig is null
            ? GameMovedNotice.Hidden
            : GameMovedNotice.Decide(
                Drift,
                _modsConfig.ActiveMods,
                [.. ModlistStateFromRows().ActiveModIds()],
                SelectedModlist?.LastAppliedHash,
                _byId.ContainsKey);

        // Explicit, because the attribute only fires when the RECORD changes — a lifted
        // dismissal with identical words would otherwise never reach the binding.
        OnPropertyChanged(nameof(ShowGameMovedStrip));
    }

    [RelayCommand]
    private void DismissGameMovedStrip()
    {
        _gameMovedDismissed = true;
        OnPropertyChanged(nameof(ShowGameMovedStrip));
    }

    /// <summary>
    /// N5b · <em>Save the game's order as a new modlist</em> — the default offer, because
    /// it destroys nothing: the current list is untouched and the game is already where
    /// it says it is.
    /// </summary>
    [RelayCommand]
    private async Task AdoptGameOrderAsNewModlist()
    {
        if (_modlistRepo is null || _modsConfig is null) return;

        // Refused mid-scan, like the switcher and for its reasons — and one of our own:
        // SwitchModlistAsync would refuse below, leaving the OLD list selected, and the
        // stamp would then credit the wrong list with the game's order.
        if (IsBusy)
        {
            StatusText = "Still reading your mod folders — adopting needs the scan to finish.";
            return;
        }

        // Captured once: the switch below re-reads the config, and the order adopted
        // must be the order named on the strip the user just read.
        var order = _modsConfig.ActiveMods;

        var name = GameMovedNotice.SuggestedName(DateTimeOffset.Now, Modlists.Select(l => l.Name));
        var created = await _modlistRepo.CreateAsync(
            name, ModlistStartup.FromGame(order, _byId));

        await RefreshModlistsAsync();
        await SwitchModlistAsync(created);

        // Belt to the guard's braces: if the switch still declined, the created list is
        // on disk but not open — say so and stop rather than stamping whatever IS open.
        if (SelectedModlist?.Id != created.Id)
        {
            StatusText = $"Created “{name}” from the game's order — open it from the switcher.";
            return;
        }

        // The game already holds this exact order — record the agreement. Without the
        // stamp the first drag in the adopted list would read ChangedOutsideRimManager
        // (list ≠ game ≠ last-written) instead of PendingApply, and the strip would
        // reappear to warn about a write nobody made. An exact match is the one case
        // the CLI rule says MAY be credited.
        await RecordAppliedAsync(order);

        DismissGameMovedStrip();
        StatusText = $"Saved the game's order as “{name}” and opened it — "
            + "your previous list is unchanged.";
    }

    /// <summary>
    /// N5b · <em>Replace this list with the game's order</em>. Snapshots first, named so
    /// the prune cannot evict it: the separators being replaced exist nowhere else.
    /// Called directly by <see cref="CompleteOrderDiffAsync"/> ("Take theirs") since
    /// S-ORDERDIFF moved the verb into the review dialog — no surface binds a command.
    /// </summary>
    private async Task ReplaceListWithGameOrder()
    {
        if (_modlistRepo is null || _modsConfig is null || SelectedModlist is not { } list) return;

        // Mid-scan the panes are half-built: the named snapshot below would capture
        // EMPTY rows as "the arrangement being replaced", which is a lie in History.
        if (IsBusy)
        {
            StatusText = "Still reading your mod folders — adopting needs the scan to finish.";
            return;
        }

        var order = _modsConfig.ActiveMods;

        var label = $"Before adopting · {DateTimeOffset.Now:d MMM HH:mm}";
        var current = list with { State = ModlistStateFromRows() };
        await _modlistRepo.SnapshotAsync(
            current, "before replacing with the game's order", KeepSnapshots, label);

        // The same three steps as any arrangement edit, so undo works on this too.
        LoadActiveRows(ModlistStartup.FromGame(order, _byId));
        CommitChange();
        await RecordAppliedAsync(order);

        RefreshHistory();
        DismissGameMovedStrip();
        StatusText = $"Replaced “{list.Name}” with the game's order ({order.Length} mods) — "
            + $"the previous arrangement is in History as “{label}”.";
    }

    /// <summary>
    /// Loads the modlists, seeding one from the game's current order if none exist, and
    /// selects the one to open.
    /// <para>
    /// <see cref="ModlistRepository.EnsureDefaultAsync"/> holds the "exactly one
    /// undeletable default" invariant here, on every load, rather than trusting whatever
    /// happened at setup.
    /// </para>
    /// </summary>
    private async Task LoadModlistsAsync(IReadOnlyList<ModId> gameActiveOrder)
    {
        await EnsureModlistStoreAsync();
        if (_modlistRepo is not { } repo) return;

        var lists = await repo.EnsureDefaultAsync(
            () => ModlistStartup.FromGame(gameActiveOrder, _byId));

        Modlists.Clear();
        foreach (var list in lists) Modlists.Add(list);

        // Keep the current selection across a refresh; otherwise open the most recently
        // used, then the default.
        SelectedModlist =
            lists.FirstOrDefault(l => l.Id == SelectedModlist?.Id)
            ?? repo.Selected(lists);

        if (SelectedModlist is { } chosen)
        {
            await repo.MarkUsedAsync(chosen);

            // Re-read the stamped copy. MarkUsedAsync writes to DISK, and the objects in
            // Modlists came from the read before it — so without this the list stays in
            // memory with LastUsedUtc still null, and Settings ▸ Modlists shows LAST USED
            // "never" for the list that is open in front of you.
            if (repo.Get(chosen.Id) is { } stamped)
            {
                var index = Modlists.IndexOf(chosen);
                if (index >= 0) Modlists[index] = stamped;
                SelectedModlist = stamped;
            }
        }

        RefreshModlistChoices();
    }

    /// <summary>The switcher's entries. Rebuilt rather than mutated, as the instance
    /// switcher's are, so the tick cannot drift from the selection.</summary>
    public ObservableCollection<ModlistChoiceViewModel> ModlistChoices { get; } = [];

    /// <summary>
    /// The selector's own search (O19), the same shape the Tags ▾ flyout uses. Narrows
    /// which ROWS are listed and nothing else — it never touches the mod lists.
    /// </summary>
    [ObservableProperty] private string _modlistSearch = string.Empty;

    partial void OnModlistSearchChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleModlistChoices));
        OnPropertyChanged(nameof(ModlistSearchFoundNothing));
        OnPropertyChanged(nameof(ModlistSearchEmptyMessage));
    }

    public IReadOnlyList<ModlistChoiceViewModel> VisibleModlistChoices =>
        string.IsNullOrWhiteSpace(ModlistSearch)
            ? ModlistChoices
            : [.. ModlistChoices.Where(c =>
                c.Name.Contains(ModlistSearch.Trim(), StringComparison.OrdinalIgnoreCase))];

    public bool ModlistSearchFoundNothing =>
        VisibleModlistChoices.Count == 0 && ModlistChoices.Count > 0;

    public string ModlistSearchEmptyMessage => $"No modlist matches “{ModlistSearch.Trim()}”.";

    private void RefreshModlistChoices()
    {
        // Per-list drift for the flyout rows (S-SELECTOR): the same Classify + the
        // same LastWrittenToGame evidence the footer uses — never a bare hash. The
        // SELECTED list is judged from the live rows so unapplied edits show, except
        // mid-reload when the rows are empty and would read as a lie.
        var lastWritten = _modsConfig is null
            ? null
            : ModlistDrift.LastWrittenToGame(Modlists.Append(SelectedModlist).OfType<Modlist>());

        ModlistChoices.Clear();
        foreach (var list in Modlists)
        {
            var isCurrent = list.Id == SelectedModlist?.Id;
            var drift = _modsConfig is null
                ? DriftKind.Unknown
                : ModlistDrift.Classify(
                    isCurrent && !IsBusy ? ModlistStateFromRows() : list.State,
                    _modsConfig.ActiveMods, lastWritten);

            ModlistChoices.Add(new ModlistChoiceViewModel(
                list, isCurrent, l => _ = SwitchModlistAsync(l), drift));
        }

        // The searched view is derived, so it has to be told the source changed.
        OnPropertyChanged(nameof(VisibleModlistChoices));
        OnPropertyChanged(nameof(ModlistSearchFoundNothing));
    }

    /// <summary>The name shown on the toolbar selector.</summary>
    public string SelectedModlistName => SelectedModlist?.Name ?? "No modlist";

    // The selector's identity swatch (v2 S-SELECTOR): the modlist's own palette
    // colour, replacing the 6px path-health dot — colour-only at 6px, reporting
    // PATH health from inside a LIST switcher, was unlearnable. Path health lives
    // in the strips and the Paths verdicts now. One bool per hue, the palette-class
    // pattern (never a converter).
    public bool IsModlistPalette0 => (SelectedModlist?.PaletteIndex ?? 0) == 0;
    public bool IsModlistPalette1 => SelectedModlist?.PaletteIndex == 1;
    public bool IsModlistPalette2 => SelectedModlist?.PaletteIndex == 2;
    public bool IsModlistPalette3 => SelectedModlist?.PaletteIndex == 3;
    public bool IsModlistPalette4 => SelectedModlist?.PaletteIndex == 4;
    public bool IsModlistPalette5 => SelectedModlist?.PaletteIndex == 5;

    partial void OnSelectedModlistChanged(Modlist? value)
    {
        OnPropertyChanged(nameof(SelectedModlistName));
        OnPropertyChanged(nameof(CanSwitchModlist));
        OnPropertyChanged(nameof(IsModlistPalette0));
        OnPropertyChanged(nameof(IsModlistPalette1));
        OnPropertyChanged(nameof(IsModlistPalette2));
        OnPropertyChanged(nameof(IsModlistPalette3));
        OnPropertyChanged(nameof(IsModlistPalette4));
        OnPropertyChanged(nameof(IsModlistPalette5));
    }

    /// <summary>Offered disabled rather than hidden, so the route is discoverable on the
    /// install that has one list today and three next month.</summary>
    public bool CanSwitchModlist => Modlists.Count > 1;

    /// <summary>
    /// Switches the open modlist: hands the in-game mod settings over, swaps the
    /// arrangement, and rebuilds the panes.
    /// <para>
    /// The outgoing list is captured BEFORE the incoming one is restored, and captured
    /// from the live Config folder rather than from what it held when it was opened —
    /// otherwise tuning changed during this session is lost at the moment of switching,
    /// which is the one moment the user is most sure they saved it.
    /// </para>
    /// <para>
    /// Settings only move for lists that opted in. A switch that rewrote mod settings
    /// nobody asked it to touch would be the single most alarming thing this app could do.
    /// </para>
    /// </summary>
    private async Task SwitchModlistAsync(Modlist target)
    {
        if (_modlistRepo is null || SelectedModlist?.Id == target.Id) return;

        // Refused during a scan, and said out loud. The toolbar deliberately stays live
        // while the panes show the first-scan state (2k), so this IS reachable — and the
        // switch would half-happen: ReloadAsync would return at its own IsBusy guard and
        // never rebuild the panes, leaving the window showing one list while the selection
        // claimed another, with every later edit saving into the wrong one.
        if (IsBusy)
        {
            StatusText = "Still reading your mod folders — switching lists needs the scan to finish.";
            return;
        }

        // NOT an early return on a missing config folder. Only the settings half needs
        // that path; switching the arrangement does not. Guarding the whole method on it
        // made the switcher silently do nothing on an install where ConfigDir is unset —
        // the dead-control failure, in the one control the whole change exists for.
        var configDir = _installPaths?.ConfigDir;
        var settings = new ModSettingsStore(_workspace.FileSystem);

        // Flush the arrangement first: PersistModlist QUEUES through SerialWriter, so
        // without the drain the outgoing list's write could land after the switch has
        // already re-read it — handing back the arrangement the user just left.
        PersistModlist();
        if (_modlistWriter is { } writer) await writer.DrainAsync();

        // The state goes up HERE, not when the scan starts.
        //
        // Everything between this line and the reload copies Mod_*.xml files in and out of
        // the game's config folder — a few hundred of them on a real install — and it used
        // to happen with the old list's panes still on screen and nothing moving. So a
        // switch read as: a frozen window for a beat, then a flash of "Reading mod folders…"
        // as the (warm, fast) scan went by, then the result. The slowest part of a switch was
        // the part with no feedback at all.
        //
        // Deliberately NOT IsBusy: ReloadAsync guards on that flag and would return at its
        // own gate, leaving the panes showing one list while the selection claimed another.
        IsScanning = true;

        if (configDir is not null && SelectedModlist is { CapturesModSettings: true } outgoing)
        {
            LoadPhase = LoadPhase.SavingModSettings;
            ScanProgress = default;
            var captured = await settings.CaptureAsync(
                outgoing.Id, configDir, progress: LoadProgress());
            _log.Info(LogSubsystem.Io,
                $"Captured {captured.Files} mod settings file(s) for '{outgoing.Name}'");
        }

        SelectedModlist = target;
        await _modlistRepo.MarkUsedAsync(target);

        if (configDir is not null && target.CapturesModSettings)
        {
            LoadPhase = LoadPhase.RestoringModSettings;
            ScanProgress = default;
            var restored = await settings.RestoreAsync(
                target.Id, configDir, progress: LoadProgress());
            _log.Info(LogSubsystem.Io,
                $"Restored {restored} mod settings file(s) for '{target.Name}'");
        }

        // A rescan, because switching does not change what is installed but does change
        // which of it is active — and rebuilding the panes from the new list is exactly
        // what a reload already does. It takes the state over and lowers it at the end.
        if (_installPaths is not null)
        {
            await ReloadAsync();
        }
        else
        {
            // No install, so no reload to hand the state to — and a state nobody lowers is
            // a window that never comes back.
            IsScanning = false;
        }

        // D5 · curly, like every other quoted name on screen. This is the app's
        // most-fired status line, so it set the tone for the ASCII drift.
        StatusText = $"Switched to “{target.Name}”.";
    }

    /// <summary>
    /// Converts the old instances-and-profiles layout, once, and REPORTS what it did.
    /// <para>
    /// A one-way change to persisted data that says nothing is indistinguishable from
    /// data loss, so the summary goes to the status bar and to the activity log — the
    /// latter because the status bar's next message will replace it within seconds and
    /// this is the one line the user may want to read twice.
    /// </para>
    /// </summary>
    /// <summary>Builds the modlist store once. The instance-to-modlist migration that
    /// used to run here retired in N11: this app never shipped, so the legacy tree it
    /// converted existed on exactly one machine, which has long since converted.</summary>
    private Task EnsureModlistStoreAsync()
    {
        _modlistRepo ??= new ModlistRepository(_workspace.FileSystem);
        _modlistWriter ??= new SerialWriter<Modlist>(
            l => _modlistRepo!.SaveAsync(l),
            ex => _log.Warn(LogSubsystem.Io, $"could not save the modlist: {ex.Message}"));

        return Task.CompletedTask;
    }

    // --- IModlistStore (Settings ▸ Modlists) --------------------------------
    //
    // Replaces IInstanceStore. Instances are gone, and after the migration its
    // create/duplicate/delete produced data nothing reads — a control that quietly does
    // nothing is worse than one that is absent.

    IReadOnlyList<Modlist> IModlistStore.All => Modlists;

    string? IModlistStore.CurrentId => SelectedModlist?.Id;

    int IModlistStore.SnapshotCount(Modlist modlist)
    {
        if (_modlistRepo is null) return 0;

        var dir = _modlistRepo.SnapshotDirectory(modlist.Id);
        return _workspace.FileSystem.DirectoryExists(dir)
            ? _workspace.FileSystem.EnumerateEntries(dir).Count(e => !e.IsDirectory)
            : 0;
    }

    int IModlistStore.SettingsFileCount(Modlist modlist) =>
        new ModSettingsStore(_workspace.FileSystem).Stored(modlist.Id).Files;

    async Task IModlistStore.RenameAsync(Modlist modlist, string name)
    {
        await SaveModlistAsync(modlist with { Name = name });
        _log.Info(LogSubsystem.Io, $"Renamed modlist to '{name}'");
    }

    async Task IModlistStore.RecolourAsync(Modlist modlist, int paletteIndex) =>
        await SaveModlistAsync(modlist with { PaletteIndex = paletteIndex });

    async Task<Modlist> IModlistStore.DuplicateAsync(Modlist modlist, string name)
    {
        if (_modlistRepo is null) return modlist;

        var copy = await _modlistRepo.DuplicateAsync(modlist, name);
        await RefreshModlistsAsync();
        _log.Info(LogSubsystem.Io, $"Duplicated modlist '{modlist.Name}' as '{name}'");
        return copy;
    }

    async Task IModlistStore.SetDefaultAsync(Modlist modlist)
    {
        if (_modlistRepo is null) return;

        await _modlistRepo.SetDefaultAsync(modlist.Id);
        await RefreshModlistsAsync();
        _log.Info(LogSubsystem.Io, $"'{modlist.Name}' is now the default modlist");
    }

    async Task IModlistStore.SetCapturesModSettingsAsync(Modlist modlist, bool captures)
    {
        // Turning it OFF discards the capture. Keeping a snapshot nothing will ever
        // restore is disk pretending to be a feature, and it would silently reappear the
        // moment capture was switched back on — handing back settings from months ago.
        if (!captures) new ModSettingsStore(_workspace.FileSystem).Forget(modlist.Id);

        await SaveModlistAsync(modlist with { CapturesModSettings = captures });
    }

    async Task<Modlist> IModlistStore.CreateAsync(string name)
    {
        if (_modlistRepo is null) return default!;

        var created = await _modlistRepo.CreateAsync(name);
        await RefreshModlistsAsync();
        return created;
    }

    async Task IModlistStore.DeleteAsync(Modlist modlist)
    {
        if (_modlistRepo is null) return;

        // Deleting the OPEN list would leave the panes bound to something that no longer
        // exists, so move off it first and let the refresh pick the survivor.
        var wasOpen = SelectedModlist?.Id == modlist.Id;

        if (!_modlistRepo.Delete(modlist.Id)) return;
        new ModSettingsStore(_workspace.FileSystem).Forget(modlist.Id);

        _log.Warn(LogSubsystem.Io,
            $"Deleted modlist '{modlist.Name}' (its snapshots and captured mod settings). "
            + "Mods, saves and the game folder untouched.");

        if (wasOpen) SelectedModlist = null;
        await RefreshModlistsAsync();

        if (SelectedModlist is null)
            await ReloadAsync();
    }

    /// <summary>Persists one list and re-reads the set, so every surface agrees.</summary>
    private async Task SaveModlistAsync(Modlist modlist)
    {
        if (_modlistRepo is null) return;

        // Same second-writer hazard as RecordAppliedAsync. Settings is modal, so a drag
        // cannot currently be in flight while these run — but that is a property of the
        // window, not of this method, and it is not one worth depending on.
        if (_modlistWriter is { } writer) await writer.DrainAsync();

        await _modlistRepo.SaveAsync(modlist);
        if (SelectedModlist?.Id == modlist.Id) SelectedModlist = modlist;
        await RefreshModlistsAsync();
    }

    /// <summary>Re-reads the lists without rescanning the disk — none of these edits
    /// change what is installed.</summary>
    private async Task RefreshModlistsAsync()
    {
        if (_modlistRepo is null) return;

        var lists = await _modlistRepo.EnsureDefaultAsync();
        Modlists.Clear();
        foreach (var list in lists) Modlists.Add(list);

        if (SelectedModlist is { } open)
            SelectedModlist = lists.FirstOrDefault(l => l.Id == open.Id) ?? _modlistRepo.Selected(lists);
        else
            SelectedModlist = _modlistRepo.Selected(lists);

        RefreshModlistChoices();
        OnPropertyChanged(nameof(CanSwitchModlist));
    }
}
