using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of the Tags ▾ filter flyout (3f): colour chip, name, and the number of
/// mods carrying the tag.
/// <para>
/// Filtering changes only which rows are <em>visible</em>; the load order is never
/// touched (1e). That is why this is a view concern and not a mutation of the
/// profile.
/// </para>
/// </summary>
public sealed partial class TagFilterViewModel : ObservableObject
{
    public TagFilterViewModel(Tag tag, int count)
    {
        Id = tag.Id;
        Name = tag.Name;
        PaletteIndex = Palette.Normalize(tag.PaletteIndex);
        Count = count;
    }

    public string Id { get; }
    public string Name { get; }
    public int PaletteIndex { get; }
    public int Count { get; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// The row's OWN command, like the modlist switcher's rows carry theirs.
    /// <para>
    /// Not an ancestor binding to a command on the hub: this row is rendered inside a
    /// Flyout, whose content lives in a popup, and a binding reaching out through
    /// <c>$parent[…]</c> from a popup resolves against nothing and fails <b>in silence</b>
    /// — a row that highlights, takes the click and does nothing. That exact bug has
    /// already shipped in this app once, which is why a markup guard now pins it.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;

    // Hue by bound style class rather than a converter: a converter hands back a
    // brush frozen at conversion time, which survives a theme switch.
    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;
}
