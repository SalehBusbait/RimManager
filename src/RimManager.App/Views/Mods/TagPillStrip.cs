using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Mods;

/// <summary>
/// The row's tag pills (v2 §4A.1), drawn directly rather than templated: one
/// ItemsControl per row is too heavy at 565 rows, so this control renders its
/// pills into the DrawingContext — labels while they fit, then colour dots, then
/// "+n" (<see cref="TagPillLayout"/> owns the ladder, testably).
/// <para>
/// Pills carry their own bg + inset border so they read on any row wash (zebra,
/// selection, the relationship tints). Colours resolve through the live theme
/// dictionaries at render, and a theme change invalidates, so the pills flip with
/// the palette exactly as the stripe did — index-persisted, never hex.
/// </para>
/// </summary>
public sealed class TagPillStrip : Control
{
    public static readonly StyledProperty<IReadOnlyList<TagPill>?> PillsProperty =
        AvaloniaProperty.Register<TagPillStrip, IReadOnlyList<TagPill>?>(nameof(Pills));

    private const double Gap = 4;
    private const double DotSize = 7;
    private const double PadX = 5;
    private const double FontSize = 9;

    static TagPillStrip()
    {
        AffectsMeasure<TagPillStrip>(PillsProperty);
        AffectsRender<TagPillStrip>(PillsProperty);
    }

    public IReadOnlyList<TagPill>? Pills
    {
        get => GetValue(PillsProperty);
        set => SetValue(PillsProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private Typeface PillTypeface()
    {
        var family = this.TryFindResource("RmFontUi", ActualThemeVariant, out var f) && f is FontFamily ff
            ? ff
            : FontFamily.Default;
        return new Typeface(family, weight: FontWeight.Bold);
    }

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : null;

    private FormattedText Label(string text, Typeface typeface, IBrush brush) =>
        new(text.ToUpperInvariant(), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, FontSize, brush);

    private (int Labelled, int Dots, int Overflow, double[] Widths) Layout(double budget)
    {
        var pills = Pills;
        if (pills is null || pills.Count == 0) return (0, 0, 0, []);

        var typeface = PillTypeface();
        var fallback = Brushes.Gray;   // measurement only; render resolves real brushes
        var widths = new double[pills.Count];
        for (var i = 0; i < pills.Count; i++)
            widths[i] = Math.Ceiling(Label(pills[i].Name, typeface, fallback).Width) + PadX * 2;

        var (labelled, dots, overflow) = TagPillLayout.Arrange(
            widths, budget, Gap, DotSize,
            rest => Math.Ceiling(Label($"+{rest}", typeface, fallback).Width));

        return (labelled, dots, overflow, widths);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pills = Pills;
        if (pills is null || pills.Count == 0) return default;

        // Infinity is NamePillPanel asking what the zone would like if nothing pushed
        // back — every pill labelled. The panel then measures again at what it can
        // actually spare, and that second pass is where the ladder fires.
        var (labelled, dots, overflow, widths) = Layout(
            double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width);

        var used = 0d;
        for (var i = 0; i < labelled; i++) used += (i > 0 ? Gap : 0) + widths[i];
        if (dots > 0) used += (labelled > 0 ? Gap : 0) + dots * DotSize + (dots - 1) * Gap;
        if (overflow > 0)
        {
            var text = Label($"+{overflow}", PillTypeface(), Brushes.Gray);
            used += (labelled + dots > 0 ? Gap : 0) + Math.Ceiling(text.Width);
        }

        var height = PillHeight();
        return new Size(Math.Min(used, availableSize.Width), height);
    }

    /// <summary>h14 on compact rows, h16 on comfortable (v2 spec).</summary>
    private double PillHeight()
    {
        var rowHeight = this.TryFindResource("RmRowHeight", ActualThemeVariant, out var v) && v is double d
            ? d
            : 20;
        return rowHeight >= 26 ? 16 : 14;
    }

    public override void Render(DrawingContext context)
    {
        var pills = Pills;
        if (pills is null || pills.Count == 0) return;

        var (labelled, dots, overflow, widths) = Layout(Bounds.Width);
        var typeface = PillTypeface();
        var pillH = PillHeight();
        var top = (Bounds.Height - pillH) / 2;
        var x = 0d;

        for (var i = 0; i < labelled; i++)
        {
            var fg = Brush($"RmPalette{pills[i].PaletteIndex}Brush");
            var bg = Brush($"RmPalette{pills[i].PaletteIndex}BgBrush");
            if (fg is null || bg is null) continue;

            var rect = new Rect(x, top, widths[i], pillH);
            context.DrawRectangle(bg, null, rect, pillH / 2, pillH / 2);
            if (fg is ISolidColorBrush solid)
            {
                // The inset 1px border at ~35%: the second channel that keeps a pill
                // legible on the selection/relationship washes.
                var pen = new Pen(new SolidColorBrush(solid.Color, 0.35), 1);
                context.DrawRectangle(null, pen, rect.Deflate(0.5), pillH / 2, pillH / 2);
            }

            var text = Label(pills[i].Name, typeface, fg);
            context.DrawText(text, new Point(x + PadX, top + (pillH - text.Height) / 2));
            x += widths[i] + Gap;
        }

        for (var i = 0; i < dots; i++)
        {
            var fg = Brush($"RmPalette{pills[labelled + i].PaletteIndex}Brush");
            if (fg is null) continue;

            var cy = top + pillH / 2;
            context.DrawEllipse(fg, null, new Point(x + DotSize / 2, cy), DotSize / 2, DotSize / 2);
            x += DotSize + Gap;
        }

        if (overflow > 0 && Brush("RmPalette5Brush") is { } slate)
        {
            var text = Label($"+{overflow}", typeface, slate);
            context.DrawText(text, new Point(x, top + (pillH - text.Height) / 2));
        }
    }
}
