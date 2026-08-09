using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One entry in the modlist switcher — the toolbar's selector, which is now what the user
/// switches with, in place of the instance selector that stood there.
/// <para>
/// The command lives on the ROW rather than being reached for with
/// <c>$parent[Window]</c>: a MenuFlyout's content sits in a popup, and an ancestor binding
/// that cannot find its target fails in silence, leaving every entry as a menu item that
/// highlights and does nothing. That exact bug shipped once already in this control.
/// </para>
/// </summary>
public sealed class ModlistChoiceViewModel
{
    public ModlistChoiceViewModel(
        Modlist modlist, bool isCurrent, Action<Modlist> select,
        DriftKind drift = DriftKind.Unknown)
    {
        Modlist = modlist;
        Name = modlist.Name;
        IsCurrent = isCurrent;
        ModCount = modlist.State.Entries.Count(e => e.Kind == ModlistEntryKind.Mod);
        PaletteIndex = Palette.Normalize(modlist.PaletteIndex);
        Drift = drift;
        SelectCommand = new RelayCommand(() => select(modlist));
    }

    public Modlist Modlist { get; }
    public string Name { get; }

    /// <summary>How many mods it holds, so the choice is informed rather than a guess.</summary>
    public int ModCount { get; }

    public string Label => $"{Name}   ({ModCount})";

    /// <summary>The flyout row's mono count, S-SELECTOR's wording.</summary>
    public string ActiveLabel => $"{ModCount} active";

    /// <summary>Ticked in the menu — a switcher that does not say where you are is a list.</summary>
    public bool IsCurrent { get; }

    /// <summary>
    /// The default list, marked here as it is on Settings ▸ Modlists (O19). Worth saying
    /// in the switcher too: it is the list that opens when nothing else has been used,
    /// and the one the Delete button refuses — both facts you want before choosing, not
    /// after.
    /// </summary>
    public bool IsDefault => Modlist.IsDefault;

    public ICommand SelectCommand { get; }

    // --- S-SELECTOR's palette-shell row (UI audit) ---------------------------
    // Swatch by bound class (the palette-index-not-hex rule), and a per-list drift
    // glyph — the same three glyphs the status footer draws, in-sync silent.

    public int PaletteIndex { get; }

    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;

    public DriftKind Drift { get; }

    public bool IsDriftEdited => Drift == DriftKind.PendingApply;
    public bool IsDriftOutside => Drift == DriftKind.ChangedOutsideRimManager;
    public bool IsDriftNever => Drift == DriftKind.Unknown;
}
