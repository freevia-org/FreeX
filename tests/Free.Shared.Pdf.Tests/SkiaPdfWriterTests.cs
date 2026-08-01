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
    public void Write_AcceptsClipGroupsAroundNestedTransforms()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                40,
                35,
                17,
                new PdfDrawOp[]
                {
                    new PdfClipGroup(
                        10,
                        15,
                        60,
                        40,
                        new PdfDrawOp[]
                        {
                            new PdfFillRect(0, 0, 120, 80, new PdfColor(0x11, 0x22, 0x33)),
                        }),
                }),
        });
        using var stream = new MemoryStream();

        var pageCount = SkiaPdfWriter.Write(new PdfContentDocument(new[] { page }), stream);

        pageCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WriteToBytes_EmbedsFontForNonWinAnsiText()
    {
        var page = new PdfContentPage(240, 120, new PdfDrawOp[]
        {
            new PdfText(12, 80, 18, PdfFontFace.Regular, new PdfColor(0, 0, 0), "Привет 世界 Καλημέρα"),
        });

        var bytes = SkiaPdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));

        bytes.Should().StartWith("%PDF-"u8.ToArray());
        bytes.Should().Contain("/Font"u8.ToArray());
        bytes.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Write_AcceptsLinearGradientShapeAndPathOps()
    {
        var gradient = new PdfLinearGradient(
            10,
            20,
            80,
            70,
            [
                new PdfGradientStop(0, new PdfColor(0x11, 0x22, 0x33)),
                new PdfGradientStop(0.5, new PdfColor(0x44, 0x55, 0x66)),
                new PdfGradientStop(1, new PdfColor(0xAA, 0xBB, 0xCC)),
            ]);
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillRectLinearGradient(10, 20, 30, 20, gradient, new PdfColor(0x11, 0x22, 0x33)),
            new PdfStrokeEllipseLinearGradient(45, 20, 30, 20, gradient, new PdfColor(0xAA, 0xBB, 0xCC), 2),
            new PdfPathLinearGradient(
                [
                    new PdfPathContour(
                        new PdfPathPoint(10, 10),
                        [
                            PdfPathSegment.LineTo(new PdfPathPoint(20, 10)),
                            PdfPathSegment.LineTo(new PdfPathPoint(20, 20)),
                        ],
                        Closed: true),
                ],
                gradient,
                new PdfColor(0x11, 0x22, 0x33),
                null,
                new PdfColor(0xAA, 0xBB, 0xCC),
                1),
        });
        using var stream = new MemoryStream();

        var pageCount = SkiaPdfWriter.Write(new PdfContentDocument(new[] { page }), stream);

        pageCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPagesToPng_PaintsPatternForegroundOverBackgroundAndKeepsOutline()
    {
        var pattern = PdfPatternFill.FromPreset(
            "pct10",
            new PdfColor(0xC0, 0x00, 0x00),
            new PdfColor(0xFF, 0xFF, 0xFF));
        var page = new PdfContentPage(120, 80, new PdfDrawOp[]
        {
            new PdfFillRectPattern(10, 20, 80, 40, pattern),
            new PdfStrokeRect(10, 20, 80, 40, PdfColor.Black, 2, new PdfDashPattern([3, 2])),
        });

        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(new PdfContentDocument([page])).Single());
        var redPixels = 0;
        var whitePixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red > 120 && pixel.Green < 100 && pixel.Blue < 100)
                redPixels++;
            if (pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245)
                whitePixels++;
        }

        redPixels.Should().BeGreaterThan(20);
        whitePixels.Should().BeGreaterThan(20);
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
