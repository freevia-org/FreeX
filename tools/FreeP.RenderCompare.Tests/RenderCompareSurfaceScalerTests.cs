using Avalonia;
using Avalonia.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeP.RenderCompare.Tests;

public sealed class RenderCompareSurfaceScalerTests
{
    [Theory]
    [InlineData(12192000L, 6858000L, 1000, 1000, 1000, 562)]
    [InlineData(9144000L, 6858000L, 1000, 1000, 1000, 750)]
    [InlineData(12192000L, 6858000L, 320, 180, 320, 180)]
    public void Native_render_size_preserves_deck_ratio_before_Office_surface_stretch(
        long slideWidthEmu,
        long slideHeightEmu,
        int targetWidth,
        int targetHeight,
        int expectedWidth,
        int expectedHeight)
    {
        var presentation = new FreeP.Core.Model.Presentation
        {
            SlideSizeCxEmu = slideWidthEmu,
            SlideSizeCyEmu = slideHeightEmu,
        };

        RenderCompareSurfaceScaler.ResolveNativeRenderSize(
                presentation,
                targetWidth,
                targetHeight)
            .Should()
            .Be(new RenderCompareSurfaceSize(expectedWidth, expectedHeight));
    }

    [Fact]
    public void Stretch_produces_the_requested_fixed_surface()
    {
        FreePAvaloniaRenderer.EnsureAppInitialised();
        var source = CreatePng(4, 3);

        var stretched = RenderCompareSurfaceScaler.StretchPngToSurface(source, 11, 7);

        using var bitmap = new Bitmap(new MemoryStream(stretched, writable: false));
        bitmap.PixelSize.Should().Be(new PixelSize(11, 7));
    }

    [Fact]
    public void Stretch_keeps_existing_bytes_when_surface_already_matches()
    {
        var source = CreatePng(4, 3);

        RenderCompareSurfaceScaler.StretchPngToSurface(source, 4, 3)
            .Should()
            .Equal(source);
    }

    private static byte[] CreatePng(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 30;
            pixels[offset + 1] = 90;
            pixels[offset + 2] = 160;
            pixels[offset + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
