using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Dialogs;

/// <summary>The full preview image (N8). See the markup comment for the shape.</summary>
public partial class ImageViewerWindow : Window
{
    /// <summary>The image's 12px margin, both sides, matching the markup.</summary>
    private const double ImagePad = 24;

    /// <summary>The header band's height, matching the markup (40 — the titled band
    /// every reference window shares).</summary>
    private const double HeaderHeight = 40;

    public ImageViewerWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // The zoom line follows the body's real size — the view measures, the view
        // model computes (ZoomFor is the pure, tested half).
        var body = this.FindControl<Panel>("ViewerBody")!;
        body.SizeChanged += (_, e) =>
        {
            if (DataContext is ImageViewerViewModel vm)
                vm.UpdateViewport(e.NewSize.Width - ImagePad, e.NewSize.Height - ImagePad);
        };
    }

    /// <summary>
    /// Sized when the view model arrives — which is before Show, so CenterOwner
    /// centers the FINAL size rather than the fallback. Sizing in OnOpened instead
    /// would let a large image grow the window out from under an already-computed
    /// centre, hanging it off the screen edge.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ImageViewerViewModel vm) return;

        // The owner is not known yet, so the primary screen stands in for it. On a
        // multi-monitor split the cap can differ from the owner's screen; the cap
        // binds only for images larger than ~90% of the work area, and the window
        // stays resizable.
        var screen = Screens?.Primary ?? Screens?.ScreenFromWindow(this);
        if (screen is null) return;

        // WorkingArea is physical pixels; Width/Height are DIPs.
        var maxW = screen.WorkingArea.Width / screen.Scaling * 0.9;
        var maxH = screen.WorkingArea.Height / screen.Scaling * 0.9;

        var footer = this.TryFindResource("RmDockFooterHeight", out var f) && f is double d ? d : 24;
        var chrome = footer + HeaderHeight;
        var (w, h) = ImageViewerLayout.Fit(
            vm.PixelWidth, vm.PixelHeight, maxW - ImagePad, maxH - ImagePad - chrome);
        if (w <= 0 || h <= 0) return;

        Width = Math.Max(MinWidth, w + ImagePad);
        Height = Math.Max(MinHeight, h + ImagePad + chrome);
    }

    /// <summary>Escape closes, like every non-modal reference window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
