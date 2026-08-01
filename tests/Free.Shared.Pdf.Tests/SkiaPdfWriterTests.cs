using FluentAssertions;
using Free.Shared.Pdf.Skia;
using SkiaSharp;
using System.Text;

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

    [Fact]
    public void Write_EmitsExternalLinkAnnotation()
    {
        var page = new PdfContentPage(
            100,
            80,
            [new PdfText(10, 60, 10, PdfFontFace.Regular, PdfColor.Black, "Link")],
            [new PdfLinkOverlay(10, 10, 40, 20, "https://example.com")]);

        var bytes = SkiaPdfWriter.WriteToBytes(new PdfContentDocument([page]));
        var pdf = Encoding.Latin1.GetString(bytes);

        pdf.Should().Contain("/Subtype /Link");
        pdf.Should().Contain("/URI (https://example.com)");
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
    public void RenderPagesToPng_PaintsEffectLayersOutsideSourceBounds()
    {
        var source = new PdfDrawOp[]
        {
            new PdfFillRect(40, 50, 30, 20, new PdfColor(0x20, 0x80, 0xC0)),
        };
        var page = new PdfContentPage(120, 100, new PdfDrawOp[]
        {
            new PdfEffectGroup(PdfEffectKind.Shadow, 40, 50, 30, 20,
                new PdfEffectParameters(new PdfColor(0, 0, 0), 0.75, 6, 8, -5), source),
            new PdfEffectGroup(PdfEffectKind.Glow, 40, 50, 30, 20,
                new PdfEffectParameters(new PdfColor(0xFF, 0x80, 0), 0.75, 8), source),
            new PdfEffectGroup(PdfEffectKind.Reflection, 40, 50, 30, 20,
                new PdfEffectParameters(null, 0.5, 0, ReflectionGap: 5), source),
            new PdfEffectGroup(PdfEffectKind.Bevel, 40, 50, 30, 20,
                new PdfEffectParameters(new PdfColor(0xE0, 0xE8, 0xFF), 0.8, 4,
                    SecondaryColor: new PdfColor(0x40, 0x40, 0x40)), source),
            source[0],
        });

        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(
            new PdfContentDocument([page])).Single());
        var outsidePixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var outsideSource = x < 48 || x >= 94 || y >= 68;
            var pixel = bitmap.GetPixel(x, y);
            if (outsideSource && pixel != SKColors.White)
                outsidePixels++;
        }

        outsidePixels.Should().BeGreaterThan(40);
    }

    [Fact]
    public void RenderPagesToPng_BevelIsVisibleAndBoundedToDirectionalBands()
    {
        var group = new PdfEffectGroup(
            PdfEffectKind.Bevel,
            30,
            30,
            40,
            30,
            new PdfEffectParameters(
                new PdfColor(0xFF, 0x20, 0x20),
                1,
                1,
                SecondaryColor: new PdfColor(0x20, 0x20, 0xFF),
                BevelWidth: 4,
                BevelHeight: 6),
            [new PdfFillRect(30, 30, 40, 30, new PdfColor(0x40, 0x40, 0x40))]);
        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(
            new PdfContentDocument([new PdfContentPage(120, 100, [group])])).Single());

        var insidePixels = 0;
        var outsidePixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var colored = pixel != SKColors.White;
            var inside = x >= 40 && x < 94 && y >= 53 && y < 94;
            if (colored && inside)
                insidePixels++;
            else if (colored && !inside)
                outsidePixels++;
        }

        insidePixels.Should().BeGreaterThan(100);
        outsidePixels.Should().Be(0);
    }

    [Fact]
    public void ReflectionSkew_ConvertsOfficeDegreesToSkiaFactors()
    {
        const double degrees = 9;
        var expected = Math.Tan(degrees * Math.PI / 180d);

        ((double)SkiaPdfWriter.ToSkewFactor(degrees)).Should().BeApproximately(expected, 0.000001);
        Math.Abs(SkiaPdfWriter.ToSkewFactor(degrees) - degrees).Should().BeGreaterThan(0.01,
            "SKCanvas.Skew consumes tangent factors, not the Office degree value");
    }

    [Fact]
    public void RenderPagesToPng_UsesTrueBlurAndReflectionFadeWithOfficeTransformParameters()
    {
        var page = new PdfContentPage(140, 110, new PdfDrawOp[]
        {
            new PdfEffectGroup(
                PdfEffectKind.SoftEdge,
                20,
                45,
                30,
                20,
                new PdfEffectParameters(null, 1, 12),
                [new PdfFillRect(20, 45, 30, 20, new PdfColor(0xD0, 0x30, 0x30))]),
            new PdfEffectGroup(
                PdfEffectKind.Reflection,
                78,
                50,
                24,
                16,
                new PdfEffectParameters(
                    null,
                    0.8,
                    0,
                    ReflectionGap: 4,
                    ReflectionDirectionDegrees: 90,
                    ReflectionEndOpacity: 0,
                    ReflectionFadeDirectionDegrees: 90,
                    ReflectionScaleX: 0.82,
                    ReflectionScaleY: -0.96,
                    ReflectionSkewXDegrees: 9),
                [new PdfFillRect(78, 50, 24, 16, new PdfColor(0x20, 0x70, 0xD0))]),
        });

        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(new PdfContentDocument([page])).Single());

        var nearSoftEdge = bitmap.GetPixel(24, 60);
        var farSoftEdge = bitmap.GetPixel(10, 60);
        (nearSoftEdge.Red - nearSoftEdge.Green).Should().BeGreaterThan(5,
            "Skia soft-edge should produce a colored, partially blurred perimeter outside the source bounds");
        (farSoftEdge.Red - farSoftEdge.Green).Should().BeLessThan(3,
            "the blur should remain bounded rather than painting a distant silhouette");

        static int BlueStrength(SKColor pixel) => pixel.Blue - Math.Max(pixel.Red, pixel.Green);
        var nearReflection = 0;
        var farReflection = 0;
        for (var x = 70; x < bitmap.Width; x++)
        {
            nearReflection += Math.Max(0, BlueStrength(bitmap.GetPixel(x, 86)));
            farReflection += Math.Max(0, BlueStrength(bitmap.GetPixel(x, 108)));
        }

        nearReflection.Should().BeGreaterThan(farReflection,
            "reflection alpha should fade away from the object while retaining its skew/direction transform");
        nearReflection.Should().BeGreaterThan(20);

        var expectedFootprint = 0;
        for (var x = 100; x <= 135; x++)
            expectedFootprint += Math.Max(0, BlueStrength(bitmap.GetPixel(x, 86)));
        expectedFootprint.Should().BeGreaterThan(20,
            "a 9-degree Office skew should keep the reflected footprint near the transformed object; passing 9 as a Skia factor moves it to a distant pixel range");
    }

    [Fact]
    public void RenderPagesToPng_EffectOverridesReplacePatternAndGradientPaint()
    {
        var overrideColor = new PdfColor(0xFF, 0x00, 0x00);
        var gradient = new PdfLinearGradient(
            100, 20, 130, 40,
            [
                new PdfGradientStop(0, new PdfColor(0x00, 0xFF, 0x00)),
                new PdfGradientStop(1, new PdfColor(0x00, 0x00, 0xFF)),
            ]);
        var contour = new PdfPathContour(
            new PdfPathPoint(10, 10),
            [
                PdfPathSegment.LineTo(new PdfPathPoint(35, 10)),
                PdfPathSegment.LineTo(new PdfPathPoint(35, 26)),
                PdfPathSegment.LineTo(new PdfPathPoint(10, 26)),
            ],
            Closed: true);
        var gradientFillContour = new PdfPathContour(
            new PdfPathPoint(100, 25),
            [
                PdfPathSegment.LineTo(new PdfPathPoint(130, 25)),
                PdfPathSegment.LineTo(new PdfPathPoint(130, 45)),
                PdfPathSegment.LineTo(new PdfPathPoint(100, 45)),
            ],
            Closed: true);
        var gradientStrokeContour = new PdfPathContour(
            new PdfPathPoint(100, 50),
            [
                PdfPathSegment.LineTo(new PdfPathPoint(130, 50)),
                PdfPathSegment.LineTo(new PdfPathPoint(130, 70)),
                PdfPathSegment.LineTo(new PdfPathPoint(100, 70)),
            ],
            Closed: true);
        var source = new PdfDrawOp[]
        {
            new PdfFillRectPattern(10, 30, 25, 16,
                PdfPatternFill.FromPreset("cross", new PdfColor(0x00, 0xFF, 0x00), new PdfColor(0x00, 0x00, 0xFF))),
        };
        var ellipseSource = new PdfDrawOp[]
        {
            new PdfFillEllipsePattern(10, 55, 25, 16,
                PdfPatternFill.FromPreset("dotGrid", new PdfColor(0x00, 0xFF, 0x00), new PdfColor(0x00, 0x00, 0xFF))),
        };
        var pathPatternSource = new PdfDrawOp[]
        {
            new PdfPathPattern([contour],
                PdfPatternFill.FromPreset("diagStripe", new PdfColor(0x00, 0xFF, 0x00), new PdfColor(0x00, 0x00, 0xFF)),
                null, 0),
        };
        var pathGradientFillSource = new PdfDrawOp[]
        {
            new PdfPathLinearGradient([gradientFillContour], gradient, null, null, null, 0),
        };
        var pathGradientStrokeSource = new PdfDrawOp[]
        {
            new PdfPathLinearGradient([gradientStrokeContour], null, null, gradient, null, 4),
        };
        var page = new PdfContentPage(200, 100, new PdfDrawOp[]
        {
            new PdfEffectGroup(PdfEffectKind.Shadow, 10, 30, 25, 16,
                new PdfEffectParameters(overrideColor, 1, 0, 35), source),
            new PdfEffectGroup(PdfEffectKind.Shadow, 10, 55, 25, 16,
                new PdfEffectParameters(overrideColor, 1, 0, 35), ellipseSource),
            new PdfEffectGroup(PdfEffectKind.Shadow, 10, 10, 25, 16,
                new PdfEffectParameters(overrideColor, 1, 0, 35), pathPatternSource),
            new PdfEffectGroup(PdfEffectKind.Shadow, 100, 25, 30, 20,
                new PdfEffectParameters(overrideColor, 1, 0, 35), pathGradientFillSource),
            new PdfEffectGroup(PdfEffectKind.Shadow, 100, 50, 30, 20,
                new PdfEffectParameters(overrideColor, 1, 0, 35), pathGradientStrokeSource),
        });

        using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(
            new PdfContentDocument([page])).Single());

        static int CountRed(SKBitmap bitmap, int left, int top, int right, int bottom)
        {
            var count = 0;
            for (var y = Math.Max(0, top); y < Math.Min(bitmap.Height, bottom); y++)
            for (var x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 220 && pixel.Green < 40 && pixel.Blue < 40)
                    count++;
            }
            return count;
        }

        // Bounds are converted from the PDF points used above to the 96-DPI raster.
        CountRed(bitmap, 60, 72, 95, 98).Should().BeGreaterThan(20, "rectangle pattern effect should use the override color");
        CountRed(bitmap, 60, 30, 95, 58).Should().BeGreaterThan(20, "ellipse pattern effect should use the override color");
        CountRed(bitmap, 60, 98, 95, 125).Should().BeGreaterThan(20, "path pattern effect should use the override color");
        CountRed(bitmap, 180, 78, 225, 115).Should().BeGreaterThan(20, "path gradient fill effect should use the override color");
        CountRed(bitmap, 180, 45, 225, 80).Should().BeGreaterThan(10, "path gradient stroke effect should use the override color");
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
    public void RenderPagesToPng_UsesItalicAndBoldItalicTypefaces()
    {
        var faces = new[]
        {
            PdfFontFace.Regular,
            PdfFontFace.Bold,
            PdfFontFace.Italic,
            PdfFontFace.BoldItalic,
        };
        var pages = faces
            .Select(face => new PdfContentPage(
                180,
                70,
                [new PdfText(12, 28, 30, face, PdfColor.Black, "Styled text")]))
            .ToArray();

        var pngs = SkiaPdfWriter.RenderPagesToPng(new PdfContentDocument(pages));

        pngs.Should().HaveCount(4);
        pngs[2].Should().NotEqual(pngs[0], "italic text must not use the upright regular face");
        pngs[3].Should().NotEqual(pngs[1], "bold italic text must not use the upright bold face");
    }

    [Fact]
    public void RenderPagesToPng_UsesAuthoredFontFamilies()
    {
        var pages = new[]
        {
            new PdfContentPage(
                220,
                70,
                [new PdfText(12, 28, 30, PdfFontFace.Regular, PdfColor.Black, "Family sample", "Arial")]),
            new PdfContentPage(
                220,
                70,
                [new PdfText(12, 28, 30, PdfFontFace.Regular, PdfColor.Black, "Family sample", "Courier New")]),
        };

        var pngs = SkiaPdfWriter.RenderPagesToPng(new PdfContentDocument(pages));

        pngs.Should().HaveCount(2);
        pngs[1].Should().NotEqual(pngs[0], "authored font families must reach Skia's embedded-font path");
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
