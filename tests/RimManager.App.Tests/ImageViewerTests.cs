using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The image viewer's two pure pieces (N8): the fit math and the footer wording.
/// The window itself is thin glue over these — the sizing decision and the claim the
/// footer makes are what a test can hold.
/// </summary>
public sealed class ImageViewerTests
{
    // --- ImageViewerLayout.Fit ------------------------------------------------

    [Fact]
    public void A_small_image_is_never_upscaled()
    {
        ImageViewerLayout.Fit(640, 360, 1800, 1000).Should().Be((640d, 360d),
            "the median real preview is 640×360, and stretching it across a work "
            + "area would present blur as detail");
    }

    [Fact]
    public void An_oversized_image_scales_down_preserving_aspect()
    {
        ImageViewerLayout.Fit(2000, 1000, 1000, 1000).Should().Be((1000d, 500d));
    }

    [Fact]
    public void A_tall_image_is_limited_by_height()
    {
        ImageViewerLayout.Fit(500, 2000, 1000, 1000).Should().Be((250d, 1000d));
    }

    [Fact]
    public void A_degenerate_image_yields_zero()
    {
        ImageViewerLayout.Fit(0, 0, 1000, 1000).Should().Be((0d, 0d));
    }

    [Fact]
    public void A_degenerate_viewport_falls_back_to_natural_size()
    {
        ImageViewerLayout.Fit(640, 360, 0, 0).Should().Be((640d, 360d),
            "a screen that cannot be measured is not a reason to show nothing");
    }

    // --- ImageViewerViewModel.Footer -----------------------------------------

    [Fact]
    public void The_footer_names_the_file_its_pixels_and_its_bytes()
    {
        ImageViewerViewModel.Footer("Preview.png", 640, 360, 251_000)
            .Should().Be("Preview.png · 640×360 · 245 KB");
    }

    [Fact]
    public void The_footer_omits_a_size_it_does_not_know()
    {
        ImageViewerViewModel.Footer("Preview.png", 640, 360, null)
            .Should().Be("Preview.png · 640×360",
                "an unknown byte count is omitted, never guessed");
    }

    [Fact]
    public void The_footer_switches_to_megabytes_past_one()
    {
        ImageViewerViewModel.Footer("Preview.png", 2575, 1449, 1_572_864)
            .Should().Be("Preview.png · 2575×1449 · 1.5 MB");
    }

    [Fact]
    public void A_tiny_file_still_reports_at_least_one_kilobyte()
    {
        ImageViewerViewModel.Footer("Preview.png", 64, 64, 300)
            .Should().Be("Preview.png · 64×64 · 1 KB",
                "0 KB beside a visible image would be an obviously false claim");
    }

    [Fact]
    public void Fit_zoom_reports_the_shrink_and_never_exceeds_one()
    {
        // 640×360 in a 320×320 viewport: width is the binding side at 0.5.
        ImageViewerViewModel.ZoomFor(fit: true, 640, 360, 320, 320).Should().Be(0.5);

        // A viewport larger than the image does not upscale — the never-upscale
        // rule as arithmetic.
        ImageViewerViewModel.ZoomFor(fit: true, 640, 360, 2000, 2000).Should().Be(1.0);
    }

    [Fact]
    public void One_to_one_is_always_exactly_100_percent()
    {
        ImageViewerViewModel.ZoomFor(fit: false, 2575, 1449, 320, 200).Should().Be(1.0);
    }

    [Fact]
    public void An_unmeasured_viewport_reads_100_rather_than_dividing_by_zero()
    {
        ImageViewerViewModel.ZoomFor(fit: true, 640, 360, 0, 0).Should().Be(1.0);
    }
}
