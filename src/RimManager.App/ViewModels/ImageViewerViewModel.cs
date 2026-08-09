using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RimManager.App.ViewModels;

/// <summary>
/// The full preview image behind the Mod Info crop (N8, UI-10).
/// <para>
/// The info pane shows <c>Preview.png</c> as a 344×120 <c>UniformToFill</c> crop, and
/// on the real install that crop hides content on nearly every mod — 540 of 546
/// measured previews are taller than the band (the median is 640×360). This window is
/// the rest of the picture. One image, deliberately: the other files under
/// <c>About/</c> are Workshop-description embeds and upload residue
/// (<c>patreon.png</c>, <c>changelog.png</c>, <c>description_1.png</c>…), not a
/// gallery — measured, 51 of 559 mods carry any at all.
/// </para>
/// <para>
/// Reuses the Bitmap the info pane already decoded, so opening the viewer costs no
/// disk read. The footer states the file's real name, pixel size and bytes, each read
/// from the file rather than assumed.
/// </para>
/// </summary>
public sealed partial class ImageViewerViewModel : ObservableObject
{
    public ImageViewerViewModel(string modName, Bitmap image, string fileName, long? fileBytes)
    {
        Title = modName;
        Image = image;
        PixelWidth = image.PixelSize.Width;
        PixelHeight = image.PixelSize.Height;
        FooterText = Footer(fileName, PixelWidth, PixelHeight, fileBytes);
    }

    /// <summary>The window title — the mod's name, so the taskbar names the mod.</summary>
    public string Title { get; }

    public Bitmap Image { get; }

    /// <summary>Pixel size, not DIP size: a PNG carrying odd DPI metadata must not
    /// change what the footer claims the file holds.</summary>
    public int PixelWidth { get; }

    public int PixelHeight { get; }

    /// <summary>"Preview.png · 640×360 · 245 KB".</summary>
    public string FooterText { get; }

    // --- Fit / 1:1 (T6, S-VIEWER) --------------------------------------------
    // Fit is the opening state and never upscales (DownOnly); 1:1 shows the file's
    // real pixels behind scrollbars. The zoom line states what Fit is actually
    // showing — "63%" answers the question a shrunk screenshot always raises.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOneToOne), nameof(ZoomText))]
    private bool _isFit = true;

    /// <summary>The segmented pair's other half — one bool, two radios.</summary>
    public bool IsOneToOne
    {
        get => !IsFit;
        set => IsFit = !value;
    }

    private double _viewportWidth;
    private double _viewportHeight;

    /// <summary>The view reports its body size on every layout pass; the zoom line
    /// follows. The VIEW measures and the VM computes — the arithmetic stays pure
    /// and tested (<see cref="ZoomFor"/>).</summary>
    public void UpdateViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        OnPropertyChanged(nameof(ZoomText));
    }

    public string ZoomText =>
        $"{Math.Round(ZoomFor(IsFit, PixelWidth, PixelHeight, _viewportWidth, _viewportHeight) * 100)}%";

    /// <summary>Pure: the scale the image is rendered at. Fit never exceeds 1 —
    /// the never-upscale rule as arithmetic.</summary>
    public static double ZoomFor(bool fit, int pixelWidth, int pixelHeight, double viewW, double viewH)
    {
        if (!fit) return 1.0;
        if (pixelWidth <= 0 || pixelHeight <= 0 || viewW <= 0 || viewH <= 0) return 1.0;
        return Math.Min(1.0, Math.Min(viewW / pixelWidth, viewH / pixelHeight));
    }

    /// <summary>Static and pure so the footer wording is testable without a Bitmap.</summary>
    public static string Footer(string fileName, int width, int height, long? bytes)
    {
        var dims = $"{fileName} · {width}×{height}";
        return bytes is { } b ? $"{dims} · {ByteSize.Format(b)}" : dims;
    }
}
