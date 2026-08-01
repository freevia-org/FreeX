using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

public sealed class PortablePdfWriterTests
{
    [Fact]
    public void Write_EmitsValidPdfWithTextFillAndStroke()
    {
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfFillRect(36, 700, 100, 22, new PdfColor(238, 242, 247)),
            new PdfStrokeRect(36, 700, 100, 22, new PdfColor(196, 202, 210), 0.5),
            new PdfText(40, 706, 12, PdfFontFace.Bold, PdfColor.Black, "Hello"),
        });
        var document = new PdfContentDocument(new[] { page });

        var bytes = PortablePdfWriter.WriteToBytes(document);

        var pdf = Encoding.ASCII.GetString(bytes);
        pdf.Should().StartWith("%PDF-1.7");
        pdf.Should().Contain("/Type /Catalog");
        pdf.Should().Contain("/Encoding /WinAnsiEncoding");
        pdf.Should().Contain("(Hello) Tj");
        pdf.Should().Contain("100 22 re f");
        pdf.Should().Contain("0.5 w");
        pdf.Should().Contain("xref");
        pdf.Should().EndWith("%%EOF\n");
    }

    [Fact]
    public void Write_EncodesWinAnsiTextAsHex()
    {
        // C=43 a=61 f=66 e-acute(é)=E9 space=20 euro(€)=80
        var text = "Café €";
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, text),
        });

        var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));

        // C=43 a=61 f=66 é=E9 space=20 €=80
        var pdf = Encoding.ASCII.GetString(bytes);
        pdf.Should().Contain("<436166E92080> Tj");
    }

    [Fact]
    public void Write_ThrowsForNonWinAnsiText()
    {
        var text = "Київ"; // Kyiv (Cyrillic)
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, text),
        });

        var act = () => PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Portable PDF export currently supports ASCII and WinAnsi text only;*");
    }

    [Fact]
    public void Write_SupportsMultiplePagesWithDifferentSizes()
    {
        var pages = new[]
        {
            new PdfContentPage(612, 792, new PdfDrawOp[] { new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "P1") }),
            new PdfContentPage(842, 595, new PdfDrawOp[] { new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "P2") }),
        };

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(pages)));

        pdf.Should().Contain("/Count 2");
        pdf.Should().Contain("MediaBox [0 0 612 792]");
        pdf.Should().Contain("MediaBox [0 0 842 595]");
    }

    [Fact]
    public void Write_EmitsExternalLinkAnnotationWithClippedTopLeftGeometry()
    {
        var page = new PdfContentPage(
            100,
            80,
            [new PdfText(10, 60, 10, PdfFontFace.Regular, PdfColor.Black, "Link")],
            [new PdfLinkOverlay(-5, 10, 45, 20, "https://example.com/a(b)", "Open link")]);

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])));

        pdf.Should().Contain("/Annots [");
        pdf.Should().Contain("/Type /Annot /Subtype /Link");
        pdf.Should().Contain("/Rect [0 50 40 70]");
        pdf.Should().Contain("/Border [0 0 0]");
        pdf.Should().Contain("/Contents (Open link)");
        pdf.Should().Contain("/A << /S /URI /URI (https://example.com/a\\(b\\)) >>");
    }

    [Fact]
    public void Write_EmitsInternalLinkToCrossPageNamedDestination()
    {
        var pages = new[]
        {
            new PdfContentPage(
                100,
                80,
                [new PdfText(10, 60, 10, PdfFontFace.Regular, PdfColor.Black, "Jump")],
                [new PdfLinkOverlay(10, 10, 40, 20, null, "Jump to target", "Target1")]),
            new PdfContentPage(
                100,
                80,
                [new PdfText(12, 20, 10, PdfFontFace.Regular, PdfColor.Black, "Target")],
                NamedDestinations: [new PdfNamedDestination("Target1", 12, 20)]),
        };

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(pages)));

        pdf.Should().Contain("/Type /Annot /Subtype /Link");
        pdf.Should().Contain("/Contents (Jump to target)");
        pdf.Should().Contain("/Dest [");
        pdf.Should().Contain("/XYZ 12 60 null]");
        pdf.Should().NotContain("/S /URI");
    }

    [Fact]
    public void Write_EmitsPdfLineAsMoveThenLineStroke()
    {
        // PdfLine should emit a PDF path: m (moveto), l (lineto), S (stroke).
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfLine(36, 700, 576, 700, new PdfColor(180, 185, 190), 0.4),
        });

        var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));
        var pdf   = Encoding.ASCII.GetString(bytes);

        pdf.Should().Contain("36 700 m",   "PdfLine must emit PDF moveto at (x1, y1)");
        pdf.Should().Contain("576 700 l S","PdfLine must emit lineto then stroke");
        pdf.Should().Contain("0.4 w",      "PdfLine must emit the specified line width");
    }

    [Fact]
    public void Write_EmitsDashPatternAndCenteredFlipTransform()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                50,
                40,
                0,
                [new PdfStrokeRect(
                    20,
                    30,
                    40,
                    20,
                    PdfColor.Black,
                    1,
                    new PdfDashPattern([4, 3]))],
                FlipH: true),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])));

        pdf.Should().Contain("-1 0 0 1 100 0 cm");
        pdf.Should().Contain("[4 3] 0 d");
    }

    [Fact]
    public void Write_EmitsReusedTiledPatternForShapeFillsAndPathOutlines()
    {
        var pattern = PdfPatternFill.FromPreset(
            "pct10",
            new PdfColor(0xC0, 0x00, 0x00),
            new PdfColor(0xFF, 0xFF, 0xFF));
        var path = new PdfPathPattern(
            [new PdfPathContour(
                new PdfPathPoint(70, 10),
                [
                    PdfPathSegment.LineTo(new PdfPathPoint(90, 10)),
                    PdfPathSegment.LineTo(new PdfPathPoint(90, 30)),
                ],
                Closed: true)],
            pattern,
            new PdfColor(0x00, 0x00, 0x00),
            1.5,
            new PdfDashPattern([2, 1]));
        var page = new PdfContentPage(120, 80, new PdfDrawOp[]
        {
            new PdfFillRectPattern(10, 20, 40, 30, pattern),
            new PdfFillEllipsePattern(55, 20, 30, 20, pattern),
            path,
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("/PatternType 1");
        pdf.Should().Contain("/Pattern cs\n/P1 scn");
        pdf.Should().Contain("[2 1] 0 d");
        pdf.Split("/PatternType 1", StringSplitOptions.None).Should().HaveCount(2, "the shared tile must be emitted once and reused");
        pdf.Should().Contain("0.753 0 0 RG");
        pdf.Should().Contain("1 1 1 rg");
    }

    [Theory]
    [InlineData("pct10", PdfPatternKind.Horizontal)]
    [InlineData("pct50", PdfPatternKind.DownDiagonal)]
    [InlineData("pct90", PdfPatternKind.Dot)]
    [InlineData("horzBrick", PdfPatternKind.Brick)]
    [InlineData("diagCross", PdfPatternKind.DiagonalCross)]
    public void PatternPresetMapping_FollowsWpfVisualFamilies(string preset, PdfPatternKind expected)
    {
        PdfPatternFill.FromPreset(preset, PdfColor.Black, new PdfColor(0xFF, 0xFF, 0xFF)).Kind.Should().Be(expected);
    }

    [Fact]
    public void Write_AppliesCenteredRotationAndFlipToPatternFill()
    {
        var pattern = PdfPatternFill.FromPreset("pct50", PdfColor.Black, new PdfColor(0xFF, 0xFF, 0xFF));
        var page = new PdfContentPage(160, 100, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                80,
                50,
                90,
                [new PdfFillRectPattern(60, 40, 40, 20, pattern)],
                FlipH: true),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("0 1 1 0 30 -30 cm");
        pdf.Should().Contain("/Pattern cs\n/P1 scn");
    }

    [Fact]
    public void Write_PdfLineRoundTripsCorrectCoordinates()
    {
        var page = new PdfContentPage(612, 792, new PdfDrawOp[]
        {
            new PdfLine(10, 20, 100, 200, new PdfColor(0, 0, 0), 1.0),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        // Coordinates must appear verbatim in the content stream.
        pdf.Should().Contain("10 20 m",    "moveto x1 y1");
        pdf.Should().Contain("100 200 l S","lineto x2 y2 then stroke");
    }

    [Fact]
    public void Write_EmitsFilledTrianglePath()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFilledTriangle(20, 30, 12, 25, 12, 35, new PdfColor(0x11, 0x22, 0x33)),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("20 30 m");
        pdf.Should().Contain("12 25 l");
        pdf.Should().Contain("12 35 l f");
    }

    [Fact]
    public void Write_EmitsFilledAndStrokedCustomPath()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfPath(
                [
                    new PdfPathContour(
                        new PdfPathPoint(10, 20),
                        [
                            PdfPathSegment.LineTo(new PdfPathPoint(30, 20)),
                            PdfPathSegment.BezierTo(
                                new PdfPathPoint(35, 35),
                                new PdfPathPoint(20, 45),
                                new PdfPathPoint(10, 40)),
                        ],
                        Closed: true),
                ],
                new PdfColor(0x11, 0x22, 0x33),
                new PdfColor(0x44, 0x55, 0x66),
                1.25),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("0.267 0.333 0.4 RG");
        pdf.Should().Contain("1.25 w");
        pdf.Should().Contain("10 20 m\n30 20 l\n35 35 20 45 10 40 c\nh\nB");
    }

    [Fact]
    public void Write_EmitsLinearGradientPatternResourcesForFillAndStrokeOps()
    {
        var gradient = new PdfLinearGradient(
            10,
            20,
            90,
            20,
            [
                new PdfGradientStop(0, new PdfColor(0x11, 0x22, 0x33)),
                new PdfGradientStop(1, new PdfColor(0xAA, 0xBB, 0xCC)),
            ]);
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillRectLinearGradient(10, 20, 50, 20, gradient, new PdfColor(0x11, 0x22, 0x33)),
            new PdfStrokeRectLinearGradient(10, 20, 50, 20, gradient, new PdfColor(0x11, 0x22, 0x33), 1.5),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("/Pattern << /P1 ");
        pdf.Should().Contain("/ShadingType 2");
        pdf.Should().Contain("/Coords [10 20 90 20]");
        pdf.Should().Contain("/C0 [0.067 0.133 0.2]");
        pdf.Should().Contain("/C1 [0.667 0.733 0.8]");
        pdf.Should().Contain("/Pattern cs\n/P1 scn\n10 20 50 20 re f");
        pdf.Should().Contain("/Pattern CS\n/P1 SCN\n1.5 w\n10 20 50 20 re S");
    }

    [Fact]
    public void Write_EmitsMultiStopLinearGradientStitchingFunction()
    {
        var gradient = new PdfLinearGradient(
            0,
            0,
            100,
            0,
            [
                new PdfGradientStop(0, new PdfColor(0x00, 0x00, 0x00)),
                new PdfGradientStop(0.5, new PdfColor(0x80, 0x80, 0x80)),
                new PdfGradientStop(1, new PdfColor(0xFF, 0xFF, 0xFF)),
            ]);
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillRectLinearGradient(0, 0, 100, 80, gradient, PdfColor.Black),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("/FunctionType 3");
        pdf.Should().Contain("/Bounds [0.5]");
        pdf.Should().Contain("/Encode [0 1 0 1]");
    }

    [Fact]
    public void Write_FallsBackToSolidColorForDegenerateLinearGradient()
    {
        var gradient = new PdfLinearGradient(
            10,
            20,
            10,
            20,
            [
                new PdfGradientStop(0, new PdfColor(0x11, 0x22, 0x33)),
                new PdfGradientStop(1, new PdfColor(0xAA, 0xBB, 0xCC)),
            ]);
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillRectLinearGradient(10, 20, 50, 20, gradient, new PdfColor(0x44, 0x55, 0x66)),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().NotContain("/Pattern <<");
        pdf.Should().Contain("0.267 0.333 0.4 rg");
        pdf.Should().Contain("10 20 50 20 re f");
    }

    [Fact]
    public void Write_EmitsFilledAndStrokedEllipsePaths()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfFillEllipse(10, 20, 40, 20, new PdfColor(0x11, 0x22, 0x33)),
            new PdfStrokeEllipse(10, 20, 40, 20, new PdfColor(0x44, 0x55, 0x66), 1.5),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0.067 0.133 0.2 rg");
        pdf.Should().Contain("50 30 m");
        pdf.Should().Contain("50 35.523 41.046 40 30 40 c");
        pdf.Should().Contain("18.954 20 30 20 c");
        pdf.Should().Contain("f");
        pdf.Should().Contain("0.267 0.333 0.4 RG");
        pdf.Should().Contain("1.5 w");
        pdf.Should().Contain("S");
    }

    [Fact]
    public void Write_EmitsRotationGroupSaveTransformAndRestore()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                20,
                20,
                90,
                new PdfDrawOp[]
                {
                    new PdfFillRect(10, 15, 20, 10, new PdfColor(0x11, 0x22, 0x33)),
                    new PdfText(12, 18, 8, PdfFontFace.Bold, PdfColor.Black, "Rotated"),
                }),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("q\n0 -1 1 0 0 40 cm\nq");
        pdf.Should().Contain("10 15 20 10 re f");
        pdf.Should().Contain("(Rotated) Tj");
        pdf.Should().Contain("Q\nendstream", "the grouped content must restore the graphics state before closing the stream");
    }

    [Fact]
    public void Write_EmitsNestedClipAndRotationGroupsWithoutFlatteningChildren()
    {
        var page = new PdfContentPage(160, 120, new PdfDrawOp[]
        {
            new PdfRotationGroup(
                50,
                45,
                23,
                new PdfDrawOp[]
                {
                    new PdfClipGroup(
                        10,
                        20,
                        80,
                        50,
                        new PdfDrawOp[]
                        {
                            new PdfFillRect(0, 0, 140, 90, new PdfColor(0x11, 0x22, 0x33)),
                            new PdfText(12, 28, 8, PdfFontFace.Regular, PdfColor.Black, "Nested"),
                        }),
                }),
        });

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("q\n");
        pdf.Should().Contain("10 20 80 50 re W n");
        pdf.Should().Contain("0 0 140 90 re f");
        pdf.Should().Contain("(Nested) Tj");
        pdf.Should().Contain("Q\nQ\nendstream", "nested groups must each restore their graphics state");
    }

    [Fact]
    public void Write_EmitsPngImageXObjectAndPlacement()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("/XObject << /Im1 ");
        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Width 1 /Height 1");
        pdf.Should().Contain("/ColorSpace /DeviceRGB");
        pdf.Should().Contain("/Filter /FlateDecode");
        pdf.Should().Contain("20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsRotatedImagePlacement()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png", RotationDegrees: 90),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("0 -20 10 0 15 45 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsImageOpacityExtGState()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, MinimalPngBytes(), "image/png", Opacity: 0.5),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("/ExtGState << /GS1 ");
        pdf.Should().Contain("<< /Type /ExtGState /ca 0.5 /CA 0.5 >>");
        pdf.Should().Contain("/GS1 gs\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmitsOpacityGroupExtGStateForVectorOps()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfOpacityGroup(
                0.375,
                new PdfDrawOp[]
                {
                    new PdfFillRect(10, 20, 30, 40, new PdfColor(0x11, 0x22, 0x33)),
                }),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("/ExtGState << /GS1 ");
        pdf.Should().Contain("<< /Type /ExtGState /ca 0.375 /CA 0.375 >>");
        pdf.Should().Contain("/GS1 gs\nq\n0.067 0.133 0.2 rg\n10 20 30 40 re f");
    }

    [Fact]
    public void Write_RendersComposableEffectGroupsWithPaintAndReflectionTransform()
    {
        var source = new PdfDrawOp[] { new PdfFillRect(30, 40, 24, 16, new PdfColor(0x11, 0x22, 0x33)) };
        var effects = new PdfDrawOp[]
        {
            new PdfEffectGroup(PdfEffectKind.Shadow, 30, 40, 24, 16,
                new PdfEffectParameters(new PdfColor(0x20, 0x20, 0x20), 0.5, 4, 3, -3), source),
            new PdfEffectGroup(PdfEffectKind.Glow, 30, 40, 24, 16,
                new PdfEffectParameters(new PdfColor(0x44, 0x72, 0xC4), 0.6, 5), source),
            new PdfEffectGroup(PdfEffectKind.SoftEdge, 30, 40, 24, 16,
                new PdfEffectParameters(null, 0.34, 3), source),
            new PdfEffectGroup(PdfEffectKind.Reflection, 30, 40, 24, 16,
                new PdfEffectParameters(null, 0.38, 0, ReflectionGap: 4), source),
            new PdfEffectGroup(PdfEffectKind.Bevel, 30, 40, 24, 16,
                new PdfEffectParameters(new PdfColor(0xE0, 0xE8, 0xFF), 0.82, 3,
                    SecondaryColor: new PdfColor(0x5C, 0x6B, 0x85)), source),
        };

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(
            new PdfContentDocument([new PdfContentPage(100, 100, effects)]))).Replace("\r\n", "\n");

        pdf.Should().Contain("0.125 rg");
        pdf.Should().Contain("0.267 0.447 0.769 rg");
        pdf.Should().Contain("0.878 0.91 1 rg");
        pdf.Should().Contain("1 0 0 -1");
        pdf.Should().Contain("0.361 0.42 0.522 rg");
    }

    [Fact]
    public void Write_BevelEmitsSeparateDirectionalBandsWithinDeclaredBounds()
    {
        var group = new PdfEffectGroup(
            PdfEffectKind.Bevel,
            10,
            20,
            40,
            30,
            new PdfEffectParameters(
                new PdfColor(0xE0, 0xE8, 0xFF),
                0.8,
                1,
                SecondaryColor: new PdfColor(0x40, 0x40, 0x40),
                BevelWidth: 4,
                BevelHeight: 6),
            [new PdfFillRect(10, 20, 40, 30, new PdfColor(0x20, 0x60, 0xA0))]);
        var bands = PdfRenderGeometry.GetBevelBands(group);
        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(
            new PdfContentDocument([new PdfContentPage(80, 80, [group])])))
            .Replace("\r\n", "\n");

        bands.Should().HaveCount(8);
        bands[0].Points.Should().Equal(
            new PdfPathPoint(10, 50),
            new PdfPathPoint(50, 50),
            new PdfPathPoint(50, 47),
            new PdfPathPoint(10, 47));
        bands[1].Points.Should().Equal(
            new PdfPathPoint(10, 47),
            new PdfPathPoint(50, 47),
            new PdfPathPoint(50, 44),
            new PdfPathPoint(10, 44));
        bands[0].IsHighlight.Should().BeTrue();
        bands[2].IsHighlight.Should().BeFalse();
        bands[4].IsHighlight.Should().BeFalse();
        bands[6].IsHighlight.Should().BeTrue();

        pdf.Split("h W n", StringSplitOptions.None).Should().HaveCount(9);
        pdf.Should().Contain("10 20 40 30 re W n");
        pdf.Should().Contain("1 0 0 1 0 6 cm");
        pdf.Should().Contain("1 0 0 1 4 0 cm");
        pdf.Should().Contain("1 0 0 1 0 -6 cm");
        pdf.Should().Contain("1 0 0 1 -4 0 cm");
        pdf.Should().Contain("0.878 0.91 1 rg");
        pdf.Should().Contain("0.251 0.251 0.251 rg");
        pdf.Should().Contain("<< /Type /ExtGState /ca 0.576 /CA 0.576 >>");
        pdf.Should().Contain("<< /Type /ExtGState /ca 0.352 /CA 0.352 >>");
    }

    [Fact]
    public void Write_BevelBandsComposeWithNestedRotationAndClipGroups()
    {
        var bevel = new PdfEffectGroup(
            PdfEffectKind.Bevel,
            10,
            15,
            40,
            30,
            new PdfEffectParameters(
                new PdfColor(0xFF, 0xFF, 0xFF),
                0.7,
                3,
                SecondaryColor: new PdfColor(0x20, 0x20, 0x20)),
            [new PdfFillRect(10, 15, 40, 30, PdfColor.Black)]);
        var page = new PdfContentPage(100, 80,
        [
            new PdfRotationGroup(
                20,
                20,
                90,
                [new PdfClipGroup(0, 0, 80, 70, [bevel])]),
        ]);

        var pdf = Encoding.ASCII.GetString(PortablePdfWriter.WriteToBytes(
            new PdfContentDocument([page]))).Replace("\r\n", "\n");

        pdf.Should().Contain("q\n0 -1 1 0 0 40 cm\nq\n0 0 80 70 re W n\nq");
        pdf.Should().Contain("10 15 40 30 re W n");
        pdf.Split("h W n", StringSplitOptions.None).Should().HaveCount(9);
    }

    [Fact]
    public void Write_BlurFallbackUsesSymmetricWeightedStamps()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfEffectGroup(
                PdfEffectKind.SoftEdge,
                20,
                25,
                30,
                20,
                new PdfEffectParameters(null, 1, 9),
                [new PdfFillRect(20, 25, 30, 20, new PdfColor(0xD0, 0x30, 0x30))]),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("1 0 0 1 -9 -9 cm",
            "the portable blur kernel should extend in the negative diagonal direction");
        pdf.Should().Contain("1 0 0 1 9 9 cm",
            "the portable blur kernel should extend in the positive diagonal direction");
        pdf.Should().Contain("/ca 0.01 /CA 0.01",
            "blur stamps should use weighted opacity resources instead of repeated opaque silhouettes");
    }

    [Fact]
    public void Write_ReflectionFallbackUsesBoundedFadeBandsAndOfficeTransformParameters()
    {
        var source = new PdfDrawOp[]
        {
            new PdfFillRect(30, 40, 24, 16, new PdfColor(0x11, 0x22, 0x33)),
        };
        var page = new PdfContentPage(100, 100, new PdfDrawOp[]
        {
            new PdfEffectGroup(
                PdfEffectKind.Reflection,
                30,
                40,
                24,
                16,
                new PdfEffectParameters(
                    null,
                    0.72,
                    0,
                    ReflectionGap: 5,
                    ReflectionDirectionDegrees: 127,
                    ReflectionEndOpacity: 0.04,
                    ReflectionStartPosition: 0.1,
                    ReflectionEndPosition: 0.9,
                    ReflectionFadeDirectionDegrees: 68,
                    ReflectionScaleX: 0.78,
                    ReflectionScaleY: -0.92,
                    ReflectionSkewXDegrees: 11),
                source),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument([page])))
            .Replace("\r\n", "\n");

        pdf.Split("h W n", StringSplitOptions.None).Should().HaveCount(13,
            "portable PDF should retain a fine, visibly fading diagonal reflection instead of collapsing it to one opaque pass");
        pdf.Should().Contain(" m\n", "directional reflection bands should be emitted as transformed polygons");
        pdf.Should().Contain("/GS");
        pdf.Should().Contain(" cm");
    }

    [Fact]
    public void Write_AppliesPngImageColorEffectsBeforeEmbedding()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                RgbPngBytes(255, 0, 0),
                "image/png",
                ColorEffects: new PdfImageColorEffects(
                    Grayscale: true,
                    BiLevelThreshold: null,
                    Brightness: null,
                    Contrast: null)),
        });

        var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));
        var pdf = Encoding.Latin1.GetString(bytes);
        var pixels = InflateZlib(ExtractFirstPdfStream(bytes));

        pdf.Should().Contain("/ColorSpace /DeviceRGB");
        pixels.Should().Equal(54, 54, 54);
    }

    [Fact]
    public void PdfImageColorEffectPixels_AppliesBrightnessContrastAndBiLevelInRendererOrder()
    {
        byte[] pixels = [51, 102, 204];

        PdfImageColorEffectPixels.ApplyToRgb24(
            pixels,
            new PdfImageColorEffects(
                Grayscale: false,
                BiLevelThreshold: 0.5,
                Brightness: 0.25,
                Contrast: -0.5));

        pixels.Should().Equal(255, 255, 255);
    }

    [Fact]
    public void Write_EmitsSourceCroppedImagePlacementWithDestinationClip()
    {
        var page = new PdfContentPage(120, 90, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                20,
                80,
                40,
                MinimalJpegBytes(),
                "image/jpeg",
                SourceCrop: new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375)),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("10 20 80 40 re W n");
        pdf.Should().Contain("160 0 0 80 -30 -10 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_ClipsImageToEllipse()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                MinimalPngBytes(),
                "image/png",
                ClipKind: PdfImageClipKind.Ellipse),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("30 35 m");
        pdf.Should().Contain("30 37.761 25.523 40 20 40 c");
        pdf.Should().Contain("W n\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_ClipsImageToRoundedRectangle()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                MinimalPngBytes(),
                "image/png",
                ClipKind: PdfImageClipKind.RoundedRectangle),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain("11.8 30 m");
        pdf.Should().Contain("28.2 30 l");
        pdf.Should().Contain("W n\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Theory]
    [InlineData(PdfImageClipKind.Triangle, "20 40 m\n30 30 l\n10 30 l\nh\nW n")]
    [InlineData(PdfImageClipKind.Diamond, "20 40 m\n30 35 l\n20 30 l\n10 35 l\nh\nW n")]
    [InlineData(PdfImageClipKind.Parallelogram, "15 40 m\n30 40 l\n25 30 l\n10 30 l\nh\nW n")]
    [InlineData(PdfImageClipKind.Hexagon, "15 40 m\n25 40 l\n30 35 l\n25 30 l\n15 30 l\n10 35 l\nh\nW n")]
    [InlineData(PdfImageClipKind.Chevron, "10 40 m\n25 40 l\n30 35 l\n25 30 l\n10 30 l\n15 35 l\nh\nW n")]
    public void Write_ClipsImageToPresetPolygon(PdfImageClipKind clipKind, string expectedPath)
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(
                10,
                30,
                20,
                10,
                MinimalPngBytes(),
                "image/png",
                ClipKind: clipKind),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })))
            .Replace("\r\n", "\n");

        pdf.Should().Contain(expectedPath);
        pdf.Should().Contain("W n\n20 0 0 10 10 30 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_EmbedsJpegImageWithDctDecode()
    {
        var page = new PdfContentPage(120, 90, new PdfDrawOp[]
        {
            new PdfImage(12, 20, 48, 36, MinimalJpegBytes(), "image/jpeg"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Width 16 /Height 16");
        pdf.Should().Contain("/ColorSpace /DeviceRGB");
        pdf.Should().Contain("/Filter /DCTDecode");
        pdf.Should().Contain("48 0 0 36 12 20 cm");
        pdf.Should().Contain("/Im1 Do");
    }

    [Fact]
    public void Write_SkipsUnsupportedImageContentTypes()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, new byte[] { 1, 2, 3, 4 }, "image/gif"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().NotContain("/Subtype /Image");
        pdf.Should().NotContain("/XObject");
        pdf.Should().NotContain("/Im1 Do");
    }

    [Fact]
    public void Write_SkipsMalformedSupportedImageBytes()
    {
        var page = new PdfContentPage(100, 80, new PdfDrawOp[]
        {
            new PdfImage(10, 30, 20, 10, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
            new PdfImage(40, 30, 20, 10, new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
            new PdfText(10, 10, 10, PdfFontFace.Regular, PdfColor.Black, "Still exports"),
        });

        var pdf = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page })));

        pdf.Should().NotContain("/Subtype /Image");
        pdf.Should().NotContain("/XObject");
        pdf.Should().NotContain("/Im1 Do");
        pdf.Should().Contain("(Still exports) Tj");
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static byte[] RgbPngBytes(byte red, byte green, byte blue)
    {
        var bytes = new List<byte>(128);
        bytes.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        AppendPngChunk(bytes, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        AppendPngChunk(bytes, "IDAT", DeflateZlib([0, red, green, blue]));
        AppendPngChunk(bytes, "IEND", []);
        return bytes.ToArray();
    }

    private static void AppendPngChunk(List<byte> target, string type, byte[] data)
    {
        target.AddRange(ToBigEndian(data.Length));
        target.AddRange(Encoding.ASCII.GetBytes(type));
        target.AddRange(data);
        target.AddRange([0, 0, 0, 0]);
    }

    private static byte[] ToBigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] ExtractFirstPdfStream(byte[] pdfBytes)
    {
        var streamMarker = Encoding.ASCII.GetBytes("stream\n");
        var endMarker = Encoding.ASCII.GetBytes("\nendstream");
        var start = IndexOf(pdfBytes, streamMarker, 0);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += streamMarker.Length;
        var end = IndexOf(pdfBytes, endMarker, start);
        end.Should().BeGreaterThan(start);
        return pdfBytes[start..end];
    }

    private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
    {
        for (var i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] == pattern[j])
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return i;
        }

        return -1;
    }

    private static byte[] DeflateZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(data);
        return output.ToArray();
    }

    private static byte[] InflateZlib(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(input, CompressionMode.Decompress))
            stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] MinimalJpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");
}
