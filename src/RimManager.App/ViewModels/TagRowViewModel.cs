using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// One row of the tag table on Settings ▸ Tags &amp; metadata (<c>2g</c>): colour chip,
/// name, how many mods carry it, and how many auto-assign rules it has.
/// </summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    public TagRowViewModel(Tag tag, int modCount)
    {
        Id = tag.Id;
        _name = tag.Name;
        _paletteIndex = Palette.Normalize(tag.PaletteIndex);
        ModCount = modCount;
        Rules = TagsPresenter.RulesLabel(tag.AutoAssign.IsDefaultOrEmpty ? 0 : tag.AutoAssign.Length);
    }

    public string Id { get; }

    [ObservableProperty] private string _name;

    /// <summary>
    /// Hue by bound style class, never a converter — a converter hands back a brush
    /// frozen at conversion time, which then survives a theme switch unchanged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPalette0), nameof(IsPalette1), nameof(IsPalette2))]
    [NotifyPropertyChangedFor(nameof(IsPalette3), nameof(IsPalette4), nameof(IsPalette5))]
    private int _paletteIndex;

    public int ModCount { get; }
    public string Rules { get; }

    public bool IsPalette0 => PaletteIndex == 0;
    public bool IsPalette1 => PaletteIndex == 1;
    public bool IsPalette2 => PaletteIndex == 2;
    public bool IsPalette3 => PaletteIndex == 3;
    public bool IsPalette4 => PaletteIndex == 4;
    public bool IsPalette5 => PaletteIndex == 5;
}

/// <summary>
/// One of the six palette choices in the tag editor (<c>2g</c>).
/// <para>
/// An items collection rather than six hand-written buttons, because the six buttons
/// carried <c>CommandParameter="0"</c> — a <b>string</b>, which a command taking an
/// <c>int</c> silently refuses. Every swatch was dead, and nothing said so: the binding
/// failure is a log line, not an exception. Here the parameter is a real <c>int</c>.
/// </para>
/// </summary>
public sealed partial class TagPaletteChoiceViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>The hue's name, so a colour-only control is reachable by screen reader.</summary>
    public string Name { get; } = Palette.NameOf(index);

    [ObservableProperty] private bool _isSelected;

    public bool IsPalette0 => Index == 0;
    public bool IsPalette1 => Index == 1;
    public bool IsPalette2 => Index == 2;
    public bool IsPalette3 => Index == 3;
    public bool IsPalette4 => Index == 4;
    public bool IsPalette5 => Index == 5;
}

/// <summary>
/// One editable auto-assign condition (<c>2g</c>). Structured — a kind and a value —
/// rather than the free text the mockup renders: the display form is unambiguous to read
/// and ambiguous to parse, and a mistyped rule that silently matches nothing is exactly
/// the failure a text box invites.
/// </summary>
public sealed partial class TagConditionRowViewModel : ObservableObject
{
    public TagConditionRowViewModel(TagCondition condition)
    {
        _kindIndex = TagsPresenter.IndexFromKind(condition.Kind);
        _value = condition.Value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(IsSize))]
    private int _kindIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _value;

    public TagCondition ToCondition() => new(TagsPresenter.KindFromIndex(KindIndex), Value.Trim());

    /// <summary>Whether the value is a number of megabytes rather than text.</summary>
    public bool IsSize => TagsPresenter.KindFromIndex(KindIndex) == TagConditionKind.SizeOverMb;

    /// <summary>
    /// The rule as it will read, shown live beside the fields. The quotes are the point:
    /// they make a trailing space visible, and a condition matching <c>"Oskar "</c> would
    /// otherwise never fire with nothing on screen to explain why.
    /// </summary>
    public string Preview => TagsPresenter.ConditionText(ToCondition());
}
