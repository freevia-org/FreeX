using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

public class ImageDiffTests
{
    [Fact]
    public void Identical_images_score_zero()
    {
        var a = PixelImage.Solid(40, 30, 12, 34, 56, 255);
        var b = PixelImage.Solid(40, 30, 12, 34, 56, 255);
        ImageDiff.MeanPixelDiffPercent(a, b).Should().Be(0.0);
    }

    [Fact]
    public void Black_vs_white_at_canonical_aspect_scores_one_hundred()
    {
        // Square sources letterbox into the 800x600 canvas leaving identical white bars,
        // so only the centered content region differs. Use a 4:3 source so the content
        // fills the WHOLE canvas (no bars) and the diff is a clean 100%.
        var black = PixelImage.Solid(80, 60, 0, 0, 0, 255);
        var white = PixelImage.Solid(80, 60, 255, 255, 255, 255);
        ImageDiff.MeanPixelDiffPercent(black, white).Should().BeApproximately(100.0, 0.001);
    }

    [Fact]
    public void Black_vs_white_square_letterboxes_to_content_fraction()
    {
        // 50x50 -> fits 600x600 centered in 800x600; bars (100px each side) are identical
        // white. Differing region = 600*600 of 800*600 = 75% of pixels at full contrast.
        var black = PixelImage.Solid(50, 50, 0, 0, 0, 255);
        var white = PixelImage.Solid(50, 50, 255, 255, 255, 255);
        ImageDiff.MeanPixelDiffPercent(black, white).Should().BeApproximately(75.0, 0.5);
    }

    [Fact]
    public void Slightly_different_image_scores_small_nonzero()
    {
        // 4:3 source fills the whole canvas, so the per-channel delta maps directly.
        var a = PixelImage.Solid(80, 60, 100, 100, 100, 255);
        var b = PixelImage.Solid(80, 60, 110, 110, 110, 255); // +10 each channel
        var diff = ImageDiff.MeanPixelDiffPercent(a, b);
        diff.Should().BeApproximately(10.0 / 255.0 * 100.0, 0.05); // ≈ 3.92%
    }

    [Fact]
    public void Transparent_pixels_composite_over_white()
    {
        // Fully transparent black should composite to white, matching an opaque-white image.
        var transparent = PixelImage.Solid(16, 16, 0, 0, 0, 0);
        var white = PixelImage.Solid(16, 16, 255, 255, 255, 255);
        ImageDiff.MeanPixelDiffPercent(transparent, white).Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void Different_aspect_ratios_letterbox_without_throwing()
    {
        var wide = PixelImage.Solid(200, 40, 50, 60, 70, 255);
        var tall = PixelImage.Solid(40, 200, 50, 60, 70, 255);
        // Same color, different shape: letterboxing leaves white bars => some diff, but bounded.
        var diff = ImageDiff.MeanPixelDiffPercent(wide, tall);
        diff.Should().BeGreaterThan(0).And.BeLessThan(100);
    }

    [Fact]
    public void Logical_viewport_diff_normalizes_equivalent_dpi_sizes()
    {
        var logical = PixelImage.Solid(40, 30, 20, 40, 60, 255);
        var highDpi = PixelImage.Solid(50, 38, 20, 40, 60, 255);

        ImageDiff.LogicalViewportMeanPixelDiffPercent(logical, highDpi, 40, 30)
            .Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void Logical_viewport_diff_uses_full_viewport_without_letterbox_bars()
    {
        var black = PixelImage.Solid(50, 38, 0, 0, 0, 255);
        var white = PixelImage.Solid(40, 30, 255, 255, 255, 255);

        ImageDiff.LogicalViewportMeanPixelDiffPercent(black, white, 40, 30)
            .Should().BeApproximately(100.0, 0.001);
    }
}
