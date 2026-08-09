using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RimManager.App.Views.Controls;

/// <summary>
/// The four-segment proportional bar under "WHAT RIMMANAGER FOUND" (<c>2i</c>-3).
/// <para>
/// A drawn control rather than a <c>Grid</c> of star-width columns, because Avalonia's
/// <c>Grid.ColumnDefinitions</c> is a plain CLR property and a <c>ColumnDefinition</c>
/// is not in the logical tree — a <c>{Binding}</c> on either has no DataContext to
/// resolve against and would fail in silence, which is precisely the class of bug that
/// never reaches CI in this project.
/// </para>
/// <para>
/// Segments are proportional to the real counts. Colour is never the sole carrier of
/// meaning here: every segment restates a figure printed in words directly above it.
/// </para>
/// </summary>
public sealed class StackedBar : Control
{
    public static readonly StyledProperty<double> InstalledProperty =
        AvaloniaProperty.Register<StackedBar, double>(nameof(Installed));

    public static readonly StyledProperty<double> ToDownloadProperty =
        AvaloniaProperty.Register<StackedBar, double>(nameof(ToDownload));

    public static readonly StyledProperty<double> UnavailableProperty =
        AvaloniaProperty.Register<StackedBar, double>(nameof(Unavailable));

    public static readonly StyledProperty<double> AlreadyActiveProperty =
        AvaloniaProperty.Register<StackedBar, double>(nameof(AlreadyActive));

    public static readonly StyledProperty<IBrush?> InstalledBrushProperty =
        AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(InstalledBrush));

    public static readonly StyledProperty<IBrush?> ToDownloadBrushProperty =
        AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(ToDownloadBrush));

    public static readonly StyledProperty<IBrush?> UnavailableBrushProperty =
        AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(UnavailableBrush));

    public static readonly StyledProperty<IBrush?> AlreadyActiveBrushProperty =
        AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(AlreadyActiveBrush));

    static StackedBar() => AffectsRender<StackedBar>(
        InstalledProperty, ToDownloadProperty, UnavailableProperty, AlreadyActiveProperty,
        InstalledBrushProperty, ToDownloadBrushProperty, UnavailableBrushProperty,
        AlreadyActiveBrushProperty);

    public double Installed
    {
        get => GetValue(InstalledProperty);
        set => SetValue(InstalledProperty, value);
    }

    public double ToDownload
    {
        get => GetValue(ToDownloadProperty);
        set => SetValue(ToDownloadProperty, value);
    }

    public double Unavailable
    {
        get => GetValue(UnavailableProperty);
        set => SetValue(UnavailableProperty, value);
    }

    public double AlreadyActive
    {
        get => GetValue(AlreadyActiveProperty);
        set => SetValue(AlreadyActiveProperty, value);
    }

    public IBrush? InstalledBrush
    {
        get => GetValue(InstalledBrushProperty);
        set => SetValue(InstalledBrushProperty, value);
    }

    public IBrush? ToDownloadBrush
    {
        get => GetValue(ToDownloadBrushProperty);
        set => SetValue(ToDownloadBrushProperty, value);
    }

    public IBrush? UnavailableBrush
    {
        get => GetValue(UnavailableBrushProperty);
        set => SetValue(UnavailableBrushProperty, value);
    }

    public IBrush? AlreadyActiveBrush
    {
        get => GetValue(AlreadyActiveBrushProperty);
        set => SetValue(AlreadyActiveBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double[] values = [Installed, ToDownload, Unavailable, AlreadyActive];
        IBrush?[] brushes = [InstalledBrush, ToDownloadBrush, UnavailableBrush, AlreadyActiveBrush];

        var total = values[0] + values[1] + values[2] + values[3];
        if (total <= 0) return;   // nothing resolved yet: the track shows through

        var x = 0d;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0) continue;

            // The last non-empty segment runs to the edge, so rounding never leaves a
            // hairline of track showing at the right of a fully accounted-for bar.
            var isLast = true;
            for (var j = i + 1; j < values.Length; j++)
            {
                if (values[j] > 0) { isLast = false; break; }
            }

            var w = isLast ? width - x : width * values[i] / total;
            if (brushes[i] is { } brush) context.FillRectangle(brush, new Rect(x, 0, w, height));
            x += w;
        }
    }
}
