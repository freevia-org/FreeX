using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class PremultipliedBgraRasterEffectsTests
{
    [Fact]
    public void ApplyAdjustmentsInPlace_AppliesBrightnessToBgraChannels()
    {
        byte[] pixels = [10, 20, 30, 255];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct: 10,
            contrastPct: 0,
            saturationPct: 100,
            transparencyPct: 0,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        pixels.Should().Equal(36, 46, 56, 255);
    }

    [Fact]
    public void ApplyAdjustmentsInPlace_PreservesPremultipliedTransparency()
    {
        byte[] pixels = [25, 50, 100, 128];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct: 0,
            contrastPct: 0,
            saturationPct: 100,
            transparencyPct: 50,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        pixels.Should().Equal(13, 25, 50, 64);
    }

    [Fact]
    public void ApplyAdjustmentsInPlace_PreservesRendererTransparencyChannelProfiles()
    {
        byte[] wpfPixels = [10, 20, 30, 255];
        byte[] avaloniaPixels = [10, 20, 30, 255];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            wpfPixels,
            brightnessPct: 10,
            contrastPct: 0,
            saturationPct: 100,
            transparencyPct: 50,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);
        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            avaloniaPixels,
            brightnessPct: 10,
            contrastPct: 0,
            saturationPct: 100,
            transparencyPct: 50,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.SourceChannels);

        wpfPixels.Should().Equal(18, 23, 28, 128);
        avaloniaPixels.Should().Equal(5, 10, 15, 128);
    }

    [Fact]
    public void ApplyAdjustmentsInPlace_UsesSharedRecolorLuminance()
    {
        byte[] pixels = [10, 20, 100, 255];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct: 0,
            contrastPct: 0,
            saturationPct: 100,
            transparencyPct: 0,
            ImageRecolorMode.Grayscale,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        pixels.Should().Equal(36, 36, 36, 255);
    }

    [Fact]
    public void ApplyAdjustmentsInPlace_ScalesContrastAroundMidpoint()
    {
        byte[] pixels = [64, 128, 192, 255];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct: 0,
            contrastPct: 100,
            saturationPct: 100,
            transparencyPct: 0,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        pixels.Should().Equal(0, 129, 255, 255);
    }

    [Fact]
    public void ApplyAdjustmentsInPlace_DesaturatesUsingRec709Luminance()
    {
        byte[] pixels = [10, 20, 100, 255];

        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct: 0,
            contrastPct: 0,
            saturationPct: 0,
            transparencyPct: 0,
            ImageRecolorMode.None,
            colorTemperature: 0,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        pixels.Should().Equal(36, 36, 36, 255);
    }

    [Fact]
    public void BoxBlur_UsesClampedEdgesAndPreservesAlpha()
    {
        byte[] pixels =
        [
            0, 0, 0, 255,
            90, 90, 90, 192,
            180, 180, 180, 128,
        ];

        var result = PremultipliedBgraRasterEffects.BoxBlur(pixels, 3, 1, 12, 1);

        result.Should().Equal(
            30, 30, 30, 234,
            90, 90, 90, 191,
            150, 150, 150, 149);
        pixels.Should().Equal(
            0, 0, 0, 255,
            90, 90, 90, 192,
            180, 180, 180, 128);
    }

    [Fact]
    public void Sobel_ProducesSharedEdgeMagnitude()
    {
        var pixels = SolidColumns(0, 0, 255);

        var result = PremultipliedBgraRasterEffects.Sobel(pixels, 3, 3, 12);

        result.Should().Equal(0, 0, 0, 0, 255, 0, 0, 0, 0);
    }

    [Fact]
    public void Posterize_QuantizesRgbAndPreservesPremultipliedAlpha()
    {
        byte[] pixels = [10, 90, 200, 128];

        var applied = PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 1, 1, 4, ImageArtisticEffect.Posterize, out var result);

        applied.Should().BeTrue();
        result.Should().Equal(0, 85, 170, 128);
    }

    [Fact]
    public void Watercolor_BlursSaturatesThenBrightensInEstablishedOrder()
    {
        byte[] pixels = [10, 20, 100, 255];

        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 1, 1, 4, ImageArtisticEffect.Watercolor, out var result).Should().BeTrue();

        result.Should().Equal(13, 26, 126, 255);
        pixels.Should().Equal(10, 20, 100, 255);
    }

    [Theory]
    [InlineData(ImageArtisticEffect.Blur)]
    [InlineData(ImageArtisticEffect.PencilGrayscale)]
    [InlineData(ImageArtisticEffect.PencilSketch)]
    [InlineData(ImageArtisticEffect.LineDrawing)]
    [InlineData(ImageArtisticEffect.Paintbrush)]
    [InlineData(ImageArtisticEffect.PaintStrokes)]
    [InlineData(ImageArtisticEffect.Pastels)]
    public void SharedArtisticEffects_ReturnIndependentDeterministicBuffers(ImageArtisticEffect effect)
    {
        var pixels = SolidColumns(0, 90, 180);
        var original = (byte[])pixels.Clone();

        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 3, 3, 12, effect, out var first).Should().BeTrue();
        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 3, 3, 12, effect, out var second).Should().BeTrue();

        first.Should().Equal(second);
        first.Should().NotBeSameAs(pixels);
        pixels.Should().Equal(original);
    }

    [Fact]
    public void FilmGrain_IsDeterministicAndDoesNotChangeAlpha()
    {
        byte[] pixels = [80, 100, 120, 128, 20, 40, 60, 64];

        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 2, 1, 8, ImageArtisticEffect.FilmGrain, out var first).Should().BeTrue();
        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 2, 1, 8, ImageArtisticEffect.FilmGrain, out var second).Should().BeTrue();

        first.Should().Equal(second);
        first.SequenceEqual(pixels).Should().BeFalse();
        first[3].Should().Be(128);
        first[7].Should().Be(64);
    }

    [Fact]
    public void Mosaic_AveragesTwoByTwoBlocksAndKeepsPerPixelAlpha()
    {
        const int width = 40;
        const int height = 40;
        const int stride = width * 4;
        var pixels = new byte[stride * height];
        SetPixel(pixels, stride, 0, 0, 0, 20, 40, 255);
        SetPixel(pixels, stride, 1, 0, 20, 40, 60, 192);
        SetPixel(pixels, stride, 0, 1, 40, 60, 80, 128);
        SetPixel(pixels, stride, 1, 1, 60, 80, 100, 64);

        PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, width, height, stride, ImageArtisticEffect.Mosaic, out var result).Should().BeTrue();

        Pixel(result, stride, 0, 0).Should().Equal(30, 50, 70, 255);
        Pixel(result, stride, 1, 0).Should().Equal(30, 50, 70, 192);
        Pixel(result, stride, 0, 1).Should().Equal(30, 50, 70, 128);
        Pixel(result, stride, 1, 1).Should().Equal(30, 50, 70, 64);
    }

    [Theory]
    [InlineData(ImageArtisticEffect.GlowDiffused)]
    [InlineData(ImageArtisticEffect.GlowEdges)]
    [InlineData(ImageArtisticEffect.Photocopy)]
    public void RendererSpecificLegacyEffects_AreNotClaimedBySharedKernel(ImageArtisticEffect effect)
    {
        byte[] pixels = [10, 20, 30, 255];

        var applied = PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
            pixels, 1, 1, 4, effect, out var result);

        applied.Should().BeFalse();
        result.Should().BeSameAs(pixels);
    }

    [Fact]
    public void RendererAdapters_DelegatePortableRasterMathToSharedKernel()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "ImageAdjustHelper.cs"));
        var avaloniaCore = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "Editing", "AvaloniaImageAdjustHelper.cs"));
        var avaloniaEffects = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "Editing", "AvaloniaImageAdjustHelper.Effects.cs"));

        foreach (var source in new[] { wpf, avaloniaCore })
            source.Should().Contain("PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(");
        foreach (var source in new[] { wpf, avaloniaEffects })
        {
            source.Should().Contain("PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(");
            source.Should().NotContain("private static byte[] BoxBlur(");
            source.Should().NotContain("new Random(12345)");
        }
    }

    private static byte[] SolidColumns(byte left, byte middle, byte right)
    {
        var pixels = new byte[3 * 3 * 4];
        for (var y = 0; y < 3; y++)
        {
            SetPixel(pixels, 12, 0, y, left, left, left, 255);
            SetPixel(pixels, 12, 1, y, middle, middle, middle, 255);
            SetPixel(pixels, 12, 2, y, right, right, right, 255);
        }

        return pixels;
    }

    private static void SetPixel(
        byte[] pixels,
        int stride,
        int x,
        int y,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        var offset = y * stride + x * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = a;
    }

    private static byte[] Pixel(byte[] pixels, int stride, int x, int y)
    {
        var offset = y * stride + x * 4;
        return pixels[offset..(offset + 4)];
    }
}
