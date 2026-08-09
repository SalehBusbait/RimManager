using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Abstractions;
using System.Collections.Immutable;
using RimManager.App.Services;
using RimManager.App.Shortcuts;
using RimManager.App.Themes;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>The seven pages on the 172px rail (<c>1c</c>, <c>2g</c>), in rail order.</summary>
public enum SettingsPage
{
    Paths,
    SortingAndRules,
    Integrations,
    Appearance,
    TagsAndMetadata,
    Modlists,
    Advanced,
}

/// <summary>
/// Backs the Settings dialog (spec 1c): edits the install's paths and the theme, and
/// reports rules/integration status.
/// <para>
/// <b>Nothing here is committed by a button.</b> The commit bar's Reset / Cancel / Save
/// governed only the four path fields — the other six pages had been live since R6 — so a
/// bar that looked like it owned the window in fact owned one page of seven, and Cancel
/// was a bare <c>Close()</c> that discarded nothing because nothing had been held back.
/// The paths now save as they are edited, like every other surface, through a
/// <see cref="SerialWriter{T}"/>.
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IFileSystem _fs;
    private readonly Func<(string? Game, string? Config, string? Workshop)> _detectPaths;

    /// <summary>
    /// Where a path edit lands. Serialised and latest-wins: the fields push on every
    /// keystroke, which is far faster than a disk round-trip, and two unawaited saves
    /// racing on one file is the bug that lost 3 of 5 preference writes and later crashed
    /// the app inside <c>File.Replace</c>.
    /// </summary>
    private readonly SerialWriter<InstallPaths> _pathWriter;

    /// <summary>Local mods that are git working trees, measured once by the scan (1c).</summary>
    private readonly int _gitTrackedRepos;

    /// <summary>Which page the 172px rail has selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPathsPage), nameof(IsSortingPage),
                              nameof(IsIntegrationsPage),
                              nameof(IsAppearancePage), nameof(IsTagsPage), nameof(IsModlistsPage),
                              nameof(IsAdvancedPage),
                              nameof(IsUnbuiltPage),
                              nameof(PageTitle))]
    private int _pageIndex;

    public SettingsPage Page => (SettingsPage)PageIndex;

    public bool IsPathsPage => Page == SettingsPage.Paths;
    public bool IsSortingPage => Page == SettingsPage.SortingAndRules;
    public bool IsIntegrationsPage => Page == SettingsPage.Integrations;
    public bool IsAppearancePage => Page == SettingsPage.Appearance;
    public bool IsTagsPage => Page == SettingsPage.TagsAndMetadata;
    public bool IsModlistsPage => Page == SettingsPage.Modlists;
    public bool IsAdvancedPage => Page == SettingsPage.Advanced;

    /// <summary>The pages that still say so rather than pretending.</summary>
    public bool IsUnbuiltPage =>
        !IsPathsPage && !IsSortingPage && !IsIntegrationsPage && !IsAppearancePage && !IsTagsPage && !IsModlistsPage && !IsAdvancedPage;

    /// <summary>
    /// Measuring the integrations touches processes and disk, so it happens when the
    /// page is first shown rather than when the window opens — and once, not on every
    /// visit back to it.
    /// </summary>
    partial void OnPageIndexChanged(int value)
    {
        if (IsIntegrationsPage && !_integrationsLoaded) _ = LoadIntegrations();
    }

    public bool IsCompactDensity
    {
        get => !Prefs.IsComfortableDensity;
        set { if (value) Prefs.IsComfortableDensity = false; }
    }

    // --- theme (design handoff v2) ------------------------------------------
    // The accent picker is GONE: accents are theme-bound, each dictionary authors
    // its own. T4 made this the GALLERY (S-GALLERY): a full-width follow-system
    // card, then one card per theme — mini-preview under that theme's own tokens
    // (ThemePreviewHost scopes, never writes), badge, name, variant. Still the
    // N4g chip shape: display state fires nothing, the choice fires from the
    // command.

    /// <summary>Follow system first, then the ten themes in roster order — the one
    /// truth the resync walks. The markup reads the two views below.</summary>
    public ObservableCollection<ThemeChoiceViewModel> ThemeChoices { get; } = [];

    /// <summary>The full-width first card (S-GALLERY): follow-system previews a pair,
    /// not a theme, so it is authored separately from the per-theme cards.</summary>
    public ThemeChoiceViewModel? FollowChoice =>
        ThemeChoices.FirstOrDefault(c => c.Theme == AppTheme.FollowSystem);

    /// <summary>The ten per-theme cards, roster order.</summary>
    public IEnumerable<ThemeChoiceViewModel> ThemeCards =>
        ThemeChoices.Where(c => c.Theme != AppTheme.FollowSystem);

    private void BuildThemeChoices()
    {
        ThemeChoices.Add(new ThemeChoiceViewModel(
            AppTheme.FollowSystem, "Follow system (Drop Pods pair)", t => Prefs.Theme = t));
        foreach (var info in ThemeCatalog.All)
        {
            var label = info.IsLight ? $"{info.DisplayName} · light" : info.DisplayName;
            ThemeChoices.Add(new ThemeChoiceViewModel(info.Theme, label, t => Prefs.Theme = t));
        }

        SyncThemeChoices();
    }

    /// <summary>The pref is the one truth; every row mirrors it (the log-level shape).</summary>
    private void SyncThemeChoices()
    {
        foreach (var choice in ThemeChoices) choice.IsSelected = choice.Theme == Prefs.Theme;
    }

    /// <summary>The Font dropdown's rows (<c>2g</c>). Labels only — the family strings are
    /// an implementation detail nobody should have to read.</summary>
    public static IReadOnlyList<string> FontChoices { get; } =
        [.. UiFonts.Choices.Select(f => f.Label)];

    // --- Tags & metadata (2g) -----------------------------------------------

    private readonly ITagStore? _tags;

    public ObservableCollection<TagRowViewModel> TagRows { get; } = [];
    public ObservableCollection<TagConditionRowViewModel> Conditions { get; } = [];

    /// <summary>The six palette choices in the tag editor.</summary>
    public ObservableCollection<TagPaletteChoiceViewModel> TagPalette { get; } =
        [.. Enumerable.Range(0, Palette.Count).Select(i => new TagPaletteChoiceViewModel(i))];

    public static IReadOnlyList<string> ConditionKinds => TagsPresenter.ConditionKinds;

    /// <summary>"7 · used on 141 mods".</summary>
    [ObservableProperty] private string _tagHeader = "none yet";

    /// <summary>The on-disk line under METADATA STORAGE.</summary>
    [ObservableProperty] private string _metadataStorageLine = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagSelection), nameof(EditTitle))]
    private TagRowViewModel? _selectedTag;

    public bool HasTagSelection => SelectedTag is not null;

    /// <summary>"EDIT · OVERHAUL" — the card names what it is editing, so a two-column
    /// layout never leaves you wondering which row the fields belong to.</summary>
    public string EditTitle => SelectedTag is null
        ? "EDIT"
        : $"EDIT · {SelectedTag.Name.ToUpperInvariant()}";

    partial void OnSelectedTagChanged(TagRowViewModel? value)
    {
        LoadConditionsFor(value);
        SyncTagPalette();
    }

    private void SyncTagPalette()
    {
        foreach (var choice in TagPalette)
            choice.IsSelected = SelectedTag is not null && choice.Index == SelectedTag.PaletteIndex;
    }

    private void LoadConditionsFor(TagRowViewModel? row)
    {
        Conditions.Clear();
        if (_tags is null || row is null) return;

        foreach (var tag in _tags.Tags.Tags.Where(t => t.Id == row.Id))
        {
            foreach (var condition in tag.AutoAssign) Conditions.Add(Track(new TagConditionRowViewModel(condition)));
        }
    }

    /// <summary>Edits save on change, which is now how every page in this window
    /// commits — there is no Save button anywhere in it.</summary>
    private TagConditionRowViewModel Track(TagConditionRowViewModel row)
    {
        // A named handler, not a lambda, so it can be detached when the row goes.
        row.PropertyChanged += OnConditionChanged;
        return row;
    }

    private void OnConditionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        _ = PersistTagsAsync();

    private void OnTagRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Name only, and only the row the editor is bound to: PersistTagsAsync reads
        // SelectedTag, so persisting for another row's notification would save the
        // wrong row's fields over it.
        if (e.PropertyName == nameof(TagRowViewModel.Name) && ReferenceEquals(sender, SelectedTag))
            _ = PersistTagsAsync();
    }

    private void RefreshTagRows(string? keepSelectedId = null)
    {
        if (_tags is null) return;

        var counts = _tags.CountsByTagId();
        TagRows.Clear();
        foreach (var tag in _tags.Tags.Tags)
        {
            var row = new TagRowViewModel(tag, counts.GetValueOrDefault(tag.Id));
            // A RENAME saves like every other edit (UI audit — the Name TextBox
            // edited the row TwoWay and nothing listened, so a rename showed in the
            // list and was silently lost on close unless some other edit happened to
            // save). The palette swatch persists through its own command already.
            row.PropertyChanged += OnTagRowChanged;
            TagRows.Add(row);
        }

        TagHeader = TagsPresenter.Header(TagRows.Count, _tags.TaggedModCount());
        MetadataStorageLine = _tags.StorageLine();
        SelectedTag = TagRows.FirstOrDefault(r => r.Id == keepSelectedId) ?? TagRows.FirstOrDefault();
    }

    [RelayCommand]
    private async Task NewTag()
    {
        if (_tags is null) return;

        var tag = new Tag
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = TagsPresenter.UniqueName(_tags.Tags.Tags),

            // Cycles the palette so consecutive new tags are distinguishable at a glance
            // instead of all arriving blue.
            PaletteIndex = Palette.Normalize(TagRows.Count),
        };

        await _tags.SaveAsync(new TagSet(_tags.Tags.Tags.Add(tag)));
        RefreshTagRows(tag.Id);
        Report($"Added tag “{tag.Name}”.");
    }

    /// <summary>
    /// Deletes a tag through the shared destructive confirm (2i-6), and removes it from
    /// every mod that carried it. Both halves: a tag id left behind on a mod is a
    /// reference to something that no longer exists.
    /// </summary>
    [RelayCommand]
    private async Task DeleteTag()
    {
        if (_tags is null || Confirm is null || SelectedTag is not { } row) return;

        var used = row.ModCount;
        var result = await Confirm(new ConfirmRequest(
            $"Delete the tag “{row.Name}”?",
            used == 0
                ? "It is not on any mod. Nothing else changes."
                : $"It will be removed from {used} mod{(used == 1 ? "" : "s")}. Their notes, "
                  + "favourites and other tags are kept, and no mod folder is touched.",
            Verb: "Delete tag"));

        if (!result.Confirmed) return;

        var cleared = await _tags.DeleteTagAsync(row.Id);
        RefreshTagRows();
        Report(cleared == 0
            ? $"Deleted tag “{row.Name}”."
            : $"Deleted tag “{row.Name}” and removed it from {cleared} mod{(cleared == 1 ? "" : "s")}.");
    }

    [RelayCommand]
    private async Task AddCondition()
    {
        Conditions.Add(Track(new TagConditionRowViewModel(
            new TagCondition(TagConditionKind.AuthorContains, string.Empty))));
        await PersistTagsAsync();
    }

    [RelayCommand]
    private async Task RemoveCondition(TagConditionRowViewModel row)
    {
        // Unsubscribe first. A removed row whose TextBox is still pushing its value would
        // otherwise keep triggering saves from a row nobody can see any more.
        row.PropertyChanged -= OnConditionChanged;
        Conditions.Remove(row);
        await PersistTagsAsync();
    }

    /// <summary>Recolours the selected tag. The palette INDEX is stored, never a hex
    /// (non-negotiable #6), which is what lets it flip with the theme.</summary>
    [RelayCommand]
    private async Task ChooseTagColour(int paletteIndex)
    {
        if (SelectedTag is not { } row) return;

        row.PaletteIndex = Palette.Normalize(paletteIndex);
        SyncTagPalette();
        await PersistTagsAsync();
    }

    /// <summary>Writes the edited tag back. Conditions that cannot match anything are
    /// dropped rather than saved: an empty value would tag the whole library on the next
    /// scan.</summary>
    private async Task PersistTagsAsync()
    {
        if (_tags is null || SelectedTag is not { } row) return;

        var conditions = Conditions
            .Select(c => c.ToCondition())
            .Where(TagsPresenter.IsUsable)
            .ToImmutableArray();

        var updated = _tags.Tags.Tags.Select(t => t.Id == row.Id
            ? t with
            {
                Name = string.IsNullOrWhiteSpace(row.Name) ? t.Name : row.Name.Trim(),
                PaletteIndex = row.PaletteIndex,
                AutoAssign = conditions,
            }
            : t).ToImmutableArray();

        await _tags.SaveAsync(new TagSet(updated));
        OnPropertyChanged(nameof(EditTitle));
    }

    // --- Instances (2g) -----------------------------------------------------

    private readonly IModlistStore? _modlists;

    /// <summary>
    /// Supplied by the view. Owned by the SETTINGS window, because a confirm
    /// parented to the main window would open behind this modal.
    /// </summary>
    public Confirmer? Confirm { get; set; }

    public static string WhatIsAModlist => ModlistsPresenter.WhatIsAModlist;

    public ObservableCollection<ModlistRowViewModel> ModlistRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModlistSelection), nameof(CanDeleteModlist),
                              nameof(WhyDeleteRefused), nameof(DeleteConsequence),
                              nameof(SelectedModlistTitle), nameof(CanMakeDefault),
                              nameof(CapturesModSettings), nameof(ModSettingsSummary))]
    private ModlistRowViewModel? _selectedModlistRow;

    public bool HasModlistSelection => SelectedModlistRow is not null;

    /// <summary>
    /// The default cannot go, and neither can the last one standing. Both are enforced in
    /// the repository too — a rule that only exists in a view model is not a rule, and the
    /// CLI has no buttons to grey out.
    /// </summary>
    public bool CanDeleteModlist =>
        SelectedModlistRow is { } row
        && DefaultModlist.CanDelete(row.Modlist, ModlistRows.Count);

    /// <summary>Why it is refused, said out loud beside the disabled button.</summary>
    public string? WhyDeleteRefused => SelectedModlistRow is { } row
        ? ModlistsPresenter.WhyDeleteIsRefused(row.IsDefault, ModlistRows.Count)
        : null;

    /// <summary>Making an already-default list default again is a no-op worth disabling.</summary>
    public bool CanMakeDefault => SelectedModlistRow is { IsDefault: false };

    /// <summary>"SELECTED · HEAVILY MODDED" — the block names what it is editing.</summary>
    public string SelectedModlistTitle => SelectedModlistRow is { } r
        ? $"SELECTED · {r.Name.ToUpperInvariant()}"
        : "SELECTED";

    public string DeleteConsequence => SelectedModlistRow is { } row
        ? ModlistsPresenter.DeleteConsequence(row.Name, row.Snapshots, row.SettingsFiles)
        : string.Empty;

    public string ModSettingsSummary => SelectedModlistRow is { } row
        ? ModlistsPresenter.ModSettingsSummary(row.CapturesModSettings, row.SettingsFiles)
        : string.Empty;

    /// <summary>
    /// The capture toggle, as a settable property so the switch IS the state.
    /// <para>
    /// Turning it OFF discards what was captured. That is deliberate and the summary says
    /// so: keeping a snapshot nothing will ever restore is disk pretending to be a feature,
    /// and leaving it would silently reappear the next time capture was switched back on.
    /// </para>
    /// </summary>
    public bool CapturesModSettings
    {
        get => SelectedModlistRow?.CapturesModSettings ?? false;
        set
        {
            if (_modlists is null || SelectedModlistRow is not { } row) return;
            if (row.CapturesModSettings == value) return;

            _ = ApplyCaptureAsync(row, value);
        }
    }

    private async Task ApplyCaptureAsync(ModlistRowViewModel row, bool captures)
    {
        await _modlists!.SetCapturesModSettingsAsync(row.Modlist, captures);
        RefreshModlistRows(row.Id);
        Report(captures
            ? $"“{row.Name}” will keep its own mod settings."
            : $"“{row.Name}” no longer keeps its own mod settings.");
    }

    private void RefreshModlistRows(string? keepSelectedId = null)
    {
        if (_modlists is null) return;

        var now = DateTimeOffset.Now;
        ModlistRows.Clear();
        foreach (var modlist in _modlists.All)
        {
            ModlistRows.Add(new ModlistRowViewModel(
                modlist,
                _modlists.SnapshotCount(modlist),
                _modlists.SettingsFileCount(modlist),
                modlist.Id == _modlists.CurrentId,
                now));
        }

        SelectedModlistRow = ModlistRows.FirstOrDefault(r => r.Id == keepSelectedId)
                             ?? ModlistRows.FirstOrDefault(r => r.IsCurrent)
                             ?? ModlistRows.FirstOrDefault();
        SyncModlistPalette();
        OnPropertyChanged(nameof(CanDeleteModlist));
        OnPropertyChanged(nameof(WhyDeleteRefused));
    }

    [RelayCommand]
    private async Task RenameModlist()
    {
        if (_modlists is null || SelectedModlistRow is not { } row) return;
        if (string.IsNullOrWhiteSpace(row.Name)) return;

        await _modlists.RenameAsync(row.Modlist, row.Name.Trim());
        RefreshModlistRows(row.Id);
        Report($"Renamed to “{row.Name.Trim()}”.");
    }

    [RelayCommand]
    private async Task NewModlist()
    {
        if (_modlists is null) return;

        var name = ModlistsPresenter.CopyName(_modlists.All.Select(l => l.Name), "New list");
        var created = await _modlists.CreateAsync(name);

        RefreshModlistRows(created.Id);
        Report($"Created “{name}” — empty, so every installed mod starts inactive.");
    }

    [RelayCommand]
    private async Task DuplicateModlist()
    {
        if (_modlists is null || SelectedModlistRow is not { } row) return;

        var name = ModlistsPresenter.CopyName(_modlists.All.Select(l => l.Name), row.Name);
        var clone = await _modlists.DuplicateAsync(row.Modlist, name);

        RefreshModlistRows(clone.Id);
        Report($"Duplicated as “{name}” — same mods, same order, same separators.");
    }

    /// <summary>
    /// Moves the default flag. The only way to change it, and therefore the only way to
    /// make the current default deletable.
    /// </summary>
    [RelayCommand]
    private async Task MakeDefaultModlist()
    {
        if (_modlists is null || SelectedModlistRow is not { } row || row.IsDefault) return;

        await _modlists.SetDefaultAsync(row.Modlist);
        RefreshModlistRows(row.Id);
        Report($"“{row.Name}” is now the default.");
    }

    /// <summary>
    /// Deletes through the shared destructive confirm (<c>2i</c>-6) — one shape for every
    /// destructive action, because the shape IS the safety feature.
    /// </summary>
    [RelayCommand]
    private async Task DeleteModlist()
    {
        if (_modlists is null || Confirm is null) return;
        if (SelectedModlistRow is not { } row || !CanDeleteModlist) return;

        var result = await Confirm(new ConfirmRequest(
            $"Delete “{row.Name}”?",
            ModlistsPresenter.DeleteConsequence(row.Name, row.Snapshots, row.SettingsFiles),
            Verb: "Delete modlist"));

        if (!result.Confirmed) return;

        await _modlists.DeleteAsync(row.Modlist);
        RefreshModlistRows();
        Report($"Deleted “{row.Name}”. Your mods and ModsConfig.xml were not touched.");
    }

    // --- Advanced (2g) ------------------------------------------------------

    /// <summary>The keep-N snapshot choices, as 2g's dropdown.</summary>
    public static IReadOnlyList<string> SnapshotKeepChoices { get; } =
        ["Last 20", "Last 50", "Last 100", "Last 250", "Keep all"];

    private static readonly int[] SnapshotKeepValues = [20, 50, 100, 250, int.MaxValue];

    /// <summary>
    /// The dropdown's index, mapped to a count. An index rather than a free number:
    /// the prune is applied on every snapshot, and a typo of 1 instead of 100 would
    /// quietly discard a history the moment the next one is taken.
    /// </summary>
    public int SnapshotKeepIndex
    {
        get
        {
            var index = Array.IndexOf(SnapshotKeepValues, Prefs.KeepSnapshots);
            return index < 0 ? 2 : index;
        }
        set
        {
            if (value < 0 || value >= SnapshotKeepValues.Length) return;
            Prefs.KeepSnapshots = SnapshotKeepValues[value];
            OnPropertyChanged();
        }
    }

    // SnapshotDangerDetail is GONE (UI audit): it counted the Modlists-page-selected
    // row while the button acts on the OPEN modlist — a third scope on one control.
    // The confirm dialog states the real count at the moment it matters.

    /// <summary>The log-level segments, each separately bindable.</summary>
    public ObservableCollection<LogLevelChoiceViewModel> LogLevelChoices { get; }

    /// <summary>The six colour choices for the modlist dot.</summary>
    public ObservableCollection<TagPaletteChoiceViewModel> ModlistPalette { get; } =
        [.. Enumerable.Range(0, Palette.Count).Select(i => new TagPaletteChoiceViewModel(i))];

    private void SyncModlistPalette()
    {
        foreach (var choice in ModlistPalette)
            choice.IsSelected = SelectedModlistRow is not null
                                && choice.Index == SelectedModlistRow.PaletteIndex;
    }

    /// <summary>Recolours the modlist's dot. An INDEX, never a hex (#6).</summary>
    [RelayCommand]
    private async Task ChooseModlistColour(int paletteIndex)
    {
        if (_modlists is null || SelectedModlistRow is not { } row) return;

        await _modlists.RecolourAsync(row.Modlist, Palette.Normalize(paletteIndex));
        RefreshModlistRows(row.Id);
    }

    /// <summary>The heading for whichever page the rail has selected.</summary>
    public string PageTitle => Page switch
    {
        SettingsPage.SortingAndRules => "Sorting & rules",
        SettingsPage.Integrations => "Integrations",
        SettingsPage.Appearance => "Appearance",
        SettingsPage.TagsAndMetadata => "Tags & metadata",
        SettingsPage.Modlists => "Modlists",
        SettingsPage.Advanced => "Advanced",
        _ => "Paths",
    };

    // ConfigLocation went with the rail footer that was its only reader. Kept as a
    // property with nothing bound to it, it would be exactly the dead code the R2a
    // orphaned handlers were.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameCheck), nameof(LocalModsCheck), nameof(LocalModsDir))]
    private string _gameDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigCheck))]
    private string? _configDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkshopCheck))]
    private string? _workshopDir;

    /// <summary>
    /// Read-only: <c>InstallPaths.LocalModsDir</c> is derived as <c>&lt;game&gt;/Mods</c>,
    /// which is where RimWorld looks and the only place it looks. 1c draws a Browse
    /// button beside it; a settable override would be a Core change and a second
    /// source of truth for a folder the game does not let you move. Live off the
    /// field, like <see cref="LocalModsCheck"/> beneath it.
    /// </summary>
    public string LocalModsDir => System.IO.Path.Combine(GameDir, "Mods");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCmdCheck))]
    private string? _steamCmdDir;

    // The three path toggles that used to sit here — "Watch folders for changes",
    // "Back up ModsConfig.xml before every Apply", "Launch RimWorld after Apply" — are
    // gone. They were plain [ObservableProperty] fields, absent from IAppPreferences,
    // never persisted and never read by anything: three controls that took a click and
    // changed nothing, live since R6. Watching folders is N5's work and backup-before-Apply
    // is already unconditional in ApplyService, so each will come back as a real
    // preference or not at all.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>
    /// Whether the band exists at all.
    /// <para>
    /// It used to be a permanent 44px strip in a 640px window, blank almost always — dead
    /// chrome charging rent on the page body, most visibly on Advanced where the danger
    /// zone is the last thing on a long scroll. Now it appears when there is something to
    /// report and is absent otherwise, which is the whole of what the band was ever for.
    /// It is not merely hidden: a collapsed <c>IsVisible</c> gives the height back.
    /// </para>
    /// </summary>
    public bool HasStatus => Status.Length > 0;

    // --- the band's tone and lifetime (T5, S-SETBAND) ------------------------
    // ok / info / bad, each with its own lifetime: terminal ok and info results FADE
    // after four seconds — a result that stays after being read becomes furniture —
    // while bad persists until dismissed, because an error that removes itself was
    // never reported. In-flight work ("Downloading SteamCMD…") reports at info tone
    // WITHOUT the fade — it is not a result, and a progress line that vanishes
    // mid-download reads as the download having stopped. That is decision (a): the
    // band carries operations too, rather than per-card busy pills.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusOk), nameof(IsStatusInfo), nameof(IsStatusBad))]
    private string _statusTone = "info";

    public bool IsStatusOk => StatusTone == "ok";
    public bool IsStatusInfo => StatusTone == "info";
    public bool IsStatusBad => StatusTone == "bad";

    /// <summary>Monotonic stamp: a fade only clears the message it was scheduled for,
    /// so a report landing inside another's four seconds is never swept up with it.</summary>
    private int _statusSeq;

    private void Report(string text) => Show(text, "ok", fade: true);
    private void ReportInfo(string text) => Show(text, "info", fade: true);
    private void ReportBusy(string text) => Show(text, "info", fade: false);
    private void ReportBad(string text) => Show(text, "bad", fade: false);

    private async void Show(string text, string tone, bool fade)
    {
        var seq = ++_statusSeq;
        StatusTone = tone;
        Status = text;

        if (!fade) return;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (seq == _statusSeq) Status = string.Empty;
    }

    /// <summary>The band's ×. Only rendered for bad, the one tone that persists.</summary>
    [RelayCommand]
    private void DismissStatus()
    {
        _statusSeq++;
        Status = string.Empty;
    }

    // --- path persistence ---------------------------------------------------
    // Each field commits as it is edited. What is on screen is what is on disk, always:
    // holding an invalid path back would be a page that silently refuses to save, and the
    // verdict line under every field already says what is wrong and offers the fix.

    partial void OnGameDirChanged(string value) => PersistPaths();
    partial void OnConfigDirChanged(string? value) => PersistPaths();
    partial void OnWorkshopDirChanged(string? value) => PersistPaths();
    partial void OnSteamCmdDirChanged(string? value) => PersistPaths();

    private void PersistPaths() => _pathWriter.Queue(new InstallPaths
    {
        GameDir = GameDir.Trim(),
        ConfigDir = Blank(ConfigDir),
        WorkshopDir = Blank(WorkshopDir),
        SteamCmdDir = Blank(SteamCmdDir),
    });

    /// <summary>
    /// Awaits any queued path write. The caller reloads the install from disk the moment
    /// this window closes, so without the drain that reload can read the file as it was
    /// before the last keystroke landed — and hand the edit straight back.
    /// </summary>
    public Task FlushPathsAsync() => _pathWriter.DrainAsync();

    public string RulesStatus { get; }

    // --- Integrations (2g) --------------------------------------------------
    // Measured, not configured: everything here reports what is on this machine.
    // The three toggles beside the cards live on IAppPreferences with the rest.

    private readonly Func<Task<IntegrationStatus>>? _loadIntegrations;
    private readonly Func<CancellationToken, Task<string>>? _installSteamCmd;
    private bool _integrationsLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamHeadline), nameof(SteamPill), nameof(SteamRunning),
                              nameof(SteamCmdDetail), nameof(SteamCmdDetailTip),
                              nameof(SteamCmdInstalled),
                              nameof(GitHeadline), nameof(GitPathLine), nameof(GitPathTip),
                              nameof(GitFound))]
    private IntegrationStatus _integrations = IntegrationStatus.Unknown;

    /// <summary>True while the probe is running, so the cards can say so rather than
    /// briefly reporting "not running" about something they have not looked at yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamHeadline), nameof(GitHeadline))]
    private bool _isProbingIntegrations;

    [ObservableProperty] private bool _isInstallingSteamCmd;

    public string SteamHeadline => IsProbingIntegrations
        ? "Looking for the Steam client…"
        : IntegrationsPresenter.SteamHeadline(Integrations);

    public string SteamPill => IntegrationsPresenter.SteamPill(Integrations);
    public bool SteamRunning => Integrations.SteamClientRunning;
    // Instance rather than static: a compiled binding cannot reach a static member, and
    // the prose lives in the presenter so the page and its test read the same string.
    public string SteamUses => IntegrationsPresenter.SteamUses;

    public string SteamCmdUses => IntegrationsPresenter.SteamCmdUses;

    /// <summary>
    /// Budget for a path shown inside a card. Elided in the string rather than by the
    /// control: a TextBlock that trims cannot wrap, and a non-wrapping TextBlock measures
    /// at its full natural width — which pushed this card's buttons off the window.
    /// The full value is on the tooltip.
    /// </summary>
    private const int CardPathBudget = 40;

    public string SteamCmdDetail => IntegrationsPresenter.SteamCmdDetail(Integrations, CardPathBudget);
    public string SteamCmdDetailTip => IntegrationsPresenter.SteamCmdDetail(Integrations);
    public bool SteamCmdInstalled => Integrations.SteamCmdInstalled;

    public string GitHeadline => IsProbingIntegrations
        ? "Looking for git…"
        : IntegrationsPresenter.GitHeadline(Integrations);

    public string GitPathLine => IntegrationsPresenter.GitPathLine(Integrations, CardPathBudget);
    public string GitPathTip => IntegrationsPresenter.GitPathLine(Integrations);
    public bool GitFound => Integrations.GitVersion is not null;
    // CanManageRepos is GONE (UI audit): it was notify-wired and bound by NOTHING —
    // the "Manage repos…" button it was meant to enable is deliberately disabled
    // until a repo editor exists, so the VM was maintaining an enable state for a
    // control that could never use it.

    /// <summary>Re-measures the three cards. Also the Refresh button, since Steam can be
    /// started while this window is open.</summary>
    [RelayCommand]
    private async Task LoadIntegrations()
    {
        if (_loadIntegrations is null || IsProbingIntegrations) return;

        IsProbingIntegrations = true;
        try
        {
            Integrations = await _loadIntegrations();
            _integrationsLoaded = true;
        }
        catch (Exception ex)
        {
            ReportBad($"Could not read integration status: {ex.Message}");
        }
        finally
        {
            IsProbingIntegrations = false;
        }
    }

    /// <summary>
    /// Fetches Valve's bootstrapper into RimManager's own private SteamCMD directory.
    /// Says out loud that the long download is SteamCMD's, not ours: the bootstrapper is
    /// a few MB and its first run pulls a few hundred.
    /// </summary>
    [RelayCommand]
    private async Task InstallSteamCmd()
    {
        if (_installSteamCmd is null || IsInstallingSteamCmd) return;

        IsInstallingSteamCmd = true;
        ReportBusy("Downloading SteamCMD…");
        try
        {
            await _installSteamCmd(CancellationToken.None);
            Report("SteamCMD installed. Its own first-run self-update happens on first download.");
            await LoadIntegrations();
        }
        catch (Exception ex)
        {
            ReportBad($"SteamCMD install failed: {ex.Message}");
        }
        finally
        {
            IsInstallingSteamCmd = false;
        }
    }

    public SettingsViewModel(
        InstallPaths paths, string rulesStatus,
        Func<InstallPaths, Task> save, IFileSystem fs,
        Func<(string? Game, string? Config, string? Workshop)> detectPaths,
        IAppPreferences preferences,
        Func<Task<IntegrationStatus>>? loadIntegrations = null,
        Func<CancellationToken, Task<string>>? installSteamCmd = null,
        int gitTrackedRepos = 0,
        ITagStore? tags = null,
        IModlistStore? modlists = null)
    {
        _tags = tags;
        _modlists = modlists;
        _gitTrackedRepos = gitTrackedRepos;
        Prefs = preferences;
        _loadIntegrations = loadIntegrations;
        _installSteamCmd = installSteamCmd;

        // Built before anything can set a path property. The failure goes to Status,
        // which is the whole of what the commit bar carries now: a save that fails
        // silently is how a user loses work without being told.
        _pathWriter = new SerialWriter<InstallPaths>(
            save,
            ex => ReportBad($"Could not save the paths: {ex.Message}"));

        // The mirrors above are derived from Prefs, so they have to be told when the
        // thing they mirror changes underneath them — the toolbar can flip density
        // while this window is open.
        preferences.PropertyChanged += (_, e) =>
        {
            // The clicked row's highlight arrives through the resync — the pref is
            // the one truth, and every row mirrors it (the log-level shape).
            if (e.PropertyName == nameof(IAppPreferences.Theme)) SyncThemeChoices();
            if (e.PropertyName == nameof(IAppPreferences.IsComfortableDensity)) OnPropertyChanged(nameof(IsCompactDensity));

            // The clicked segment's highlight arrives through this resync too — the
            // pref is the one truth, and every segment mirrors it (N4g's chip shape).
            if (e.PropertyName == nameof(IAppPreferences.LogLevelIndex)) SyncLogLevelChoices();
        };
        _fs = fs;
        _detectPaths = detectPaths;
        RulesStatus = rulesStatus;

        _gameDir = paths.GameDir;
        _configDir = paths.ConfigDir;
        _workshopDir = paths.WorkshopDir;
        _steamCmdDir = paths.SteamCmdDir;

        BuildThemeChoices();
        RefreshTagRows();
        RefreshModlistRows();

        LogLevelChoices = [.. LogLevels.Choices.Select((c, i) =>
            new LogLevelChoiceViewModel(i, c.Label, index => Prefs.LogLevelIndex = index)
            {
                IsSelected = i == LogLevels.Clamp(preferences.LogLevelIndex),
            })];
    }

    /// <summary>Mirrors the pref onto the segments — the only writer of their
    /// <c>IsSelected</c>, which is display state with no side effect.</summary>
    private void SyncLogLevelChoices()
    {
        if (LogLevelChoices is null) return; // the hook is wired before the ctor builds them

        var current = LogLevels.Clamp(Prefs.LogLevelIndex);
        foreach (var choice in LogLevelChoices) choice.IsSelected = choice.Index == current;
    }

    /// <summary>
    /// The live preferences, bound straight through. Not copied and written back on a
    /// Save: two stores for one preference is how the Sort flyout and this page would
    /// end up disagreeing, with the sorter reading whichever was written last.
    /// <para>
    /// So these take effect immediately, which every page says out loud — and since the
    /// paths do the same now, the window has no commit step at all.
    /// </para>
    /// </summary>
    public IAppPreferences Prefs { get; }

    // Live per-field validation (1c): every field reports WHAT IT FOUND, not merely
    // whether the folder exists — a path that exists but holds the wrong thing is the
    // failure that wastes an afternoon.
    public PathCheck GameCheck => PathProbe.Game(_fs, GameDir);
    public PathCheck ConfigCheck => PathProbe.Config(_fs, ConfigDir);
    /// <summary>
    /// Passed the git count measured by the scan rather than probing again: local mods
    /// are the only place a clone can be, and the count on this line has to be the same
    /// number the Integrations card shows.
    /// </summary>
    public PathCheck LocalModsCheck =>
        PathProbe.LocalMods(_fs, System.IO.Path.Combine(GameDir, "Mods"), _gitTrackedRepos);
    public PathCheck WorkshopCheck => PathProbe.Workshop(_fs, WorkshopDir);
    public PathCheck SteamCmdCheck => PathProbe.SteamCmd(_fs, SteamCmdDir);

    /// <summary>
    /// Re-runs detection and fills in whatever it finds, without clearing what it
    /// does not — an auto-detect that blanked a hand-typed path the probe simply
    /// could not confirm would be worse than one that does nothing.
    /// </summary>
    [RelayCommand]
    private void AutoDetect()
    {
        var (game, config, workshop) = _detectPaths();

        if (!string.IsNullOrWhiteSpace(game)) GameDir = game;
        if (!string.IsNullOrWhiteSpace(config)) ConfigDir = config;
        if (!string.IsNullOrWhiteSpace(workshop)) WorkshopDir = workshop;

        if (game is null && config is null && workshop is null)
            ReportInfo("Nothing detected — RimWorld was not found in the usual places.");
        else
            Report("Filled in what was found; nothing was cleared.");
    }

    // "Reset to defaults" went with the commit bar. It reset the three dead toggles above
    // and then ran AutoDetect, so with those gone it was a second, worse-named Auto-detect
    // button — and its status line ("not saved yet") described a commit step that no
    // longer exists.

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
