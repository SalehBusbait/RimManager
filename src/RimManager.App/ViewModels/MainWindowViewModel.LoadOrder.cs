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
    // --- inactive pane: sorting and columns (#3, 3f) -------------------------

    /// <summary>
    /// The inactive pane sorts; the active list never does. It is a set rather than a
    /// sequence, so ordering it costs nothing — and finding one mod among 168 costs a
    /// great deal without it (non-negotiable #3 permits sorting here and only here).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InactiveSortText), nameof(NameHeader), nameof(SourceHeader),
        nameof(PackageIdHeader), nameof(AuthorHeader), nameof(VersionHeader))]
    private InactiveSortKey _inactiveSortKey = InactiveSortKey.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InactiveSortText), nameof(NameHeader), nameof(SourceHeader),
        nameof(PackageIdHeader), nameof(AuthorHeader), nameof(VersionHeader))]
    private bool _inactiveSortAscending = true;

    /// <summary>"Name ▲" in the pane header.</summary>
    public string InactiveSortText =>
        $"{InactiveSort.Label(InactiveSortKey)} {(InactiveSortAscending ? "▲" : "▼")}";

    // The five clickable column headings (N1, UI-6.1). Each is one string with the
    // arrow already in it — see InactiveSort.Header for why it is not a label plus a
    // separate arrow element.
    public string NameHeader => Head(InactiveSortKey.Name);
    public string SourceHeader => Head(InactiveSortKey.Source);
    public string PackageIdHeader => Head(InactiveSortKey.PackageId);
    public string AuthorHeader => Head(InactiveSortKey.Author);
    public string VersionHeader => Head(InactiveSortKey.Version);

    private string Head(InactiveSortKey column) =>
        InactiveSort.Header(column, InactiveSortKey, InactiveSortAscending);

    partial void OnInactiveSortKeyChanged(InactiveSortKey value) => ResortInactive();
    partial void OnInactiveSortAscendingChanged(bool value) => ResortInactive();

    private void ResortInactive()
    {
        var sorted = InactiveSort.Apply(InactiveRows, InactiveSortKey, InactiveSortAscending);
        InactiveRows.Clear();
        foreach (var row in sorted) InactiveRows.Add(row);
        ActiveListOps.Renumber(InactiveRows);
    }

    [RelayCommand]
    private void SortInactiveBy(string key)
    {
        if (!Enum.TryParse<InactiveSortKey>(key, out var parsed)) return;

        // Clicking the key you are already sorted by flips the direction, which is
        // what every table in the world does.
        if (parsed == InactiveSortKey) InactiveSortAscending = !InactiveSortAscending;
        else InactiveSortKey = parsed;
    }

    // --- inactive pane columns (N1, UI-6 / UI-7.3 / §0b) ---------------------
    //
    // Columns ▾ is GONE. It configured this pane and nothing else, and §0b settles what
    // replaces it: all columns shown, with the breakpoints still deciding — "all
    // columns" meaning AT FULL WIDTH. What follows is that made continuous.
    //
    // The width that decides is the PANE's, not the window's. The splitter is
    // user-draggable and the pane is persisted at whatever width it is left, so the
    // window's width says nothing about how much room this list has. It is pushed in
    // from the code-behind, the same way WindowWidth is — and unlike WindowWidth it
    // needs no scale division, because the Border it is measured from lives INSIDE the
    // UI-scale transform, so its bounds are already layout units.

    /// <summary>
    /// The inactive pane's own measured width. 298 is the design's figure (`1a`), used
    /// until the first layout pass reports the real one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionColumnWidth), nameof(AuthorColumnWidth),
        nameof(PackageIdColumnWidth))]
    private double _inactivePaneWidth = 298;

    /// <summary>
    /// The arithmetic lives in <see cref="InactiveColumns"/>, not here: this view model
    /// needs ten services and the UI dispatcher to exist, so anything worth testing has
    /// to be outside it — and a column width is a poor thing to check by eye, since one
    /// 8px too narrow still looks like a column.
    /// </summary>
    private InactiveColumnLayout InactiveLayout =>
        InactiveColumns.For(InactivePaneWidth, IsSegmentedLayout, RowChevronWidth.Value);

    // A hidden column collapses to zero width rather than merely blanking its contents:
    // leaving the gap would make "gone" read as "empty".

    /// <summary>The source badge is never dropped: 14px, and it is the row's identity.</summary>
    public Avalonia.Controls.GridLength SourceColumnWidth => Column(true, InactiveColumns.Source);

    public Avalonia.Controls.GridLength VersionColumnWidth => Px(InactiveLayout.Version);
    public Avalonia.Controls.GridLength AuthorColumnWidth => Px(InactiveLayout.Author);
    public Avalonia.Controls.GridLength PackageIdColumnWidth => Px(InactiveLayout.PackageId);

    private static Avalonia.Controls.GridLength Px(double width) =>
        new(width, Avalonia.Controls.GridUnitType.Pixel);

    // The active list's columns are fixed — it IS the load order, not a table (#3) —
    // so these are not a picker. They are the one width the breakpoint takes away, and
    // the chevron it puts back.
    public Avalonia.Controls.GridLength RowPackageIdWidth => Column(!IsSegmentedLayout, 150);
    public Avalonia.Controls.GridLength RowVersionWidth => Column(!IsSegmentedLayout, 52);

    /// <summary>
    /// The `›` that opens the info sheet (<c>2k</c>). Present whenever mod info is an
    /// OVERLAY — so at both breakpoints, not only the segmented one.
    /// <para>
    /// 2k draws it on the segmented layout, but the condition it really answers is
    /// "the pane is not on screen, so something has to open it". Below 1150 that is
    /// already true, and until this existed the only routes back were ⌘3 and the View
    /// menu — which is exactly how the drawer became unreopenable once closed.
    /// Above 1150 the pane is docked and visible, and a chevron would open what is
    /// already open.
    /// </para>
    /// </summary>
    public Avalonia.Controls.GridLength RowChevronWidth => Column(IsInfoOverlay, 14);

    private static Avalonia.Controls.GridLength Column(bool shown, double width) =>
        new(shown ? width : 0, Avalonia.Controls.GridUnitType.Pixel);

    // ResetInactiveColumns went with Columns ▾: there is no configuration left to
    // reset. The pane's width decides, and dragging the splitter is the reset.

    // --- filter / search ---------------------------------------------------
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private ModSource? _sourceFilter;

    // Each announces the collapsed "Filters N ▾" count (2k). Without that the count on
    // the button would be whatever it was when the toolbar was built — the same silent
    // failure as zone 2's tick, in a control whose entire job is to show a number.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFilterCount), nameof(FilterButtonText))]
    private bool _warningsOnly;

    public IReadOnlyList<ModSource?> SourceOptions { get; } =
        [null, ModSource.Workshop, ModSource.Local, ModSource.Core, ModSource.Dlc, ModSource.Git];

    /// <summary>
    /// O16 · which pane the SEARCH BOX narrows. The tag and warning filters are unaffected
    /// — they are about a mod's metadata, which is the same fact whichever list it is in;
    /// the search box is the one control people use to go looking in a particular place.
    /// </summary>
    public enum SearchScope
    {
        Both,
        Active,
        Inactive,
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchScopeLabel), nameof(SearchesActive), nameof(SearchesInactive),
        nameof(SearchesBoth), nameof(SearchesActiveOnly), nameof(SearchesInactiveOnly))]
    private SearchScope _searchIn = SearchScope.Both;

    /// <summary>Whether the ACTIVE pane honours the search term.</summary>
    public bool SearchesActive => SearchIn != SearchScope.Inactive;

    /// <summary>Whether the INACTIVE pane honours the search term.</summary>
    public bool SearchesInactive => SearchIn != SearchScope.Active;

    // The radios DISPLAY the scope; the commands CHOOSE it. One-way on purpose — a
    // TwoWay radio group over derived bools wedges after a switch, which is the shape
    // the Sort flyout's own comment records paying for.
    public bool SearchesBoth => SearchIn == SearchScope.Both;

    public bool SearchesActiveOnly => SearchIn == SearchScope.Active;

    public bool SearchesInactiveOnly => SearchIn == SearchScope.Inactive;

    /// <summary>The scope button's label — it names the SCOPE, not the action.</summary>
    public string SearchScopeLabel => SearchIn switch
    {
        SearchScope.Active => "Active",
        SearchScope.Inactive => "Installed",
        _ => "Both",
    };

    [RelayCommand] private void SearchBoth() => SearchIn = SearchScope.Both;
    [RelayCommand] private void SearchActiveOnly() => SearchIn = SearchScope.Active;
    [RelayCommand] private void SearchInactiveOnly() => SearchIn = SearchScope.Inactive;

    partial void OnSearchInChanged(SearchScope value) => ApplyFilter();

    /// <summary>
    /// Whether anything is narrowing the lists right now. Several rules need it — an
    /// empty group hides only while filtering, activation lands after the last visible
    /// mod only while filtering — and each computing its own answer is how two of them
    /// would come to disagree.
    /// </summary>
    public bool IsFiltering =>
        !string.IsNullOrWhiteSpace(SearchText) || SourceFilter is not null || WarningsOnly
        || AllTags.Any(t => t.IsSelected) || UntaggedOnly || FavouritesOnly;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnUseRegexChanged(bool value) => ApplyFilter();
    partial void OnSourceFilterChanged(ModSource? value) => ApplyFilter();
    partial void OnWarningsOnlyChanged(bool value)
    {
        ApplyFilter();
        QueueLayoutSave();
    }

    private void ApplyFilter()
    {
        // EVERY warning in the dock, of every kind, PLUS any mod the list names that is
        // not installed (UI-7.1).
        //
        // This read the validation report and only each issue's Subject, which made the
        // chip quietly narrower than the tab beside it: a cycle, a duplicate packageId,
        // and the other half of every incompatibility and order rule were counted in
        // the dock and unreachable from the chip that exists to locate them. The panel
        // is where all three sources meet, so the chip reads from there.
        //
        // A missing row is rendered and carries the Broken glyph, but on a 202-mod list
        // that is one row among two hundred and the log only said how many there were.
        // Including them is the difference between reporting a problem and locating it.
        var warnedIds = WarningsPanel.All
            .Where(e => !e.IsGroupHeader)
            .Select(e => e.Owner)
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(ActiveRows.OfType<ModRowViewModel>()
                .Where(r => r.IsMissing)
                .Select(r => r.PackageId))
            .ToImmutableHashSet();

        var criteria = new FilterCriteria
        {
            Search = SearchText,
            UseRegex = UseRegex,
            Source = SourceFilter,
            WarningsOnly = WarningsOnly,
            WarnedIds = warnedIds,
            SelectedTagIds = AllTags.Where(t => t.IsSelected).Select(t => t.Id)
                .ToImmutableHashSet(StringComparer.Ordinal),
            MatchAllTags = MatchAllTags,
            UntaggedOnly = UntaggedOnly,
            FavouritesOnly = FavouritesOnly,
            TagsByMod = _tagsByMod,
            FavouriteIds = _favouriteIds,
        };

        // O16 · the SEARCH TERM is dropped for a pane the scope excludes; every other
        // clause still applies. A tag is a fact about the mod and means the same in both
        // lists, so scoping those would be a different feature — and one nobody asked for.
        var withoutSearch = criteria with { Search = null };

        Filter(ActiveRows, SearchesActive ? criteria : withoutSearch);
        Filter(InactiveRows, SearchesInactive ? criteria : withoutSearch);

        // O9 · then the separators, which can only be judged once the mods under them
        // have been. Active pane only — the inactive list holds no separators.
        ActiveListOps.ApplySeparatorVisibility(ActiveRows, IsFiltering);

        // The empty states depend on what SURVIVED the filter, so they can only be
        // computed here — and reporting "everything is active" when a filter is
        // simply hiding everything is the exact mistake 3e exists to prevent.
        RefreshEmptyStates();
    }

    private static void Filter(IEnumerable<RowViewModel> rows, FilterCriteria criteria)
    {
        foreach (var row in rows)
        {
            row.IsFilteredOut = row is ModRowViewModel mod && !RowFilter.Matches(mod.Mod, criteria);
        }
    }

    /// <summary>Whether a mod is selected, so mod-info chrome can hide rather than lie.</summary>
    public bool HasSelectedDetail => SelectedDetail is not null;

    /// <summary>
    /// The inactive pane's "Activate all". It was rendered disabled with NO reason
    /// given, which is the one thing a disabled control must never be — and the work it
    /// needed already existed.
    /// </summary>
    [RelayCommand]
    private void ActivateAll() =>
        // O9 · "all" means all of what you can SEE. It took every inactive mod regardless
        // of the filter, so narrowing the pane to three mods and pressing the button
        // beneath them activated all 492 — a one-click action with no confirm, whose
        // label named the visible list and whose effect was the whole install.
        ActivateMods([.. InactiveRows.OfType<ModRowViewModel>().Where(r => r.IsRowVisible)]);

    /// <summary>
    /// The active pane's "Collapse all", named in NEXT_PLAN as a stray and disabled
    /// since R3 with nothing saying why. Collapses every separator; expands them all
    /// again when they are already collapsed, because a button that only goes one way
    /// leaves the user hunting for the way back.
    /// </summary>
    [RelayCommand]
    private void CollapseAll()
    {
        var separators = ActiveRows.OfType<SeparatorRowViewModel>().ToList();
        if (separators.Count == 0) return;

        var collapse = separators.Any(s => !s.Collapsed);
        foreach (var separator in separators)
            ActiveListOps.ApplyCollapsed(ActiveRows, separator, collapse);

        StatusText = collapse
            ? $"Collapsed {separators.Count} groups."
            : $"Expanded {separators.Count} groups.";
    }

    /// <summary>Moves mods from the inactive pane into the active load order (bulk activate).</summary>
    public void ActivateMods(IEnumerable<ModRowViewModel> mods)
    {
        // O9 · computed ONCE, before anything moves, then walked forward so a multi-mod
        // activation keeps its order. Recomputing per mod would put each new arrival
        // after the previous one only by luck.
        var at = ActiveListOps.ActivationIndex(ActiveRows, IsFiltering);

        foreach (var mod in mods.ToList())
            if (InactiveRows.Remove(mod)) ActiveRows.Insert(Math.Clamp(at++, 0, ActiveRows.Count), mod);

        AfterListChange();

        // Off by default (#8). On, it is the whole point of the setting: activate a
        // mod and it lands where the rules say it belongs rather than at the bottom.
        if (AutoSortAfterActivate && SortCommand.CanExecute(null)) SortCommand.Execute(null);
    }

    /// <summary>Moves mods out of the active load order into the inactive pane (bulk deactivate).</summary>
    public void DeactivateMods(IEnumerable<ModRowViewModel> mods)
    {
        foreach (var mod in mods.ToList())
            if (ActiveRows.Remove(mod)) InactiveRows.Add(mod);
        AfterListChange();
    }

    private void AfterListChange()
    {
        ActiveListOps.Renumber(ActiveRows);
        ActiveListOps.Renumber(InactiveRows);
        ApplyFilter();
        Validate();
        RefreshCounts();
        CommitChange();
    }

    /// <summary>
    /// Applies each row's ONE tag stripe: the highest-priority tag by manage-list
    /// order (1e §4). The tooltip lists them all, so the stripe is a scanning aid
    /// rather than the sole carrier of meaning.
    /// </summary>
    private void ApplyTagStripesToRows()
    {
        if (_metadata is null) return;

        var entries = _metadata.LoadModMetadata().Entries;

        foreach (var row in ActiveRows.OfType<ModRowViewModel>()
                     .Concat(InactiveRows.OfType<ModRowViewModel>()))
        {
            var tagIds = entries.TryGetValue(row.PackageId.Value, out var meta)
                ? meta.TagIds
                : [];

            // Pills replaced the one-tag stripe (v2 §4A.1): every assigned tag is
            // represented. The preference keeps its stored name (ShowTagStripes)
            // so no settings migrate; its label says pills now.
            row.Pills = ShowTagStripes ? TagResolver.PillsFor(_tagSet, tagIds) : [];

            var all = TagResolver.Resolve(_tagSet, tagIds);
            row.TagTip = all.Length == 0 ? null : string.Join(" · ", all.Select(t => t.Name));
        }
    }

    /// <summary>
    /// Applies each active row's ⚡ badge (N6). Called after every conflict scan AND
    /// after every arrangement change: membership comes from the scan, but the winner
    /// is a fact about the <b>current</b> order — <see cref="RowConflicts"/> recomputes
    /// it so a drag moves the badge without a Cecil pass. Inactive rows never carry
    /// one: an unloaded mod overrides nothing (open question 4).
    /// </summary>
    private void ApplyConflictBadgesToRows()
    {
        var order = ActiveRows.OfType<ModRowViewModel>().Select(r => r.PackageId).ToList();
        var badges = RowConflicts.Compute(_lastConflicts, order);

        foreach (var row in ActiveRows.OfType<ModRowViewModel>())
            row.Conflicts = badges.GetValueOrDefault(row.PackageId);

        // The status bar's ⚡ tone (v2): warn only while override content is
        // actually contested somewhere; an all-harmony install reads neutral.
        HasOverrideConflicts = badges.Values.Any(b => b.HasOverrideConflict);

        // The same recompute moves the selection-relative highlights: a drag can hand
        // the win to the other side while the selection is still held.
        ApplyConflictRelationsToRows();
    }

    /// <summary>
    /// N6's selection-relative highlights, MO2's interaction: exactly one active mod
    /// row selected paints red on the rows whose contested content it discards and
    /// green on the rows that discard its own. Anything else — no selection, several
    /// mod rows, a separator — clears: with more than one subject the two colours
    /// would carry several meanings at once.
    /// </summary>
    private void ApplyConflictRelationsToRows()
    {
        var subjects = _activeSelection.OfType<ModRowViewModel>().Take(2).ToList();
        var relations = subjects is [{ } subject]
            ? RowConflicts.RelationsFor(
                subject.PackageId,
                _lastConflicts,
                ActiveRows.OfType<ModRowViewModel>().Select(r => r.PackageId).ToList())
            : ConflictRelations.None;

        foreach (var row in ActiveRows.OfType<ModRowViewModel>())
        {
            row.IsOverwrittenBySelected = relations.OverwrittenBySelected.Contains(row.PackageId);
            row.OverwritesSelected = relations.OverwritesSelected.Contains(row.PackageId);
            row.SharesHarmonyWithSelected = relations.SharesHarmonyWithSelected.Contains(row.PackageId);
        }
    }

    /// <summary>Any override content contested anywhere — the ⚡ count's warn tone (v2).</summary>
    [ObservableProperty] private bool _hasOverrideConflicts;

    // --- moves --------------------------------------------------------------

    /// <summary>Moves a row on drop. Separators move as single rows (groups are positional).</summary>
    /// <summary>
    /// Records what a drag actually picked up. Multi-row drag has now been "fixed"
    /// twice without being fixed, both times because the guess about which rows were
    /// in flight was never checked against what happened — so it is checkable now.
    /// </summary>
    public void LogDragStarted(int inFlight, int selectedAtPress) =>
        _log.Debug(LogSubsystem.Ui,
            $"Drag started · {inFlight} row(s) in flight · {selectedAtPress} selected at press");

    public void MoveRow(RowViewModel row, string sourcePane, string targetPane, int dropIndex)
    {
        // A separator only lives in the active pane — ignore drops that would send it elsewhere.
        if (row is SeparatorRowViewModel && !(sourcePane == ActivePane && targetPane == ActivePane))
            return;

        // A drag that ends where it started produces NO undo entry, no snapshot and
        // no status line (3a). Checking before the move is what makes that true —
        // afterwards the indices have already shifted. The check runs in the SOURCE
        // LIST's index space, the same space MoveSingle interprets dropIndex in:
        // comparing against row.Index (the displayed number) no-opped every
        // one-position-up drag on a list with separators (see IsSameSpotDrop).
        var wasAt = row.Index;
        var samePane = sourcePane == targetPane;
        var source = sourcePane == ActivePane ? ActiveRows : InactiveRows;
        if (samePane && ActiveListOps.IsSameSpotDrop(source, row, dropIndex)) return;

        MoveSingle(row, sourcePane, targetPane, dropIndex);

        ActiveListOps.Renumber(ActiveRows);
        ActiveListOps.Renumber(InactiveRows);
        RefreshCounts();
        Validate();
        CommitChange();

        MarkJustMoved(row, wasAt, targetPane);
    }

    /// <summary>
    /// Moves a whole selection in one gesture (<c>3a</c>: "one undo entry, one
    /// snapshot, one status line" — for three rows as much as for one).
    /// <para>
    /// The rows keep their relative order and land as a contiguous block at the drop
    /// point, which is what the ghost showed the user was about to happen. Moving them
    /// one at a time through <see cref="MoveRow"/> would write three snapshots and
    /// three status lines, and each move would shift the index the next one aimed at.
    /// </para>
    /// </summary>
    public void MoveRows(
        IReadOnlyList<RowViewModel> rows, string sourcePane, string targetPane, int dropIndex)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;
        if (rows.Count == 1) { MoveRow(rows[0], sourcePane, targetPane, dropIndex); return; }

        var source = sourcePane == ActivePane ? ActiveRows : InactiveRows;

        // In list order, not selection order: the user sees the list, and a selection
        // built by ctrl-clicking upward would otherwise land reversed.
        var moving = rows.Where(source.Contains).OrderBy(source.IndexOf).ToList();
        if (moving.Count == 0) return;

        var separatorsBlocked = targetPane != ActivePane && moving.OfType<SeparatorRowViewModel>().Any();
        if (separatorsBlocked) return;

        var wasAt = moving[0].Index;

        // Everything above the drop point shifts it left as it is removed, so the
        // landing index has to be corrected before anything is inserted.
        var above = moving.Count(r => source.IndexOf(r) < dropIndex);
        var target = targetPane == ActivePane ? ActiveRows : InactiveRows;
        var landing = sourcePane == targetPane ? dropIndex - above : dropIndex;
        landing = Math.Clamp(landing, 0, Math.Max(0, target.Count - (sourcePane == targetPane ? moving.Count : 0)));

        foreach (var row in moving) source.Remove(row);
        for (var i = 0; i < moving.Count; i++)
            target.Insert(Math.Clamp(landing + i, 0, target.Count), moving[i]);

        ActiveListOps.Renumber(ActiveRows);
        ActiveListOps.Renumber(InactiveRows);
        RefreshCounts();
        ApplyFilter();
        Validate();
        CommitChange();

        foreach (var row in moving) { row.PreviousIndex = row.Index; row.IsJustMoved = true; _ = FadeJustMovedAsync(row); }

        StatusText = targetPane == ActivePane
            ? $"Moved {moving.Count} mods to #{moving[0].Index} · Undo {ShortcutFormatter.Format(ShortcutTable.Get(ShortcutTable.Undo), ShortcutGesture.IsMac)}"
            : $"Deactivated {moving.Count} mods · Undo {ShortcutFormatter.Format(ShortcutTable.Get(ShortcutTable.Undo), ShortcutGesture.IsMac)}";

        _log.Info(LogSubsystem.Ui, $"Moved {moving.Count} rows from #{wasAt} to #{moving[0].Index}");
    }

    /// <summary>
    /// The post-drop moment (3a §4): the moved row keeps its selection, holds an
    /// accent tint for 1.2s with its previous index shown inline, then fades. The
    /// status bar carries the record — deliberately NO toast.
    /// </summary>
    private void MarkJustMoved(RowViewModel row, int wasAt, string targetPane)
    {
        row.PreviousIndex = wasAt;
        row.IsJustMoved = true;

        StatusText = targetPane == ActivePane
            ? $"Moved to #{row.Index} · Undo {ShortcutFormatter.Format(ShortcutTable.Get(ShortcutTable.Undo), ShortcutGesture.IsMac)}"
            : $"Deactivated · Undo {ShortcutFormatter.Format(ShortcutTable.Get(ShortcutTable.Undo), ShortcutGesture.IsMac)}";

        _ = FadeJustMovedAsync(row);
    }

    private static async Task FadeJustMovedAsync(RowViewModel row)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(true);
        row.IsJustMoved = false;
        row.PreviousIndex = null;
    }

    private void MoveSingle(RowViewModel row, string sourcePane, string targetPane, int dropIndex)
    {
        var source = sourcePane == ActivePane ? ActiveRows : InactiveRows;
        var target = targetPane == ActivePane ? ActiveRows : InactiveRows;

        int oldIndex = source.IndexOf(row);
        if (oldIndex < 0) return;

        source.RemoveAt(oldIndex);
        if (ReferenceEquals(source, target) && oldIndex < dropIndex) dropIndex--;
        dropIndex = Math.Clamp(dropIndex, 0, target.Count);
        target.Insert(dropIndex, row);
    }

    // --- separator commands (ISeparatorHost + toolbar) ----------------------

    private SeparatorRowViewModel NewSeparator(string name) =>
        new($"sep-{++_separatorSeq}", name, this);

    /// <summary>
    /// Inserts a separator <b>above the selection</b> and opens its name for editing, so
    /// the group is named in the same gesture that creates it.
    /// <para>
    /// It used to insert at index 0 unconditionally and leave the label reading "New
    /// Separator" — so on a 73-mod list the one thing you wanted, a header above the rows
    /// you had just selected, took a create, a scroll, a drag the length of the list and a
    /// rename. Above rather than below is not a coin toss: a separator owns the rows
    /// <em>after</em> it, so above is the only side on which the selection lands inside the
    /// new group.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void AddSeparator()
    {
        var separator = NewSeparator("New Separator");

        ActiveRows.Insert(ActiveListOps.SeparatorInsertIndex(ActiveRows, _activeSelection), separator);
        ActiveListOps.Renumber(ActiveRows);
        CommitChange();

        // After the commit, not before: CommitChange snapshots the arrangement for undo,
        // and the editing flag is view state rather than arrangement — an undo should give
        // back the list without the separator, not a list with a text box open on it.
        //
        // Through the command rather than the flag, so the name it opened with is recorded
        // and Escape has something to restore to.
        separator.BeginRenameCommand.Execute(null);
    }

    /// <summary>
    /// The active pane's selected rows, pushed by the view. Held as rows rather than
    /// indices because the list is rebuilt under them constantly.
    /// <para>
    /// Only the ACTIVE pane's, deliberately: the two lists keep independent selections, and
    /// a separator inserted above something the user picked in the inactive pane would be
    /// inserted above nothing.
    /// </para>
    /// </summary>
    private IReadOnlyList<RowViewModel> _activeSelection = [];

    public void SetActiveSelection(IReadOnlyList<RowViewModel> rows)
    {
        _activeSelection = rows;
        RenameSeparatorCommand.NotifyCanExecuteChanged();
        ApplyConflictRelationsToRows();
        // O8: the assign flyout's heading and tri-states are about the SELECTION, so
        // they have to follow it rather than only the info pane's one mod.
        RefreshAssignRows();
    }

    /// <summary>
    /// F2. The separator in the selection, if there is one.
    /// <para>
    /// It was in <c>ShortcutTable</c> and in the Load-order menu and resolved to <b>no
    /// command</b>, so the key did nothing and the menu row rendered permanently greyed —
    /// for a feature that exists and has worked all along from the row's own ⋮ menu. The
    /// N4a shape exactly, in a surface the audit's markup guard cannot see because this
    /// menu is built from data.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRenameSeparator))]
    private void RenameSeparator()
    {
        if (_activeSelection.OfType<SeparatorRowViewModel>().FirstOrDefault() is { } sep)
            sep.IsEditing = true;
    }

    private bool CanRenameSeparator() => _activeSelection.OfType<SeparatorRowViewModel>().Any();

    /// <summary>Sorts the active list into a valid load order and inserts separators at tier boundaries.</summary>
    [RelayCommand]
    private void AutoLayout()
    {
        var mods = ActiveRows.OfType<ModRowViewModel>().ToList();
        if (mods.Count == 0) return;

        var domain = mods.Select(m => m.Mod).ToList();
        var result = new ModSorter().Sort(
            domain, RuleGraphBuilder.Build(domain, _communityRules), SelectedModlist?.Suppressions);
        var byId = mods.ToDictionary(m => m.PackageId);

        ActiveRows.Clear();
        string? currentGroup = null;
        foreach (var id in result.Order)
        {
            if (!byId.TryGetValue(id, out var row)) continue;
            var group = TierGroupName(result.Tiers.GetValueOrDefault(id, Tier.Normal));
            if (group != currentGroup)
            {
                ActiveRows.Add(NewSeparator(group));
                currentGroup = group;
            }

            ActiveRows.Add(row);
        }

        ActiveListOps.Renumber(ActiveRows);
        ApplyFilter();
        Validate();
        RefreshCounts();
        CommitChange();
        StatusText = "Sorted into tier groups.";
    }

    // One mapping, shared with the first-run wizard, which is what its own doc always
    // claimed. There were two copies and they had already drifted — this one still said
    // "Pre-core" after the other became "Load before Core" — so the separator a user got
    // from Sort into tier groups could be named differently from the one the wizard
    // proposed for the same tier.
    private static string TierGroupName(Tier tier) => FirstRunPresenter.TierGroupName(tier);

    /// <summary>
    /// Collapse persists but is <b>not</b> an undo entry. It changes what you can see, not
    /// what the game will load — and a stack filled with collapses pushes the edits you
    /// might actually want back out of reach. It was persisted by neither route before:
    /// the flag reached <c>ModlistStateFromRows</c> but nothing wrote, so a collapsed group
    /// came back open unless some later edit happened to commit for it.
    /// </summary>
    public void ToggleCollapse(SeparatorRowViewModel separator)
    {
        ActiveListOps.ApplyCollapsed(ActiveRows, separator, !separator.Collapsed);
        PersistModlist();
    }

    /// <summary>
    /// A renamed or recoloured separator is an edit to the list, so it commits like one —
    /// persisted and undoable. Neither used to be either.
    /// </summary>
    public void SeparatorEdited(SeparatorRowViewModel separator) => CommitChange();

    public void DeleteSeparator(SeparatorRowViewModel separator)
    {
        ActiveRows.Remove(separator);
        ActiveListOps.Renumber(ActiveRows);
        RefreshCounts();
        CommitChange();
    }

    // "Deactivate group" retired with its menu row (O12, owner's call). ActiveListOps
    // .GroupMods stays — collapse and the drag contract both need the span.

    // --- misc ---------------------------------------------------------------

    private void RefreshCounts()
    {
        ActiveCount = ActiveRows.OfType<ModRowViewModel>().Count();
        InactiveCount = InactiveRows.Count;

        // ActiveSummary counts separators too, and adding one does not change
        // ActiveCount — so it needs its own notification or the header stalls at
        // "203 mods" while two separators sit in plain view.
        OnPropertyChanged(nameof(ActiveSummary));
        RefreshEmptyStates();

        StatusText = $"{ActiveCount} active · {InstalledCount} installed · {SelectedModlist?.Name}";
    }

    // --- undo/redo, sort, apply (5f) ---------------------------------------



    /// <summary>
    /// Rebuilds the active pane from a modlist arrangement — History's restore.
    /// <para>
    /// A <see cref="ModlistEntry"/> carries its own source and Workshop id, so a mod the
    /// snapshot names but the disk lacks is rebuilt with its identity intact — the
    /// property that let the ProfileState twin of this method retire in N11.
    /// </para>
    /// </summary>
    private void LoadActiveRows(ModlistState state)
    {
        ActiveRows.Clear();
        foreach (var entry in state.Entries)
        {
            if (entry.Kind == ModlistEntryKind.Separator)
            {
                ActiveRows.Add(new SeparatorRowViewModel(
                    entry.Id, entry.DisplayName, this, entry.PaletteIndex ?? 0, entry.Collapsed));
                continue;
            }

            ActiveRows.Add(_byId.TryGetValue(ModId.From(entry.Id), out var mod)
                ? new ModRowViewModel(mod)
                : ModRowViewModel.Missing(entry));
        }

        foreach (var sep in ActiveRows.OfType<SeparatorRowViewModel>().Where(s => s.Collapsed).ToList())
            ActiveListOps.ApplyCollapsed(ActiveRows, sep, true);

        var named = state.AllModIds().ToHashSet();
        InactiveRows.Clear();

        var inactive = _byId.Values
            .Where(m => !named.Contains(m.PackageId))
            .Select(m => new ModRowViewModel(m));
        foreach (var row in InactiveSort.Apply(inactive, InactiveSortKey, InactiveSortAscending))
            InactiveRows.Add(row);

        ActiveListOps.Renumber(ActiveRows);
        ActiveListOps.Renumber(InactiveRows);
        ApplyFilter();
        Validate();
        RefreshCounts();
        ApplyConflictBadgesToRows();
        // Rebuilt rows are NEW objects, and pills live on the row object — the badge
        // reapply on the line above exists for exactly this reason, and pills need it
        // for exactly the same one (missing here is how undo used to strip them).
        ApplyTagStripesToRows();
    }

    private void CommitChange()
    {
        _undo?.Push(ModlistStateFromRows());
        UpdateUndoState();
        PersistModlist();
        RefreshDrift();

        // The ⚡ badges are order-derived (N6): the same edit that moved the drift
        // verdict may have changed who loads last in a contested chain.
        ApplyConflictBadgesToRows();
    }

    /// <summary>
    /// Writes the arrangement back to the selected modlist. The single chokepoint every
    /// edit already passes through, so a new operation cannot forget to persist — which
    /// is how separators came to vanish on every restart in the first place.
    /// <para>
    /// Through <see cref="SerialWriter{T}"/>: drags fire this repeatedly, and two
    /// unawaited saves racing on one file is the bug that lost 3 of 5 preference writes
    /// and later crashed on <c>File.Replace</c>.
    /// </para>
    /// </summary>
    private void PersistModlist()
    {
        if (SelectedModlist is not { } list) return;

        // NEVER while a reload is in flight. ReloadAsync clears both panes before it
        // scans, so ActiveRows is empty for the duration — and persisting then would
        // write an EMPTY arrangement over a real one and destroy the user's load order.
        // The panes only represent a modlist once the reload has refilled them.
        if (IsBusy) return;

        var updated = list with { State = ModlistStateFromRows() };
        SelectedModlist = updated;
        _modlistWriter?.Queue(updated);
    }

    /// <summary>
    /// The active pane as a <see cref="ModlistState"/>, capturing each mod's identity so
    /// the list can still describe it after it is uninstalled.
    /// </summary>
    private ModlistState ModlistStateFromRows() =>
        ModlistState.Empty.WithEntries(ActiveRows.Select<RowViewModel, ModlistEntry>(r => r switch
        {
            // A missing row hands back the entry it came from. Rebuilding it from the
            // packageId alone would drop the source and Workshop id — the identity that
            // makes an uninstalled mod findable — and a single save would erase it.
            ModRowViewModel { MissingEntry: { } missing } => missing,

            ModRowViewModel m => _byId.TryGetValue(m.PackageId, out var mod)
                ? ModlistEntry.Mod(mod)
                : ModlistEntry.Mod(m.PackageId),
            SeparatorRowViewModel s =>
                ModlistEntry.Separator(s.Id, s.Name, s.PaletteIndex, s.Collapsed),
            _ => ModlistEntry.Separator("?", "?"),
        }));

    private void UpdateUndoState()
    {
        CanUndo = _undo?.CanUndo ?? false;
        CanRedo = _undo?.CanRedo ?? false;
    }

    /// <summary>
    /// Undo and redo <b>persist</b>, and that is a fix rather than housekeeping. They went
    /// through <see cref="LoadActiveRows"/> and stopped, bypassing <see cref="CommitChange"/>
    /// — whose own doc calls itself "the single chokepoint every edit already passes
    /// through", which these two demonstrably did not. The arrangement on disk therefore
    /// still held the pre-undo order, so a restart handed back work the user had undone.
    /// <para>
    /// It is also what makes the drift indicator honest rather than accidentally right:
    /// undoing back to the applied order now reads <c>InSync</c> <em>and</em> stays that way
    /// across a restart. Without the write, the footer and the file would disagree the
    /// moment the app reopened.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Undo()
    {
        if (_undo?.CanUndo != true) return;
        LoadActiveRows(_undo.Undo());
        UpdateUndoState();
        PersistModlist();
        RefreshDrift();
    }

    /// <inheritdoc cref="Undo"/>
    [RelayCommand]
    private void Redo()
    {
        if (_undo?.CanRedo != true) return;
        LoadActiveRows(_undo.Redo());
        UpdateUndoState();
        PersistModlist();
        RefreshDrift();
    }

    /// <summary>
    /// "Snapshot before every sort" (UI audit — the pref was written and read by
    /// NOTHING, so the advertised safety net did not exist in either position).
    /// Same shape as the adopt path's snapshot: the outgoing arrangement, labelled,
    /// prunable, visible in History immediately.
    /// </summary>
    private async Task SnapshotBeforeSortAsync()
    {
        if (!SnapshotBeforeSorting || _modlistRepo is null || SelectedModlist is not { } list)
            return;

        var current = list with { State = ModlistStateFromRows() };
        await _modlistRepo.SnapshotAsync(
            current, "before sort", KeepSnapshots, $"Before sort · {DateTimeOffset.Now:d MMM HH:mm}");
        RefreshHistory();
    }

    /// <summary>
    /// Tools ▸ Sort with… ▸ Alphabetical, and the Settings radio's second mode —
    /// which wrote a preference NOTHING read until the UI audit caught it:
    /// AlphabeticalSorter existed in Core, tested, invoked by nobody. Separators
    /// never move; mods sort by name within each separator-owned run (R1b).
    /// </summary>
    [RelayCommand]
    private void SortAlphabetical()
    {
        if (ActiveRows.Count == 0) return;

        var names = ActiveRows.OfType<ModRowViewModel>()
            .ToDictionary(r => r.PackageId, r => r.Name);
        LoadActiveRows(AlphabeticalSorter.SortWithinSeparators(ModlistStateFromRows(), names));
        CommitChange();
        StatusText = "Sorted alphabetically within separators.";
        _log.Info(LogSubsystem.Sort, "Alphabetical sort within separators");
    }

    /// <summary>Reorders the active mods through the sorting engine (drops separators; undoable).</summary>
    [RelayCommand]
    private async Task Sort()
    {
        // The Settings ▸ Sorting radio, honoured at last (UI audit): with the
        // alphabetical mode chosen, Sort IS the alphabetical sort — the preference
        // used to change nothing.
        if (!UseTopologicalSort)
        {
            await SnapshotBeforeSortAsync();
            SortAlphabetical();
            return;
        }

        await SortTopologicallyAsync();
    }

    /// <summary>
    /// Tools ▸ Sort with… ▸ Topological (rules). Forces the rules path whatever the
    /// stored preference says, mirroring <see cref="SortAlphabetical"/>, which forces
    /// its own.
    /// <para>
    /// That row used to run <c>SortCommand</c>, which consults the preference — so with
    /// alphabetical chosen, a row labelled "Topological (rules)" sorted alphabetically
    /// and the status bar said so. A submenu whose entire purpose is picking a mode had
    /// one entry that ignored the pick.
    /// </para>
    /// <para>
    /// It deliberately does NOT write the preference. <c>ChooseTopologicalSort</c> is
    /// what does that, and invoking one row of a "Sort with…" menu must not silently
    /// re-point the toolbar's Sort button and the Settings ▸ Sorting radio — that would
    /// be a quieter version of the same defect. SortAlphabetical sets nothing either.
    /// </para>
    /// </summary>
    [RelayCommand]
    private Task SortTopological() => SortTopologicallyAsync();

    private async Task SortTopologicallyAsync()
    {
        var mods = ActiveRows.OfType<ModRowViewModel>().ToList();
        if (mods.Count == 0) return;

        await SnapshotBeforeSortAsync();

        var domainMods = mods.Select(m => m.Mod).ToList();
        var rules = RuleGraphBuilder.Build(domainMods, _communityRules);
        // The modlist's pinned edges (R1b / "Accept dropped edge") ride every sort:
        // without them a pinned cycle re-decides from scratch, which is the exact
        // behaviour pinning exists to end.
        var result = new ModSorter().Sort(domainMods, rules, SelectedModlist?.Suppressions);

        var byId = mods.ToDictionary(m => m.PackageId);
        ActiveRows.Clear();
        foreach (var id in result.Order)
            if (byId.TryGetValue(id, out var row)) ActiveRows.Add(row);

        ActiveListOps.Renumber(ActiveRows);
        ApplyFilter();
        // Record the sort BEFORE validating: Validate() rebuilds the Warnings panel,
        // and the Cycles group is built from these broken edges.
        WarningsPanel.RecordSort(result);
        _lastValidationReason = "after Sort";
        Validate();
        RefreshCounts();
        CommitChange();
        Cycles.Populate(result);
        if (result.HasCycles && OpenDockOnCycleBreak)
        {
            // 2a: after a sort that broke a cycle, open on Warnings with the first
            // cycle selected — the panel explains what was dropped and why.
            RevealDock(DockWarnings);
            WarningsPanel.SelectFirstCycle();
        }
        // Cycles live in Warnings now (#7), not a tab of their own.
        StatusText = result.HasCycles
            ? $"Sorted — {Plural.Of(result.Cycles.Length, "cycle")} broken (see Warnings)."
            : "Sorted.";
        _log.Info(LogSubsystem.Sort,
            $"Sort complete · {result.Order.Length} mods"
            + (result.HasCycles ? $" · {result.Cycles.Length} cycle(s) broken" : string.Empty));
    }

    /// <summary>Writes the active order to the game's ModsConfig.xml and snapshots it (spec §4.2 auto-snapshot).</summary>
    [RelayCommand]
    private async Task Apply()
    {
        if (_installPaths is not { } paths || _modsConfig is null)
        {
            StatusText = "Nothing to apply (no config loaded).";
            return;
        }

        // Missing mods are written too. The list says they are active, so omitting them
        // would make Apply produce something other than what the pane shows — and would
        // then read as drift on the very next load. RimWorld's own "missing mods" notice
        // is the honest place for that to surface.
        // Fresh, whichever route got here — ⌘S goes through RequestApply, but the commit
        // bar's Write and Apply-and-launch can arrive minutes later.
        RefreshGameOrder();

        // The one accident that loses data the user cannot reconstruct: RimWorld wrote an
        // order, and this replaces it. Recorded BEFORE the write, as a named snapshot the
        // prune cannot evict, so the recovery is History — append-only, already built, and
        // already understood — rather than a .bak file with no UI that nobody can find.
        await SnapshotGameOrderIfChangedElsewhereAsync();

        var rows = ActiveRows.OfType<ModRowViewModel>().ToList();
        var activeIds = rows.Select(r => r.PackageId).ToList();
        var newConfig = ApplyService.WithActiveOrder(_modsConfig, activeIds);

        // The ONLY significant operation in the hub that had no catch, and the one that
        // writes into the user's game folder. ApplyService returns a non-written result
        // for the game-running case alone; every other failure — a read-only
        // ModsConfig.xml, a denied ACL, a OneDrive or antivirus lock, a full disk —
        // throws out of AtomicWriteAsync. Both routes here discarded it: the commit
        // bar's Write awaits an AsyncRelayCommand whose exception reaches an unhandled
        // UI-thread handler this app does not have, and RequestApply's fast path fires
        // `_ = ApplyCommand.ExecuteAsync(null)`, where it is simply never observed. The
        // user got no message, nothing was logged, and the commit bar had already gone.
        try
        {
            var result = await _workspace.ApplyAsync(paths, newConfig);
            StatusText = result.Message;

            if (result.Written)
            {
                _modsConfig = newConfig;

                // The evidence drift detection runs on. Without it every later comparison is
                // Unknown — the app could see that the game and the list disagreed but never
                // say which one moved, which is the whole question.
                await RecordAppliedAsync(activeIds);

                if (rows.Count(r => r.IsMissing) is > 0 and var absent)
                {
                    _log.Warn(LogSubsystem.Scan,
                        $"Applied {absent} mod(s) that are not installed; RimWorld will report them.");
                }

                await SnapshotCurrentAsync("apply");

                // Only after a SUCCESSFUL write: launching on a failed apply would start
                // RimWorld on the old load order while the window showed the new one.
                if (_launchAfterWrite) LaunchGameCommand.Execute(null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Named in the same voice as the other catches: what failed, and the move
            // that fixes it. The active order is still on screen and unchanged, so the
            // toolbar's Apply IS the retry — the commit bar is deliberately not
            // re-raised, because that bar asks a question ("write this?") and reusing it
            // as an error banner would be a second meaning on one control.
            StatusText = $"Could not write ModsConfig.xml — {ex.Message} "
                + "Close RimWorld and anything holding the file, then Apply again.";
            _log.Error(LogSubsystem.Io, $"Apply failed writing ModsConfig.xml: {ex}");
        }
        finally
        {
            // In the finally, not after the if: a throw skipped this, so a failed
            // apply-and-launch left the flag armed and the NEXT ordinary apply would
            // have launched the game on its own.
            _launchAfterWrite = false;
        }
    }

    /// <summary>
    /// Stamps the order just written to the game onto the modlist. This is what lets a
    /// later load tell "I have unsaved edits" from "RimWorld rewrote ModsConfig.xml behind
    /// me" — two states that look identical from the file and need opposite responses.
    /// <para>
    /// Written directly rather than queued: it must be on disk before the next load reads
    /// it, and unlike an arrangement edit it happens once per apply, not once per drag.
    /// </para>
    /// </summary>
    private async Task RecordAppliedAsync(IEnumerable<ModId> appliedOrder)
    {
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        // Drain the arrangement writer FIRST. It is a second writer of this same file, and
        // an edit queued just before Apply would otherwise land after this one and
        // overwrite the hash with the null it was captured with — leaving drift detection
        // permanently unable to say which side moved, which is the whole question it
        // exists to answer.
        if (_modlistWriter is { } writer) await writer.DrainAsync();

        var stamped = list with
        {
            LastAppliedHash = ModlistDrift.HashOrder(appliedOrder),
            LastAppliedUtc = DateTimeOffset.UtcNow,
        };

        // Assigned BEFORE SnapshotCurrentAsync runs, and the order matters: that method
        // re-saves SelectedModlist, so it has to be reading the stamped record. Swap the
        // two calls in Apply and the hash is erased on every write.
        SelectedModlist = stamped;

        // Through the property, so the footer moves. As a bare field write this line ran
        // on every apply and changed nothing anybody could see — no reader ran between it
        // and the next full reload.
        Drift = DriftKind.InSync;
        await _modlistRepo.SaveAsync(stamped);
    }

    /// <summary>
    /// Captures the game's own order as a snapshot of this modlist, when the game holds
    /// something RimManager did not write.
    /// <para>
    /// <b>Named</b>, so the rolling prune is forbidden to evict it: this is the one snapshot
    /// that must still be there a month later, because by definition nothing else recorded
    /// what the file held. <c>SnapshotAsync</c> writes only the snapshot file, so the open
    /// modlist is untouched — the user gets a restorable copy without their list changing
    /// under them.
    /// </para>
    /// <para>
    /// The arrangement is flat: <c>ModsConfig.xml</c> has no separators to carry, which is
    /// the whole reason modlists exist. Restoring it gives back the order and not the
    /// organisation, and that is the honest limit of what the game's file ever held.
    /// </para>
    /// </summary>
    private async Task SnapshotGameOrderIfChangedElsewhereAsync()
    {
        if (Drift != DriftKind.ChangedOutsideRimManager) return;
        if (_modlistRepo is null || _modsConfig is null || SelectedModlist is not { } list) return;

        var label = $"RimWorld · {DateTimeOffset.Now:d MMM HH:mm}";
        var captured = list with { State = ModlistStartup.FromGame(_modsConfig.ActiveMods, _byId) };

        await _modlistRepo.SnapshotAsync(
            captured, "the game's order, before apply", KeepSnapshots, label);

        RefreshHistory();
        _log.Warn(LogSubsystem.Io,
            $"The game's mod list had changed outside RimManager; kept it as '{label}' in History "
            + $"({_modsConfig.ActiveMods.Length} mods) before applying.");
    }

    private async Task SnapshotCurrentAsync(string reason)
    {
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        // The arrangement on screen, captured against the MODLIST. This used to write a
        // profile called "Current" into instances/<id>/, so History described the
        // instance while the rest of the window described a list — and switching lists
        // did not change it. It also silently re-created the legacy tree after a
        // migration that had already run and would never run again.
        var snapshot = list with { State = ModlistStateFromRows() };
        await _modlistRepo.SaveAsync(snapshot);
        SelectedModlist = snapshot;

        await _modlistRepo.SnapshotAsync(snapshot, reason, KeepSnapshots);
        RefreshHistory();
    }

    /// <summary>Reloads the selected modlist's history.</summary>
    private void RefreshHistory()
    {
        if (_modlistRepo is null || SelectedModlist is not { } list) return;

        History.Populate(
            _modlistRepo.ListSnapshots(list.Id),
            _modlistRepo.SnapshotSizes(list.Id),
            ModNames(),
            DateTimeOffset.UtcNow,
            list.Name,
            GameVersion ?? "unknown",
            RulesStatus);

        OnPropertyChanged(nameof(SnapshotCount));
    }

    // --- row context menu (2i-8) --------------------------------------------

    /// <summary>What the context menu can offer for the current selection.</summary>
    [ObservableProperty] private RowContextState _rowContext =
        new(0, string.Empty, false, false, false, false, false, null, []);

    private IReadOnlyList<ModRowViewModel> _contextSelection = [];

    /// <summary>Called by the view when a row is right-clicked, before the menu opens.</summary>
    public void PrepareRowContext(IReadOnlyList<ModRowViewModel> selected, bool fromActivePane)
    {
        _contextSelection = selected;
        RowContext = RowContextMenu.For(selected, fromActivePane);
    }

    [RelayCommand]
    private void ContextDeactivate() => DeactivateMods(_contextSelection);

    [RelayCommand]
    private void ContextActivate() => ActivateMods(_contextSelection);

    [RelayCommand]
    private void ContextOpenFolder()
    {
        if (_contextSelection.Count != 1) return;

        var error = new FolderLauncher().Open(_contextSelection[0].Mod.RootPath);
        if (error is not null) StatusText = $"Could not open the folder: {error}";
    }

    [RelayCommand]
    private void ContextOpenWorkshop()
    {
        if (RowContext.WorkshopId is not { } id) return;
        if (WorkshopLinks.Open(id) is null) StatusText = "Could not open the Workshop page.";
    }

    /// <summary>
    /// Copies every selected packageId, one per line — the form you paste into a bug
    /// report or another manager, and the reason it is the identity that gets copied
    /// rather than the display name.
    /// </summary>
    [RelayCommand]
    private Task ContextCopyPackageId() => CopyPackageIdsAsync(_contextSelection);

    /// <summary>One body for the context menu and the Edit menu (Ctrl+C).</summary>
    private async Task CopyPackageIdsAsync(IReadOnlyList<ModRowViewModel> rows)
    {
        if (rows.Count == 0) return;

        var text = string.Join(Environment.NewLine, rows.Select(r => r.PackageId.Display));

        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow.Clipboard: { } clipboard })
        {
            var transfer = new Avalonia.Input.DataTransfer();
            transfer.Add(Avalonia.Input.DataTransferItem.Create(Avalonia.Input.DataFormat.Text, text));
            await clipboard.SetDataAsync(transfer);
            StatusText = rows.Count == 1
                ? $"Copied {rows[0].PackageId.Display}."
                : $"Copied {rows.Count} packageIds.";
        }
    }

    /// <summary>Toggles the favourite flag on every selected mod.</summary>
    [RelayCommand]
    private Task ContextToggleFavourite() => ToggleFavouriteAsync(_contextSelection);

    /// <summary>One body for the context menu and the Edit menu (Ctrl+D).</summary>
    private async Task ToggleFavouriteAsync(IReadOnlyList<ModRowViewModel> rows)
    {
        if (_metadata is null || rows.Count == 0) return;

        // Decided from the FIRST row and applied to all: a per-row toggle on a mixed
        // selection leaves you unable to predict what the click did.
        var turningOn = !_metadata.MetadataFor(rows[0].PackageId).Favorite;

        foreach (var row in rows)
        {
            var meta = _metadata.MetadataFor(row.PackageId);
            await _metadata.SetMetadataAsync(row.PackageId, meta with { Favorite = turningOn });
        }

        StatusText = turningOn
            // Single-row by construction most of the time: ⌘D on one mod printed
            // "Favourited 1 mod(s)."
            ? $"Favourited {Plural.Of(rows.Count, "mod")}."
            : $"Unfavourited {Plural.Of(rows.Count, "mod")}.";
    }

    /// <summary>
    /// Deletes the selected mods' folders. The one action in this menu that touches the
    /// user's actual mods rather than RimManager's records, so it confirms through 2i-6
    /// and the wording says exactly that.
    /// </summary>
    [RelayCommand]
    private async Task ContextDeleteFromDisk()
    {
        if (Confirm is null || _contextSelection.Count == 0 || !RowContext.CanDeleteFromDisk) return;

        var selection = _contextSelection.ToList();
        var result = await Confirm(new ConfirmRequest(
            selection.Count == 1
                ? $"Delete “{selection[0].Name}” from disk?"
                : $"Delete {selection.Count} mods from disk?",
            RowContextMenu.DeleteConsequence(selection),
            Verb: selection.Count == 1 ? "Delete mod" : $"Delete {selection.Count} mods"));

        if (!result.Confirmed) return;

        var deleted = 0;
        foreach (var row in selection)
        {
            try
            {
                _workspace.FileSystem.DeleteDirectory(row.Mod.RootPath, recursive: true);
                deleted++;
                _log.Warn(LogSubsystem.Io, $"Deleted mod folder {row.Mod.RootPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error(LogSubsystem.Io, $"could not delete {row.Mod.RootPath}: {ex.Message}");
            }
        }

        StatusText = $"Deleted {deleted} of {Plural.Of(selection.Count, "mod folder")}. Rescanning…";
        await ReloadAsync();
    }

    // --- apply, as an inline bar (2i-2) -------------------------------------

    public CommitBarViewModel Commit { get; } = new();

    /// <summary>
    /// What the menu, the toolbar and ⌘S actually invoke. It does <b>not</b> write —
    /// it raises the inline bar. Apply is never a one-click write to the game folder
    /// (non-negotiable #4); the bar is the confirmation, and it is inline rather than
    /// modal so it does not steal the window.
    /// </summary>
    [RelayCommand]
    private void RequestApply()
    {
        if (_installPaths is null || _modsConfig is null)
        {
            StatusText = "Nothing to apply (no config loaded).";
            return;
        }

        // Re-read the game's file FIRST, so every decision below is made against what is
        // on disk now rather than what the last scan happened to see. Without this the
        // drift check answers a question about the past.
        RefreshGameOrder();

        var blocking = Warnings.Count(w => w.Severity == ValidationSeverity.Error);
        if (blocking > 0 && RefuseApplyWithBlockingWarnings)
        {
            // Default is to refuse: a missing dependency means the game fails to
            // load, and discovering that from RimWorld's own error screen is worse.
            // Settings ▸ Advanced can turn the refusal off, and then this falls through
            // to the reasons below rather than to a silent write.
            // D3 · the same noun the OVERRIDDEN path writes into this same slot
            // (ApplyConcerns: "1 blocking warning overridden"). The bar was contradicting
            // itself about one word in one place.
            Commit.ShowBlocked(
                $"{Plural.Of(blocking, "blocking warning")} would leave the game unable to load.");
            return;
        }

        var reasons = ApplyConcerns.For(Drift, RefuseApplyWithBlockingWarnings ? 0 : blocking);

        // Nothing to say, or the user has turned the confirmation off: write. The bar used
        // to appear on every apply and ask a question with one answer, which is how a
        // confirmation becomes a second click. Either way this is the SAME write — one path
        // to the game folder, never a second one.
        if (reasons.Count == 0 || !ConfirmBeforeApply)
        {
            _ = ApplyCommand.ExecuteAsync(null);
            return;
        }

        Commit.Show(ActiveCount, ApplyConcerns.Summarise(reasons));
    }

    /// <summary>
    /// Re-reads <c>ModsConfig.xml</c> and reclassifies the drift.
    /// <para>
    /// <c>_modsConfig</c> is a snapshot taken at the last scan, and <c>Apply</c> builds the
    /// file it writes <em>from that snapshot</em>. So a RimWorld that rewrote the file forty
    /// minutes ago — which is what accepting its own <b>"Load mod list from save"</b> does —
    /// was overwritten with no notice and no record, because the only thing that would have
    /// noticed is a reload the user had no reason to run.
    /// </para>
    /// <para>
    /// It <b>assigns</b> <c>_modsConfig</c> rather than reading into a local: one field, one
    /// truth. A second reader of this file is how two surfaces come to disagree, and this
    /// project has paid for that lesson more than once.
    /// </para>
    /// </summary>
}
