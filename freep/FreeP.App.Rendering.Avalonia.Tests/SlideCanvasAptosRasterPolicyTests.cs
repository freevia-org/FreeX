using FreeP.App.Rendering.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SlideCanvasAptosRasterPolicyTests
{
    [Fact]
    public void FixedSizeAptosBodyFallback_UsesWave185HostCalibration()
    {
        SlideCanvas.FixedSizeAptosBodyFontScale.Should().Be(0.930);
    }

    [Fact]
    public void ImportedIncreasingCircleText_UsesDedicatedAptosCalibrationOnly()
    {
        SlideCanvas.ImportedIncreasingCircleAptosFontScale.Should().Be(0.930);
        SlideCanvas.ImportedIncreasingCircleAptosOriginOffsetY.Should().Be(-4.0);
        SlideCanvas.UsesImportedIncreasingCircleAptosText(CreateLayout(1)).Should().BeTrue();
        SlideCanvas.UsesImportedIncreasingCircleAptosText(
            CreateLayout(1, fontFamily: "Aptos Display")).Should().BeFalse();
        SlideCanvas.UsesImportedIncreasingCircleAptosText(
            CreateLayout(1, fontFamily: "Calibri")).Should().BeFalse();
    }

    [Fact]
    public void ImportedIncreasingCircleCalibration_RequiresMeasuredSourceSignature()
    {
        var measured = CreateImportedLayout();
        var measuredBounds = CreateImportedBounds();

        SlideCanvas.ResolveImportedIncreasingCircleAptosOriginOffsetY(
                true,
                measured,
                measuredBounds)
            .Should().Be(SlideCanvas.ImportedIncreasingCircleAptosOriginOffsetY);
        SlideCanvas.ResolveImportedIncreasingCircleAptosOriginOffsetY(
                false,
                measured,
                measuredBounds)
            .Should().Be(0.0);
        SlideCanvas.ResolveImportedIncreasingCircleAptosOriginOffsetY(
                true,
                CreateImportedLayout(anchor: VerticalAnchor.Bottom),
                CreateImportedBounds(shortFrame: true))
            .Should().Be(SlideCanvas.ImportedIncreasingCircleAptosOriginOffsetY);
    }

    [Fact]
    public void ImportedIncreasingCircleCalibration_RejectsUnmeasuredLayoutVariants()
    {
        var measuredBounds = CreateImportedBounds();

        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(fontSizePt: 42.0),
            measuredBounds).Should().BeFalse("the Office evidence is for 44 pt text");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(autoFitKind: TextAutoFitKind.Normal),
            measuredBounds).Should().BeFalse("the source uses noAutofit");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(autoFitKind: TextAutoFitKind.Shape),
            measuredBounds).Should().BeFalse("the source uses noAutofit");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(bulletKind: BulletKind.Char),
            measuredBounds).Should().BeFalse("the source uses buNone");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(hasRunEffect: true),
            measuredBounds).Should().BeFalse("the source run has no glyph effects");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(hasBodyEffect: true),
            measuredBounds).Should().BeFalse("the source body has no 3-D effects");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(lineSpacingPercent: 90.0),
            measuredBounds).Should().BeFalse(
                "the current compositor leaves the source line-spacing token unresolved");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(),
            CreateImportedBounds(widthDelta: 1.0)).Should().BeFalse(
                "the measured frame width is part of the source geometry");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(anchor: VerticalAnchor.Bottom),
            measuredBounds).Should().BeFalse(
                "the measured bottom anchor belongs to the short source frame");
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(fontFamily: "Calibri"),
            measuredBounds).Should().BeFalse();
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(includeMixedFontRun: true),
            measuredBounds).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x000000)]
    [InlineData(0x4472C4)]
    public void ImportedIncreasingCircleCalibration_RejectsResolvedTextColorVariants(int textColorRgb)
    {
        SlideCanvas.UsesImportedIncreasingCircleAptosCalibration(
            true,
            CreateImportedLayout(textColor: SrgbColor.FromRgb(textColorRgb)),
            CreateImportedBounds()).Should().BeFalse(
                "the measured Office cache resolves its lt1 font reference to white text");
    }

    [Fact]
    public void UsesFixedSizeAptosBodyFallback_MatchesSemanticRenderingRoute()
    {
        SlideCanvas.UsesFixedSizeAptosBodyFallback(CreateLayout(8)).Should().BeTrue();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(2, bold: true)).Should().BeTrue();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, TextAutoFitKind.Normal)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, bulletKind: BulletKind.Char)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontFamily: "Calibri")).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontSizePt: 24.0)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, columnCount: 2)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontFamily: "Aptos Display")).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(
            CreateLayout(8, fontSizePt: 18.01)).Should().BeFalse();
        SlideCanvas.UsesFixedSizeAptosBodyFallback(CreateLayout(0)).Should().BeFalse();
    }

    private static ResolvedTextLayout CreateLayout(
        int paragraphCount,
        TextAutoFitKind autoFitKind = TextAutoFitKind.None,
        BulletKind bulletKind = BulletKind.None,
        string fontFamily = "Aptos",
        double fontSizePt = 18.0,
        bool bold = false,
        int columnCount = 1) =>
        new()
        {
            AutoFitKind = autoFitKind,
            ColumnCount = columnCount,
            Paragraphs = Enumerable.Range(0, paragraphCount)
                .Select(_ => new ResolvedParagraph
                {
                    Runs = new[]
                    {
                        new ResolvedRun
                        {
                            Text = "Office body",
                            FontFamily = fontFamily,
                            FontSizePt = fontSizePt,
                            Bold = bold,
                            Color = SrgbColor.Black
                        }
                    },
                    BulletKind = bulletKind
                })
                .ToArray()
        };

    private static ResolvedTextLayout CreateImportedLayout(
        string fontFamily = "Aptos",
        double fontSizePt = 44.0,
        TextAutoFitKind autoFitKind = TextAutoFitKind.None,
        BulletKind bulletKind = BulletKind.None,
        bool hasRunEffect = false,
        bool hasBodyEffect = false,
        bool includeMixedFontRun = false,
        VerticalAnchor anchor = VerticalAnchor.Top,
        double? lineSpacingPercent = null,
        SrgbColor? textColor = null) =>
        new()
        {
            Anchor = anchor,
            InsetLeftDip = DrawingMlCoordinateUnits.EmuToPixels(111_760),
            InsetTopDip = DrawingMlCoordinateUnits.EmuToPixels(111_760),
            InsetRightDip = DrawingMlCoordinateUnits.EmuToPixels(111_760),
            InsetBottomDip = DrawingMlCoordinateUnits.EmuToPixels(111_760),
            Wrap = true,
            AutoFitKind = autoFitKind,
            FontScale = 1.0,
            LnSpcReduction = 0.0,
            ColumnCount = 1,
            ColumnSpacingDip = DrawingMlCoordinateUnits.EmuToPixels(1_270),
            Text3dEffects = hasBodyEffect
                ? new ResolvedShapeEffects { HasGlow = true, GlowRadiusDip = 1.0 }
                : null,
            Paragraphs =
            [
                new ResolvedParagraph
                {
                    Align = TextAlign.Left,
                    BulletKind = bulletKind,
                    BulletChar = bulletKind == BulletKind.Char ? "\u2022" : null,
                    BulletText = bulletKind == BulletKind.Char ? "\u2022" : string.Empty,
                    LineSpacingPercent = lineSpacingPercent,
                    Runs = includeMixedFontRun
                        ? [
                            new ResolvedRun
                            {
                                Text = "Measured",
                                FontFamily = fontFamily,
                                FontSizePt = fontSizePt,
                                Color = textColor ?? SrgbColor.White,
                                TextShadow = hasRunEffect
                                    ? new ResolvedRunShadow
                                    {
                                        Color = SrgbColor.Black,
                                        Alpha = 128,
                                        BlurDip = 1.0
                                    }
                                    : null
                            },
                            new ResolvedRun
                            {
                                Text = " variant",
                                FontFamily = "Calibri",
                                FontSizePt = fontSizePt,
                                Color = textColor ?? SrgbColor.White
                            }
                        ]
                        : [
                            new ResolvedRun
                            {
                                Text = "Measured layout",
                                FontFamily = fontFamily,
                                FontSizePt = fontSizePt,
                                Color = textColor ?? SrgbColor.White,
                                TextShadow = hasRunEffect
                                    ? new ResolvedRunShadow
                                    {
                                        Color = SrgbColor.Black,
                                        Alpha = 128,
                                        BlurDip = 1.0
                                    }
                                    : null
                            }
                        ]
                }
            ]
        };

    private static LayoutRect CreateImportedBounds(
        bool shortFrame = false,
        double widthDelta = 0.0) =>
        new(
            0,
            0,
            DrawingMlCoordinateUnits.EmuToPixels(2_500_245) + widthDelta,
            DrawingMlCoordinateUnits.EmuToPixels(
                shortFrame
                    ? 845_153
                    : 3_556_686));
}
