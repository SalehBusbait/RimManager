namespace RimManager.App.ViewModels;

/// <summary>
/// Sizing for the image viewer (N8): fit the image into the available viewport,
/// preserving aspect, and never scaling it UP.
/// <para>
/// Never upscale, measured rather than assumed: the median <c>Preview.png</c> on a
/// real 559-mod install is 640×360, so nearly every preview fits a modern screen at
/// 1:1 — and a 640px banner stretched across a work area is blur presented as
/// detail. The handful larger than the screen (the largest measured is 2575×1449)
/// scale down.
/// </para>
/// </summary>
public static class ImageViewerLayout
{
    /// <summary>
    /// The display size for an image inside a viewport. Degenerate viewports fall
    /// back to the image's natural size — a window that cannot be measured is not a
    /// reason to show nothing.
    /// </summary>
    public static (double Width, double Height) Fit(
        double imageWidth, double imageHeight, double maxWidth, double maxHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0) return (0, 0);

        var scale = Math.Min(1.0, Math.Min(maxWidth / imageWidth, maxHeight / imageHeight));
        if (double.IsNaN(scale) || scale <= 0) scale = 1.0;

        return (imageWidth * scale, imageHeight * scale);
    }
}
