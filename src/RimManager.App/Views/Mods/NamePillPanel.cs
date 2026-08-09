using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Mods;

/// <summary>
/// The row's NAME cell: the mod name, then <see cref="TagPillStrip"/> right-aligned
/// against the cell's edge so the pills line up down the list.
/// <para>
/// Both children are measured unconstrained first, and only then allocated —
/// <see cref="NamePillSplit"/> owns the arithmetic and says why no stock panel can
/// do this. The first child is the name, the second the pill zone.
/// </para>
/// </summary>
public sealed class NamePillPanel : Panel
{
    /// <summary>The width the name keeps when both children want more than there is.</summary>
    public static readonly StyledProperty<double> ReserveProperty =
        AvaloniaProperty.Register<NamePillPanel, double>(nameof(Reserve), 110d);

    private double _pills;

    public double Reserve
    {
        get => GetValue(ReserveProperty);
        set => SetValue(ReserveProperty, value);
    }

    static NamePillPanel() => AffectsMeasure<NamePillPanel>(ReserveProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;

        var name = Children[0];
        var pills = Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;
        var loose = new Size(double.PositiveInfinity, availableSize.Height);

        // What each would take if nothing pushed back.
        name.Measure(loose);
        pills?.Measure(loose);
        var nameWant = name.DesiredSize.Width;
        var pillWant = pills?.DesiredSize.Width ?? 0;

        if (double.IsInfinity(availableSize.Width))
        {
            _pills = pillWant;
            return new Size(nameWant + pillWant, TallestOf(name, pills));
        }

        var (nameWidth, pillWidth) = NamePillSplit.Split(
            availableSize.Width, nameWant, pillWant, Reserve);

        // Re-measure at the allocation: the name trims, and the pill zone runs its
        // ladder against the space it actually got.
        name.Measure(new Size(nameWidth, availableSize.Height));
        pills?.Measure(new Size(pillWidth, availableSize.Height));
        _pills = pills?.DesiredSize.Width ?? 0;

        return new Size(availableSize.Width, TallestOf(name, pills));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;

        var name = Children[0];
        var pills = Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;

        var pillWidth = Math.Min(_pills, finalSize.Width);
        // The name gets everything to the left of the pills — which can be more than
        // it was measured at, and that only means it trims less.
        name.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - pillWidth), finalSize.Height));
        pills?.Arrange(new Rect(finalSize.Width - pillWidth, 0, pillWidth, finalSize.Height));

        return finalSize;
    }

    private static double TallestOf(Layoutable name, Layoutable? pills) =>
        Math.Max(name.DesiredSize.Height, pills?.DesiredSize.Height ?? 0);
}
