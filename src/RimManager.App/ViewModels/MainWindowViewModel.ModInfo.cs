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
    // --- tags (spec §4.3) --------------------------------------------------
    private TagSet _tagSet = TagSet.Empty;
    private ModId? _selectedModId;
    /// <summary>Chips in Mod Info: the tags this mod carries.</summary>
    public ObservableCollection<TagChipViewModel> SelectedModTags { get; } = [];

    /// <summary>Whether the chips row has anything to show, so it can collapse.</summary>
    public bool HasTagChips => SelectedModTags.Count > 0;

    // UnassignedTags/HasUnassignedTags retired with the old assign flyout (O7): the
    // rewritten one lists EVERY tag, because a tri-state cannot show "1 of 3" for a
    // row that is not there. Nothing had referenced them since.

    // --- the assign flyout (O7, O8) ------------------------------------------
    //
    // Every tag, tri-stated against the CURRENT SELECTION rather than against the one
    // mod the info pane is showing. The flyout lives in Mod Info because that is where
    // tagging already was, but the thing it acts on is the selection — the same rule
    // the Edit menu follows (SelectionForEdit: the active pane first, then the
    // inactive one, since a row can only be in one).

    public ObservableCollection<TagAssignRowViewModel> AssignRows { get; } = [];

    /// <summary>Search-narrowed <see cref="AssignRows"/> — what the flyout renders.</summary>
    public ObservableCollection<TagAssignRowViewModel> VisibleAssignRows { get; } = [];

    [ObservableProperty] private string _assignSearch = string.Empty;

    partial void OnAssignSearchChanged(string value) => RefreshVisibleAssignRows();

    /// <summary>"ASSIGN TO 3 MODS" — the count is the safety story; see TagAssign.</summary>
    public string AssignHeading => TagAssign.Heading(TagSelection().Count);

    public bool HasAssignRows => VisibleAssignRows.Count > 0;

    public bool AssignSearchFoundNothing =>
        AssignRows.Count > 0 && VisibleAssignRows.Count == 0;

    /// <summary>Whether any tag exists at all — a different emptiness from "none matched".</summary>
    public bool HasNoTagsAtAll => AssignRows.Count == 0;

    /// <summary>
    /// The mods the assign flyout acts on: the pane selection when there is one,
    /// falling back to the mod the info pane is showing. The fallback matters — a mod
    /// reached by the ⌘K-less routes (a warning's "show me", a conflict row) fills the
    /// pane without the list ever raising a selection.
    /// </summary>
    private IReadOnlyList<ModId> TagSelection()
    {
        var rows = SelectionForEdit();
        if (rows.Count > 0) return [.. rows.Select(r => r.Mod.PackageId)];
        return _selectedModId is { } id ? [id] : [];
    }

    /// <summary>
    /// O14 · the favourite pill's ×. It clears rather than toggles, because the pill
    /// only exists while the mod IS a favourite — a toggle here could only ever turn
    /// it off, and naming it a toggle would invite the reader to expect otherwise.
    /// Setting the property is enough: <c>ModDetailViewModel</c> persists its own.
    /// </summary>
    [RelayCommand]
    private void ClearFavourite()
    {
        if (SelectedDetail is { } detail) detail.Favorite = false;
    }

    /// <summary>Rebuilds the assigned/unassigned tag lists for the currently selected mod.</summary>
    private void RefreshTagsForSelection()
    {
        SelectedModTags.Clear();
        if (_selectedModId is { } id && _metadata is not null)
        {
            var assigned = _metadata.MetadataFor(id).TagIds;
            foreach (var tag in TagResolver.Resolve(_tagSet, assigned))
                SelectedModTags.Add(new TagChipViewModel(tag, assigned: true));
        }

        RefreshAssignRows();

        OnPropertyChanged(nameof(HasTagChips));
    }

    /// <summary>Recomputes every tag's tri-state against the current selection.</summary>
    public void RefreshAssignRows()
    {
        AssignRows.Clear();

        var selection = TagSelection();
        if (_metadata is not null && selection.Count > 0)
        {
            foreach (var tag in _tagSet.Tags)
            {
                var carrying = selection.Count(id => _metadata.MetadataFor(id).TagIds.Contains(tag.Id));
                AssignRows.Add(new TagAssignRowViewModel(tag, carrying, selection.Count));
            }
        }

        RefreshVisibleAssignRows();
        OnPropertyChanged(nameof(AssignHeading));
        OnPropertyChanged(nameof(HasNoTagsAtAll));
        // The footer's verb is gated on the selection carrying something to remove, so
        // it has to be re-asked whenever the selection or the assignments move.
        OnPropertyChanged(nameof(CanRemoveAllTags));
        OnPropertyChanged(nameof(RemoveAllTagsTip));
        RemoveAllTagsCommand.NotifyCanExecuteChanged();
    }

    private void RefreshVisibleAssignRows()
    {
        VisibleAssignRows.Clear();
        foreach (var row in AssignRows)
        {
            if (AssignSearch.Length > 0
                && !row.Name.Contains(AssignSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            VisibleAssignRows.Add(row);
        }

        OnPropertyChanged(nameof(HasAssignRows));
        OnPropertyChanged(nameof(AssignSearchFoundNothing));
    }

    /// <summary>
    /// O8 · applies a tag across the whole selection, in the direction
    /// <see cref="TagAssign.AssignsOnClick"/> gives: a partial row ASSIGNS.
    /// </summary>
    [RelayCommand]
    private async Task AssignTag(string tagId)
    {
        if (_metadata is null) return;

        var selection = TagSelection();
        if (selection.Count == 0) return;

        var row = AssignRows.FirstOrDefault(r => r.Id == tagId);
        var assigning = row is null || TagAssign.AssignsOnClick(row.State);

        // Awaited one at a time: MetadataRepository holds no cache and each save is a
        // read-modify-write of the whole document, so racing them loses writes — the
        // same reason ToggleTag is awaited.
        var changed = 0;
        foreach (var id in selection)
        {
            var meta = _metadata.MetadataFor(id);
            var has = meta.TagIds.Contains(tagId);
            if (has == assigning) continue;

            await _metadata.SetMetadataAsync(id, meta with
            {
                TagIds = assigning ? meta.TagIds.Add(tagId) : meta.TagIds.Remove(tagId),
            });
            changed++;
        }

        if (row is not null && changed > 0)
            StatusText = TagAssign.Result(assigning, changed, row.Name);

        RefreshTagsForSelection();
        RefreshTagFilters();
        ApplyTagStripesToRows();
    }

    /// <summary>
    /// Toggles a tag on the selected mod (assign ↔ unassign) and persists.
    /// <para>
    /// AWAITED, not fire-and-forget. MetadataRepository holds no cache — every read
    /// goes to disk — so refreshing before the write landed re-read the old file and
    /// the chips and the row stripe both showed the state from before the click.
    /// Two rapid toggles could also lose one another, since each save is a
    /// read-modify-write of the whole document.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ToggleTag(string tagId)
    {
        if (_selectedModId is not { } id || _metadata is null) return;

        var meta = _metadata.MetadataFor(id);
        var ids = meta.TagIds;
        var updated = ids.Contains(tagId) ? ids.Remove(tagId) : ids.Add(tagId);

        await _metadata.SetMetadataAsync(id, meta with { TagIds = updated });
        RefreshTagsForSelection();
        RefreshTagFilters();
        ApplyTagStripesToRows();
    }

    // CreateTag and NewTagName are GONE (O22, owner's call): tags are created in
    // Settings ▸ Tags and only ASSIGNED here. What that costs is worth recording —
    // the flyout could name a tag at creation and put it on the selection in one
    // action, where Settings mints "New tag"/"New tag 2" and attaches it to nothing.

    /// <summary>How many of the current selection carry at least one tag.</summary>
    private int TaggedInSelection() =>
        _metadata is null ? 0 : TagSelection().Count(id => !_metadata.MetadataFor(id).TagIds.IsEmpty);

    public bool CanRemoveAllTags => TaggedInSelection() > 0;

    /// <summary>Disabled controls say what they are waiting for (DeadControlTests).</summary>
    public string RemoveAllTagsTip =>
        CanRemoveAllTags
            ? "Take every tag off the selection. Favourites and notes are kept."
            : "Nothing selected carries a tag.";

    /// <summary>
    /// O22 · takes every tag off the selection. Favourites, notes and categories are
    /// untouched — the owner's line was "excluding favorite", and the rest follows for
    /// the same reason: this control is in the TAG flyout and may only spend tags.
    /// <para>
    /// It confirms, always, because nothing can bring them back. Tag assignments are
    /// not in the undo history — <c>UndoHistory</c> is typed on <c>ModlistState</c>,
    /// which carries no metadata — and worse, Ctrl+Z would look like it worked and did
    /// nothing: undo reloads the rows, and that repaints the pills by re-reading the
    /// already-cleared file. The only recovery is a hand-renamed
    /// <c>modmeta.json.*.bak</c>, which is not a thing to ask of anyone.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAllTags))]
    private async Task RemoveAllTags()
    {
        if (_metadata is null || Confirm is null) return;

        var selection = TagSelection();
        var affected = TaggedInSelection();
        if (affected == 0) return;

        var what = selection.Count == 1
            ? (SelectedDetail is { } d ? $"“{d.Name}”" : "this mod")
            : $"{selection.Count} selected mods";

        var result = await Confirm(new ConfirmRequest(
            selection.Count == 1 ? $"Remove every tag from {what}?" : $"Remove every tag from {what}?",
            $"{affected} mod{(affected == 1 ? "" : "s")} will lose {(affected == 1 ? "its" : "their")} tags. "
            + "Favourites, notes and categories are kept, the tags themselves still exist "
            + "in Settings, and no mod folder is touched. This cannot be undone.",
            Verb: "Remove tags"));

        if (!result.Confirmed) return;

        // Awaited one at a time, like AssignTag: MetadataRepository holds no cache and
        // each save rewrites the whole document, so racing them loses writes.
        var cleared = 0;
        foreach (var id in selection)
        {
            var meta = _metadata.MetadataFor(id);
            if (meta.TagIds.IsEmpty) continue;

            await _metadata.SetMetadataAsync(id, meta with { TagIds = [] });
            cleared++;
        }

        StatusText = $"Removed every tag from {cleared} mod{(cleared == 1 ? "" : "s")}.";

        RefreshTagsForSelection();
        RefreshTagFilters();
        ApplyTagStripesToRows();
    }

    // --- detail + warnings (5e) --------------------------------------------

    /// <summary>Populates the detail sidebar for a selected mod.</summary>
    public void SelectMod(Mod mod)
    {
        var meta = _metadata?.MetadataFor(mod.PackageId) ?? ModMetadata.Empty;
        var about = _workspace.ReadAboutXml(mod);
        var preview = _workspace.PreviewPath(mod);

        SelectedDetail = new ModDetailViewModel(mod, about, preview, meta,
            updated => _ = _metadata?.SetMetadataAsync(mod.PackageId, updated),
            loadPosition: PositionInLoadOrder(mod.PackageId),
            positionOf: PositionInLoadOrder);

        _selectedModId = mod.PackageId;
        RefreshTagsForSelection();
        RefreshWarningsForSelection();
        _ = FillInFolderSizeAsync(mod, SelectedDetail);
    }

    /// <summary>
    /// O3 · the mod's size on disk, walked off the UI thread and dropped into the
    /// detail view model when it lands.
    /// <para>
    /// Not awaited by <see cref="SelectMod"/>: selecting a row must stay instant, and
    /// a mod with thousands of texture files takes long enough to feel. The pane shows
    /// an em dash until the number arrives. Writing into <paramref name="detail"/> —
    /// the instance this selection created — is what makes the race harmless: a walk
    /// that finishes after the user has moved on updates a view model nothing is
    /// bound to any more, rather than putting one mod's size against another's name.
    /// </para>
    /// </summary>
    private async Task FillInFolderSizeAsync(Mod mod, ModDetailViewModel? detail)
    {
        if (detail is null) return;

        try
        {
            var bytes = await _workspace.FolderSizeAsync(mod).ConfigureAwait(true);
            if (bytes > 0) detail.SizeText = ByteSize.Format(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The em dash stands. A size is a nicety; failing to get one is not worth
            // a status line, and the log already has the detail.
            _log.Debug(LogSubsystem.Io, $"Could not size {mod.PackageId.Display}: {ex.Message}");
        }
    }

    // --- mod info's warnings section (N2 · UI-5) ----------------------------
    //
    // Taken from the ROW rather than re-derived from the report, so the pane, the row's
    // tooltip and the dock cannot disagree about what is wrong with a mod. Three
    // projections of one fact is exactly how the Activity panel and the log file drifted
    // apart, which cost a whole slice to unpick.

    /// <summary>This mod's warnings, one per line, for the mod-info section.</summary>
    public ObservableCollection<RowWarning> SelectedModWarnings { get; } = [];

    public bool SelectedModHasWarnings => SelectedModWarnings.Count > 0;

    public string SelectedModWarningHeading =>
        RowWarnings.SectionHeading(SelectedModWarnings.Count);

    private void RefreshWarningsForSelection()
    {
        SelectedModWarnings.Clear();

        if (_selectedModId is { } id && RowFor(id) is { } row && !row.Warnings.IsDefaultOrEmpty)
        {
            foreach (var warning in row.Warnings) SelectedModWarnings.Add(warning);
        }

        OnPropertyChanged(nameof(SelectedModHasWarnings));
        OnPropertyChanged(nameof(SelectedModWarningHeading));
    }

    /// <summary>Mod info's "Show in Warnings" — the same jump the row's glyph makes.</summary>
    [RelayCommand]
    private void RevealSelectedModWarnings()
    {
        if (_selectedModId is { } id && RowFor(id) is { } row) RevealWarningsFor(row);
    }

    private ModRowViewModel? RowFor(ModId id) =>
        ActiveRows.OfType<ModRowViewModel>().Concat(InactiveRows.OfType<ModRowViewModel>())
            .FirstOrDefault(r => r.PackageId == id);

    /// <summary>
    /// The mod's 1-based position in the active list, or null when it is not active.
    /// Feeds the facts grid and the dependency rows — a dependency's index is what
    /// tells you whether it actually loads early enough (1a §6).
    /// </summary>
    private int? PositionInLoadOrder(ModId id)
    {
        var row = ActiveRows.OfType<ModRowViewModel>().FirstOrDefault(r => r.PackageId == id);
        return row?.Index;
    }

    /// <summary>Finds a mod by id in either pane and shows its detail (used by warning clicks).</summary>
    public ModRowViewModel? SelectByPackageId(ModId id)
    {
        var row = ActiveRows.Concat(InactiveRows).OfType<ModRowViewModel>()
            .FirstOrDefault(r => r.PackageId == id);
        if (row is not null) SelectMod(row.Mod);
        return row;
    }

    /// <summary>Runs the Tier-1 validators over the active (enabled) list and refreshes the warnings panel.</summary>
    /// <summary>
    /// What triggered the last validation, shown as provenance in the Warnings
    /// toolbar. "Where did these numbers come from" is the question the toolbar
    /// exists to answer.
    /// </summary>
    private string _lastValidationReason = "on scan";

    /// <summary>
    /// A mod's 1-based position in the active load order, or null when it is not
    /// active. Null and 0 stay distinct — "not in the list" is not "loads first".
    /// </summary>
    private int? PositionOf(ModId id)
    {
        for (var i = 0; i < ActiveRows.Count; i++)
            if (ActiveRows[i] is ModRowViewModel row && row.PackageId == id) return i + 1;
        return null;
    }

    /// <summary>Display names for every scanned mod, for the warning columns.</summary>
    private IReadOnlyDictionary<ModId, string> ModNames() =>
        _byId.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
}
