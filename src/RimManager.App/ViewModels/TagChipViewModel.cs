using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One tag chip in the Mod Info pane (<c>1a</c> §4, <c>1e</c>).
/// <para>
/// Chips appear here and only here. In the mod lists a tag is a 3px stripe — the
/// highest-priority one — because a dense 20px row has no room for chips and the
/// stripe is decoration that duplicates what the chips and the tooltip already say
/// (<c>1e</c> §4). Rendering chips in a row would be the one place colour became the
/// sole carrier of meaning.
/// </para>
/// </summary>
public sealed class TagChipViewModel(Tag tag, bool assigned)
{
    public string Id { get; } = tag.Id;
    public string Name { get; } = tag.Name;
    public int PaletteIndex { get; } = Palette.Normalize(tag.PaletteIndex);
    public bool IsAssigned { get; } = assigned;

    // Hue by bound style class rather than a converter: a converter hands back a
    // brush frozen at conversion time, which survives a theme switch.
    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;
}
