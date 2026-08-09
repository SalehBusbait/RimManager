using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>Group operations a separator triggers, provided by the owning view-model.</summary>
public interface ISeparatorHost
{
    void ToggleCollapse(SeparatorRowViewModel separator);
    void DeleteSeparator(SeparatorRowViewModel separator);

    /// <summary>
    /// The separator's own content changed — its name or its colour — and the arrangement
    /// has to be written back.
    /// <para>
    /// This did not exist, and its absence was a silent data-loss bug. Renaming a separator
    /// and recolouring one both edited the view model and stopped there: nothing subscribes
    /// to a row's <c>PropertyChanged</c>, and neither command touched the persistence
    /// chokepoint. The new name survived until the next unrelated edit happened to commit
    /// the arrangement — and if none did, until the app closed. Deleting and deactivating
    /// were fine all along, which is why it never showed up in testing: four menu items,
    /// two of which saved.
    /// </para>
    /// </summary>
    void SeparatorEdited(SeparatorRowViewModel separator);
}

/// <summary>Base for a row in either pane: a mod or a separator.</summary>
public abstract partial class RowViewModel : ObservableObject
{
    /// <summary>Display index (mods are numbered; separators are not).</summary>
    [ObservableProperty] private int _index;

    /// <summary>True when hidden because its owning separator is collapsed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowVisible))]
    private bool _isCollapsedChild;

    /// <summary>True when hidden by the active filter/search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowVisible))]
    private bool _isFilteredOut;

    public bool IsRowVisible => !IsCollapsedChild && !IsFilteredOut;

    // --- drag state (3a) -----------------------------------------------------

    /// <summary>
    /// True while this row is being dragged. Source rows dim to 35% and STAY IN
    /// PLACE — removing them mid-drag reflows the list under the pointer, which is
    /// the single most disorienting thing a reorder can do.
    /// </summary>
    [ObservableProperty] private bool _isDragSource;

    /// <summary>
    /// The index this row held before the drop, shown inline as "was 114" for 1.2s
    /// afterwards (3a §4). It is what lets the user confirm the move landed where
    /// they meant without hunting for it again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviousIndexText))]
    private int? _previousIndex;

    public string? PreviousIndexText => PreviousIndex is { } p ? $"was {p}" : null;

    /// <summary>True for the 1.2s the post-drop tint holds.</summary>
    [ObservableProperty] private bool _isJustMoved;
}

/// <summary>
/// A draggable, collapsible group header in the active pane (spec §4.3). Purely a
/// manager concept — RimWorld never sees separators. Groups are positional: a
/// separator "owns" the rows below it until the next separator.
/// </summary>
public sealed partial class SeparatorRowViewModel : RowViewModel
{
    private readonly ISeparatorHost? _host;

    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name;

    /// <summary>
    /// The label as 1f wants it: upper-case. Rendered from a TextBlock rather than
    /// the edit TextBox, because a TextBox cannot show caps without fighting what
    /// the user is typing — and its baseline sits lower than the chevron and count
    /// beside it, which reads as a misaligned row.
    /// </summary>
    public string DisplayName => Name.ToUpperInvariant();

    /// <summary>True while the label is being renamed (F2, or the ⋮ menu).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    /// <summary>
    /// Index into <see cref="Palette"/> — never a hex string (design non-negotiable
    /// #6), so the colour bar flips correctly with the theme.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPalette0), nameof(IsPalette1), nameof(IsPalette2))]
    [NotifyPropertyChangedFor(nameof(IsPalette3), nameof(IsPalette4), nameof(IsPalette5))]
    private int _paletteIndex;

    // Bound style classes rather than a value converter. A converter would resolve
    // RmPalette{n}Brush once and hand back a frozen brush, which then survives a
    // theme switch and paints the dark hue on a light window. A class keeps the
    // DynamicResource in the style, so it re-resolves for free.
    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseGlyph), nameof(CountText), nameof(CollapseMenuLabel))]
    private bool _collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _modCount;

    /// <summary>
    /// "96 mods" or "9 mods · collapsed" (1f). Always shown, so a collapsed group
    /// still reports its size rather than hiding how much it is holding.
    /// </summary>
    public string CountText => Collapsed ? $"{ModCount} mods · collapsed" : $"{ModCount} mods";

    /// <summary>
    /// The menu row's verb, which has to be the one it will actually perform. It read
    /// "Collapse" in both menus regardless of state, so on an already-collapsed group it
    /// named the opposite of what clicking it did.
    /// </summary>
    public string CollapseMenuLabel => Collapsed ? "Expand" : "Collapse";

    public string CollapseGlyph => Collapsed ? "▸" : "▾";

    public SeparatorRowViewModel(string id, string name, ISeparatorHost? host = null,
        int paletteIndex = 0, bool collapsed = false)
    {
        Id = id;
        _host = host;
        _name = name;
        _paletteIndex = Palette.Normalize(paletteIndex);
        _collapsed = collapsed;
    }

    /// <summary>What the name was when editing began, so Escape has something to restore.</summary>
    private string? _nameBeforeEdit;

    [RelayCommand]
    private void BeginRename()
    {
        _nameBeforeEdit = Name;
        IsEditing = true;
    }

    /// <summary>
    /// Commits the name: Enter, the ✓ button, or clicking away.
    /// <para>
    /// An empty name falls back to what it was rather than being accepted. The binding is
    /// two-way on every keystroke, so clearing the box and pressing Enter would otherwise
    /// leave a separator with no label at all — a 22px band of colour that cannot be told
    /// from its neighbour and cannot be searched for.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void EndRename()
    {
        if (!IsEditing) return;
        IsEditing = false;

        var trimmed = Name.Trim();
        Name = trimmed.Length > 0 ? trimmed : _nameBeforeEdit ?? "Separator";
        _nameBeforeEdit = null;

        _host?.SeparatorEdited(this);
    }

    /// <summary>
    /// Escape. It used to be bound to <see cref="EndRenameCommand"/>, which meant Escape
    /// looked like Cancel and behaved like OK: the name binding pushes on every keystroke,
    /// so by the time Escape arrived the edit had already landed and there was nothing left
    /// to abandon. It restores now, which is what the key has meant in every text field
    /// since text fields existed.
    /// </summary>
    [RelayCommand]
    private void CancelRename()
    {
        if (!IsEditing) return;
        IsEditing = false;

        if (_nameBeforeEdit is { } previous) Name = previous;
        _nameBeforeEdit = null;
    }

    [RelayCommand] private void ToggleCollapse() => _host?.ToggleCollapse(this);
    [RelayCommand] private void Delete() => _host?.DeleteSeparator(this);

    /// <summary>Recolours the separator. The INDEX is stored, never a hex (#6).</summary>
    [RelayCommand]
    private void ChooseColor(int paletteIndex)
    {
        PaletteIndex = Palette.Normalize(paletteIndex);
        _host?.SeparatorEdited(this);
    }
}
