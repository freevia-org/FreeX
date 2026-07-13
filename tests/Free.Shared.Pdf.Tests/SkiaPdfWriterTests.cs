using FluentAssertions;
using Free.Shared.Pdf.Skia;
using SkiaSharp;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaPdfWriterTests
{
    [Fact]
    public void TryGetSourceRect_MapsPdfImageCropToSkiaSourceRectangle()
    {
        using var bitmap = CreateTestBitmap();
        using var image = SKImage.FromBitmap(bitmap);

        var hasCrop = SkiaPdfWriter.TryGetSourceRect(
            image,
            new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375),
            out var sourceRect);

        hasCrop.Should().BeTrue();
        sourceRect.Left.Should().Be(4);
        sourceRect.Top.Should().Be(2);
        sourceRect.Width.Should().Be(8);
        sourceRect.Height.Should().Be(8);
    }

    [Fact]
    public void Write_AcceptsSourceCroppedImages()
    {
        var imageBytes = EncodePng(CreateTestBitmap());
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                20,
                60,
                40,
                imageBytes,
                "image/png",
                SourceCrop: new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375)),
        });
        using var stream = new MemoryStream();

        var pageCount = SkiaPdfWriter.Write(new PdfContentDocument(new[] { page }), stream);

        pageCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(PdfImageClipKind.Triangle)]
    [InlineData(PdfImageClipKind.Diamond)]
    [InlineData(PdfImageClipKind.Parallelogram)]
    [InlineData(PdfImageClipKind.Hexagon)]
    [InlineData(PdfImageClipKind.Chevron)]
    public void Write_AcceptsPresetClippedImages(PdfImageClipKind clipKind)
    {
        var imageBytes = EncodePng(CreateTestBitmap());
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                20,
                60,
                40,
                imageBytes,
                "image/png",
                ClipKind: clipKind),
        });
        using var stream = new MemoryStream();

        var pageCount = SkiaPdfWriter.Write(new PdfContentDocument(new[] { page }), stream);

        pageCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreatePresetClipPath_BuildsChevronWithinImageBounds()
    {
        using var path = SkiaPdfWriter.CreatePresetClipPath(
            PdfImageClipKind.Chevron,
            new SKRect(10, 20, 70, 60));

        path.Bounds.Left.Should().Be(10);
        path.Bounds.Top.Should().Be(20);
        path.Bounds.Right.Should().Be(70);
        path.Bounds.Bottom.Should().Be(60);
    }

    [Fact]
    public void Write_AcceptsOpacityGroups()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfOpacityGroup(
                0.4,
                new PdfDrawOp[]
                {
                    new PdfStrokeRect(10, 20, 30, 40, new PdfColor(0x11, 0x22, 0x33), 3),
                }),
        });
        using var stream = new MemoryStream();

        var pageCount = SkiaPdfWriter.Write(new PdfContentDocument(new[] { page }), stream);

        pageCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ApplyColorEffects_TransformsDecodedImagePixels()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 128));
        using var image = SKImage.FromBitmap(bitmap);

        using var transformed = SkiaPdfWriter.ApplyColorEffects(
            image,
            new PdfImageColorEffects(
                Grayscale: true,
                BiLevelThreshold: null,
                Brightness: null,
                Contrast: null));

        transformed.Should().NotBeNull();
        using var transformedBitmap = SKBitmap.FromImage(transformed!);
        var pixel = transformedBitmap.GetPixel(0, 0);
        pixel.Red.Should().Be(54);
        pixel.Green.Should().Be(54);
        pixel.Blue.Should().Be(54);
        pixel.Alpha.Should().Be(128);
    }

    private static SKBitmap CreateTestBitmap()
    {
        var bitmap = new SKBitmap(16, 16);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        return bitmap;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using (bitmap)
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            return data.ToArray();
        }
    }
}
