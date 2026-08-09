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
    // --- rwlist Workshop items (NF-10 · S-RWLIST) ----------------------------

    private RwListOfferSeen _rwListSeen = RwListOfferSeen.Empty;

    /// <summary>The one item currently offered on the strip, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRwListOfferStrip), nameof(RwListOfferHeadline))]
    private RwListOffer? _rwListOffer;

    public bool ShowRwListOfferStrip => RwListOffer is not null;

    public string RwListOfferHeadline =>
        RwListOffer is { } offer ? RwListOfferPresenter.StripHeadline(offer) : string.Empty;

    /// <summary>Raised for the S-RWLIST dialog; opened modally by the view (confirm
    /// family: it exists to collect one consent).</summary>
    public event Action<RwListOfferViewModel>? RwListOfferRequested;

    /// <summary>The strip's ×: seen for good — the context menu is the re-offer.</summary>
    [RelayCommand]
    private async Task DismissRwListOffer()
    {
        if (RwListOffer is not { } offer) return;
        await MarkRwListOfferSeenAsync(offer);
    }

    /// <summary>The strip's Import… — opens the dialog for the offered item.</summary>
    [RelayCommand]
    private void OpenRwListOffer()
    {
        if (RwListOffer is { } offer) RequestRwListDialog(offer);
    }

    /// <summary>The row context menu's standing re-offer, seen or not.</summary>
    [RelayCommand]
    private void ContextImportRwList()
    {
        if (_contextSelection is not [{ Mod.IsRwListItem: true } row]) return;
        RequestRwListDialog(new RwListOffer(
            row.PackageId, RwListOfferPresenter.SeenKeyFor(row.Mod), row.Name, row.Mod.RootPath));
    }

    private void RequestRwListDialog(RwListOffer offer)
    {
        var fs = _workspace.FileSystem;
        var path = fs.EnumerateEntries(offer.RootPath)
            .Where(e => !e.IsDirectory)
            .Select(e => e.FullPath)
            .FirstOrDefault(p => p.EndsWith(".rwlist", StringComparison.OrdinalIgnoreCase));

        if (path is null)
        {
            // Scanned as a list item but the payload has gone since — say so, plainly.
            StatusText = "The item's .rwlist file is no longer there.";
            return;
        }

        RwList? list = null;
        string? error = null;
        var checksumValid = true;
        try
        {
            list = RwListImport.Load(fs.ReadAllText(path), out checksumValid);
        }
        catch (Exception ex)
        {
            error = $"The file could not be read as a mod list: {ex.Message}";
            _log.Warn(LogSubsystem.Io, $"Workshop .rwlist payload could not be parsed: {ex}");
        }

        // The mismatch goes INTO the dialog, not to the status bar. It used to be written
        // one line before this modal opened — centred over the window, and the thing the
        // user is actually reading — so the app detected edited or damaged content, said
        // so where it could not be seen, and then asked for consent as if nothing were
        // wrong. The dialog's own doc-comment calls itself "the consent"; this is exactly
        // the fact consent needs.
        if (!checksumValid && list is not null)
        {
            _log.Warn(LogSubsystem.Io,
                $"'{System.IO.Path.GetFileName(path)}' checksum does not match its content");
        }

        RwListOfferRequested?.Invoke(new RwListOfferViewModel(
            offer, System.IO.Path.GetFileName(path), list, error, checksumValid));
    }

    /// <summary>
    /// Carries out an accepted dialog: a NEW modlist from the payload (T7 decision 3 —
    /// the current list is never touched), switched to, with the reconcile counts as
    /// the status line. Every route out of the dialog marks the offer seen.
    /// </summary>
    public async Task CompleteRwListOfferAsync(RwListOfferViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        await MarkRwListOfferSeenAsync(dialog.Offer);
        if (!dialog.Accepted || dialog.List is not { } list || _modlistRepo is null) return;

        if (IsBusy)
        {
            StatusText = "Still reading your mod folders — importing needs the scan to finish.";
            return;
        }

        var name = RwListWorkshopImport.UniqueName(
            list.Name, dialog.FileName, Modlists.Select(l => l.Name));
        var created = await _modlistRepo.CreateAsync(name, RwListWorkshopImport.ToState(list));
        _log.Info(LogSubsystem.Io, $"Imported Workshop list item as modlist '{name}'");

        await RefreshModlistsAsync();
        await SwitchModlistAsync(created);

        var report = ImportReconciler.Reconcile(list, _byId);
        StatusText = SelectedModlist?.Id == created.Id
            ? $"Imported “{name}” — {report.InstalledCount} mods installed · {report.MissingCount} missing."
            : $"Created “{name}” — open it from the switcher.";
    }

    private async Task MarkRwListOfferSeenAsync(RwListOffer offer)
    {
        _rwListSeen = _rwListSeen.MarkSeen(offer.SeenKey);
        if (_state is not null) await _state.SaveRwListOffersAsync(_rwListSeen);

        // The next unseen item, if any, takes the band — sequential, never stacked.
        RwListOffer = RwListOfferPresenter.NextUnseen(
            _byId.Values, _rwListSeen);
    }

    // --- conflicts (N6: the rows and the per-mod window; the 2c tab is gone) -

    /// <summary>
    /// Raised when the user asks for the two-up XML diff (<c>3c</c>). The window is
    /// opened by the view; the view model only decides that there is something to
    /// show, so it stays constructible without a window.
    /// </summary>
    public event Action<XmlDiffViewModel>? XmlDiffRequested;

    /// <summary>Raised for the per-mod conflict window (N6b); opened by the view for
    /// the same constructibility reason as <see cref="XmlDiffRequested"/>.</summary>
    public event Action<ModConflictsViewModel>? ModConflictsRequested;

    /// <summary>Raised to open the full preview image over the info pane's crop (N8);
    /// opened by the view for the same constructibility reason as the others.</summary>
    public event Action<ImageViewerViewModel>? ImageViewerRequested;

    /// <summary>
    /// N8 · the info pane's preview is a 344×120 crop, and on the real install the
    /// crop hides content on nearly every mod — 540 of 546 measured previews are
    /// taller than the band. Clicking it opens the whole picture, reusing the pane's
    /// already-decoded bitmap: no disk read here.
    /// </summary>
    [RelayCommand]
    private void OpenPreviewImage()
    {
        if (SelectedDetail is not { Preview: { } image } d) return;

        ImageViewerRequested?.Invoke(new ImageViewerViewModel(
            d.Name, image,
            System.IO.Path.GetFileName(d.PreviewPath) ?? "Preview.png",
            d.PreviewBytes));
    }

    /// <summary>Raised to open a mod's full description (O3); opened by the view for
    /// the same constructibility reason as the others.</summary>
    public event Action<DescriptionViewerViewModel>? DescriptionViewerRequested;

    /// <summary>
    /// O3 · the info pane clamps the description to four lines, which on a typical
    /// Workshop mod is a fraction of it. This opens the rest — the same stripped text
    /// the pane holds, never a second read of the file.
    /// </summary>
    [RelayCommand]
    private void OpenFullDescription()
    {
        if (SelectedDetail is not { Description: { } text } d) return;

        DescriptionViewerRequested?.Invoke(new DescriptionViewerViewModel(d.Name, text));
    }

    /// <summary>
    /// N6b · the ⚡ badge's click and a double-click on any active row. Built from the
    /// same live-order arithmetic as the badges, so the window and the mark it came
    /// from cannot disagree about who wins. The rebuild callback re-runs this builder
    /// after the window's own "Win this", so its snapshot follows its own action.
    /// </summary>
    [RelayCommand]
    private void OpenModConflicts(ModRowViewModel? row)
    {
        if (row is null) return;

        ModConflictsDetail Build() => ModConflictsPresenter.Build(
            row.Name, row.PackageId, _lastConflicts,
            [.. ActiveRows.OfType<ModRowViewModel>().Select(r => r.PackageId)],
            ModNames(), Conflicts.IsAnalyzing);

        ModConflictsRequested?.Invoke(new ModConflictsViewModel(
            Build(), PositionOf, ModNames(),
            diff => XmlDiffRequested?.Invoke(diff),
            WinConflict,
            Build));
    }

    /// <summary>
    /// The window's "Win this" — the tab's "Make another win", ported when the tab
    /// went (N6c): moves the subject below the row's LIVE winner so it loads last and
    /// takes effect. The write-through <c>2c</c> described — reordering away from the
    /// list, allowed because the user is looking directly at the consequence — through
    /// the same snapshot-and-undo path a drag takes.
    /// </summary>
    private bool WinConflict(ContestRow? row)
    {
        if (row is null || row.Other is not { } winnerId) return false;

        var mover = ActiveRows.OfType<ModRowViewModel>().FirstOrDefault(r => r.PackageId == row.Subject);
        var winner = ActiveRows.OfType<ModRowViewModel>().FirstOrDefault(r => r.PackageId == winnerId);
        if (mover is null || winner is null || ReferenceEquals(mover, winner)) return false;

        var from = ActiveRows.IndexOf(mover);
        var to = ActiveRows.IndexOf(winner);
        if (from < 0 || to < 0 || from > to) return false;

        ActiveRows.Move(from, to);
        ActiveListOps.Renumber(ActiveRows);
        ApplyFilter();
        Validate();
        CommitChange();

        StatusText = $"{mover.Name} now loads after {winner.Name} — it wins {row.Key}.";
        _log.Info(LogSubsystem.Ui, $"Conflict resolved by move: {mover.Name} → wins {row.Key}");
        return true;
    }

    /// <summary>Exports the current active arrangement to a shareable <c>.rwlist</c> (spec §4.7).</summary>
    [RelayCommand]
    private async Task Export()
    {
        if (_metadata is null) { StatusText = "Load an install before exporting."; return; }

        var path = await _fileDialogs.SaveAsync("Export modlist", $"{SelectedModlist?.Name ?? "modlist"}.rwlist", "rwlist");
        if (path is null) return;

        try
        {
            var state = ModlistStateFromRows();
            var metaById = _metadata.LoadModMetadata().Entries.ToDictionary(kv => ModId.From(kv.Key), kv => kv.Value);
            var info = new RwListInfo(SelectedModlist?.Name ?? "Modlist", null, null, _gameMajorMinor, _knownExpansions)
            {
                CreatedUtc = SystemClock.Instance.UtcNow,
            };

            var list = RwListBuilder.Build(state, _byId, metaById,
                _metadata.LoadTags().Tags, _metadata.LoadCategories().Categories, info);
            await File.WriteAllTextAsync(path, RwListExport.ToRwList(list));
            StatusText = $"Exported {list.Mods.Count()} mods to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
            _log.Warn(LogSubsystem.Io, $"Export to .rwlist failed: {ex}");
        }
    }

    /// <summary>
    /// NF-10 slice 3 · Export as a Workshop <b>item folder</b>: the same projection as
    /// Export, wrapped in the mod-folder shape (About/About.xml + the .rwlist) that
    /// RimManager's own scanner recognises as a list item. Uploading stays the user's
    /// act — RimWorld's dev-mode uploader takes the folder from here.
    /// </summary>
    [RelayCommand]
    private async Task ExportWorkshopItem()
    {
        if (_metadata is null) { StatusText = "Load an install before exporting."; return; }

        var parent = await _fileDialogs.PickFolderAsync("Export as Workshop item — choose where the folder goes");
        if (parent is null) return;

        try
        {
            var state = ModlistStateFromRows();
            var metaById = _metadata.LoadModMetadata().Entries.ToDictionary(kv => ModId.From(kv.Key), kv => kv.Value);
            var info = new RwListInfo(SelectedModlist?.Name ?? "Modlist",
                null, null, _gameMajorMinor, _knownExpansions)
            {
                CreatedUtc = SystemClock.Instance.UtcNow,
            };

            var list = RwListBuilder.Build(state, _byId, metaById,
                _metadata.LoadTags().Tags, _metadata.LoadCategories().Categories, info);
            var folder = await WorkshopItemFolder.WriteAsync(
                _workspace.FileSystem, parent, list, RwListExport.ToRwList(list), _gameMajorMinor);

            StatusText = $"Wrote the Workshop item folder — upload it with RimWorld's dev-mode uploader. {folder}";
            _log.Info(LogSubsystem.Io, $"Exported Workshop item folder: {folder}");
            new FolderLauncher().Open(folder);
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
            _log.Warn(LogSubsystem.Io, $"Export as Workshop item folder failed: {ex}");
        }
    }

    /// <summary>
    /// NF-10 slice 4 · Export as a Steam <b>collection</b>: a PRIVATE collection on
    /// the user's own Workshop account, made of the active list's Workshop mods, via
    /// the same short-lived Steamworks child the updater uses. Private is the
    /// contract — nothing becomes public unless the user publishes it on the
    /// collection's own page, which opens for exactly that review.
    /// </summary>
    [RelayCommand]
    private async Task ExportCollection()
    {
        if (Confirm is null || _installPaths is not { } paths) return;

        var ids = ActiveRows.OfType<ModRowViewModel>()
            .Where(r => r.Mod.PublishedFileId is not null)
            .Select(r => r.Mod.PublishedFileId!)
            .Distinct()
            .ToList();
        var skipped = ActiveRows.OfType<ModRowViewModel>()
            .Count(r => r.Mod.PublishedFileId is null && r.Mod.Source is not (ModSource.Core or ModSource.Dlc));

        if (ids.Count == 0)
        {
            StatusText = "No Workshop mods in the load order — a collection can only hold Workshop items.";
            return;
        }
        if (!new SteamClientDetector().IsClientRunning())
        {
            StatusText = "Creating a collection needs the Steam client running and logged in.";
            return;
        }

        var title = SelectedModlist?.Name ?? "RimManager modlist";
        var skippedNote = skipped > 0
            ? $" {skipped} non-Workshop mod{(skipped == 1 ? "" : "s")} can't join and will be left out."
            : "";
        var result = await Confirm(new ConfirmRequest(
            "Create a Steam collection?",
            $"Creates a PRIVATE collection “{title}” with {ids.Count} Workshop mods on "
            + "your Steam account, then opens its page. Nothing becomes public unless "
            + "you publish it there, and deleting it lives there too."
            + skippedNote
            + " Steam may show you as in-game for a few seconds.",
            Verb: "Create collection"));
        if (!result.Confirmed) return;

        using var activity = Activity("creating collection…");
        StatusText = $"Creating the collection “{title}” on Steam…";
        try
        {
            if (Environment.ProcessPath is not { } selfExe)
                throw new InvalidOperationException("Can't locate our own executable for the Steam helper.");

            var exporter = new SteamworksCollectionExporter(
                selfExe, paths.GameDir, SteamWorkshopClient.RimWorldAppId);
            var outcome = await exporter.CreateAsync(title, ids);

            if (outcome.Error is not null)
            {
                StatusText = $"Collection export failed: {outcome.Error}";
                _log.Warn(LogSubsystem.Steam, $"Collection export failed: {outcome.Error}");
                return;
            }

            StatusText = outcome.LegalAgreementPending
                ? $"Created “{title}” ({outcome.AddedNote} added) — Steam wants the Workshop terms accepted before it shows."
                : $"Created the private collection “{title}” ({outcome.AddedNote} added) — review it on the page that just opened.";
            _log.Info(LogSubsystem.Steam,
                $"Collection export: id {outcome.CollectionId}, {outcome.AddedNote} added");
            if (outcome.CollectionId is { } newId) WorkshopLinks.Open(newId.ToString());
        }
        catch (Exception ex)
        {
            StatusText = $"Collection export failed: {ex.Message}";
            _log.Warn(LogSubsystem.Steam, $"Collection export failed: {ex.Message}");
        }
    }

    /// <summary>Imports a <c>.rwlist</c> / ModsConfig.xml, reconciles it, and loads the installed
    /// mods into the active list in list order (missing ones reported).</summary>
    [RelayCommand]
    private async Task Import()
    {
        var path = await _fileDialogs.OpenAsync("Import modlist", "rwlist", "xml");
        if (path is null) return;

        try
        {
            var list = RwListImport.Load(await File.ReadAllTextAsync(path), out var checksumValid);
            var report = ImportReconciler.Reconcile(list, _byId);

            // Rebuild from the full entry list so separators (and the list order) survive;
            // reconciliation above only covers mods, so it's used just for the summary.
            LoadActiveRows(RwListWorkshopImport.ToState(list));
            CommitChange();

            var warn = checksumValid ? "" : "⚠ checksum mismatch — ";
            StatusText = $"{warn}Imported {report.InstalledCount} mods · {report.MissingCount} missing · "
                + $"{report.VersionMismatchCount} version-mismatch.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            _log.Warn(LogSubsystem.Io, $"Import of a mod list failed: {ex}");
        }
    }
}
