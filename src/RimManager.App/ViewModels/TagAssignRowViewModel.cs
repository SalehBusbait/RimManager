using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of the assign flyout (O7, O8): a tag, and how much of the current
/// selection carries it.
/// <para>
/// Separate from <see cref="TagChipViewModel"/> because a chip answers "does this mod
/// have it" and this answers "how many of these mods have it" — a tri-state the chip
/// has no way to express.
/// </para>
/// </summary>
public sealed partial class TagAssignRowViewModel : ObservableObject
{
    public TagAssignRowViewModel(Tag tag, int assignedCount, int selectionCount)
    {
        Id = tag.Id;
        Name = tag.Name;
        PaletteIndex = Palette.Normalize(tag.PaletteIndex);
        _state = TagAssign.StateOf(assignedCount, selectionCount);
        AssignedCount = assignedCount;
        SelectionCount = selectionCount;
    }

    public string Id { get; }
    public string Name { get; }
    public int PaletteIndex { get; }
    public int AssignedCount { get; }
    public int SelectionCount { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAll), nameof(IsSome), nameof(CountText))]
    private TagAssignState _state;

    public bool IsAll => State == TagAssignState.All;

    /// <summary>Drives the "−" mark: some of the selection has it, not all.</summary>
    public bool IsSome => State == TagAssignState.Some;

    /// <summary>
    /// "1 of 3" beside a partial row, so the dash is a number rather than a mood.
    /// Blank for a single selection, where the tick already says everything.
    /// </summary>
    public string CountText =>
        SelectionCount > 1 && State == TagAssignState.Some
            ? $"{AssignedCount} of {SelectionCount}"
            : string.Empty;

    // Hue by bound style class rather than a converter: a converter hands back a
    // brush frozen at conversion time, which survives a theme switch.
    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;
}
