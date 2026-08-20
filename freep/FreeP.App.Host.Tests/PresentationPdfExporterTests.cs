using System.Linq;
using System.Text;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public class PresentationPdfExporterTests
{
    private static Presentation SampleDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var s1 = new Slide { Title = "Welcome" };
        var bullet1 = new SlideShape { Kind = SlideShapeKind.AutoShape };
        bullet1.Text = "First bullet";
        s1.Shapes.Add(bullet1);
        var bullet2 = new SlideShape { Kind = SlideShapeKind.AutoShape };
        bullet2.Text = "Second bullet";
        s1.Shapes.Add(bullet2);

        var s2 = new Slide { Title = "Agenda" };
        var multiLine = new SlideShape { Kind = SlideShapeKind.AutoShape };
        multiLine.Text = "Line A\nLine B";
        s2.Shapes.Add(multiLine);

        presentation.Slides.Add(s1);
        presentation.Slides.Add(s2);
        presentation.Properties.Title = "My Deck";
        presentation.Properties.Author = "Tester";
        return presentation;
    }

    [Fact]
    public void ExportToBytes_ProducesValidPdf()
    {
        var bytes = PresentationPdfExporter.ExportToBytes(SampleDeck());

        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(bytes).Should().Contain("%%EOF");
    }

    [Fact]
    public void BuildDocument_OnePagePerSlide()
    {
        PresentationPdfExporter.BuildDocument(SampleDeck()).Pages.Should().HaveCount(2);
    }

    [Fact]
    public void BuildDocument_EmptyPresentation_StillHasOnePage()
    {
        var empty = Presentation.CreateEmpty();
        empty.Slides.Clear();

        PresentationPdfExporter.BuildDocument(empty).Pages.Should().ContainSingle();
    }

    [Fact]
    public void BuildDocument_DrawsTitleAndShapeText()
    {
        var doc = PresentationPdfExporter.BuildDocument(SampleDeck());

        var page1 = doc.Pages[0].Ops.OfType<PdfText>().Select(t => t.Text).ToList();
        page1.Should().Contain("Welcome");
        page1.Should().Contain("First bullet");
        page1.Should().Contain("Second bullet");

        // A multi-line shape's text splits into one text op per line.
        var page2 = doc.Pages[1].Ops.OfType<PdfText>().Select(t => t.Text).ToList();
        page2.Should().Contain("Line A");
        page2.Should().Contain("Line B");
    }

    [Fact]
    public void BuildDocument_EmitsSlideBackgroundAndBasicShapeGeometry()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var slide = new Slide
        {
            Background = new ShapeFill.Solid(SrgbColor.FromRgb(0xF2E6D8)),
        };
        var shape = new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4)),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, widthPt: 1.5),
            Text = "Positioned text",
        };
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfFillRect>().Should().Contain(fill =>
            fill.X == 0 &&
            fill.Y == 0 &&
            fill.Width == 960 &&
            fill.Height == 540 &&
            fill.Color == new PdfColor(0xF2, 0xE6, 0xD8));
        ops.OfType<PdfFillRect>().Should().Contain(fill =>
            fill.X == 72 &&
            fill.Y == 378 &&
            fill.Width == 144 &&
            fill.Height == 72 &&
            fill.Color == new PdfColor(0x44, 0x72, 0xC4));
        ops.OfType<PdfStrokeRect>().Should().Contain(stroke =>
            stroke.X == 72 &&
            stroke.Y == 378 &&
            stroke.Width == 144 &&
            stroke.Height == 72 &&
            stroke.LineWidth == 1.5);
        ops.OfType<PdfText>().Should().Contain(text =>
            text.X == 80 &&
            text.Y == 424 &&
            text.Text == "Positioned text");
    }

    [Fact]
    public void BuildDocument_CarriesShapeFillAndOutlineAlphaAsPdfOpacityGroups()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4), alpha: 128)),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(SrgbColor.FromRgb(0xC00000), alpha: 64),
                widthPt: 1.5),
        });
        deck.Slides.Add(slide);

        var groups = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfOpacityGroup>()
            .ToArray();

        groups.Should().HaveCount(2);
        groups[0].Opacity.Should().BeApproximately(128 / 255.0, 0.0001);
        groups[0].Ops.OfType<PdfFillRect>().Should().ContainSingle(fill =>
            fill.X == 72 &&
            fill.Y == 378 &&
            fill.Color == new PdfColor(0x44, 0x72, 0xC4));
        groups[1].Opacity.Should().BeApproximately(64 / 255.0, 0.0001);
        groups[1].Ops.OfType<PdfStrokeRect>().Should().ContainSingle(stroke =>
            stroke.X == 72 &&
            stroke.Y == 378 &&
            stroke.LineWidth == 1.5 &&
            stroke.Color == new PdfColor(0xC0, 0x00, 0x00));
    }

    [Fact]
    public void BuildDocument_MapsLinearGradientShapeFillAndOutlineToPdfGradientOps()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var fillGradient = new ShapeFill.Gradient(
            [
                new GradientStop(0.0, new ThemeAwareColor(SrgbColor.FromRgb(0x112233))),
                new GradientStop(1.0, new ThemeAwareColor(SrgbColor.FromRgb(0xAABBCC))),
            ],
            GradientKind.Linear,
            angleDegrees: 0);
        var strokeGradient = new ShapeFill.Gradient(
            [
                new GradientStop(0.0, new ThemeAwareColor(SrgbColor.FromRgb(0x445566))),
                new GradientStop(1.0, new ThemeAwareColor(SrgbColor.FromRgb(0xDDEEFF))),
            ],
            GradientKind.Linear,
            angleDegrees: 90);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = fillGradient,
            Outline = new ShapeOutline.GradientVisible(strokeGradient, widthPt: 2.25),
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;
        var fill = ops.OfType<PdfFillRectLinearGradient>().Should().ContainSingle().Subject;
        var stroke = ops.OfType<PdfStrokeRectLinearGradient>().Should().ContainSingle().Subject;

        fill.X.Should().Be(72);
        fill.Y.Should().Be(378);
        fill.FallbackColor.Should().Be(new PdfColor(0x11, 0x22, 0x33));
        fill.Gradient.StartX.Should().Be(72);
        fill.Gradient.StartY.Should().Be(414);
        fill.Gradient.EndX.Should().Be(216);
        fill.Gradient.EndY.Should().Be(414);
        fill.Gradient.Stops.Select(stop => (stop.Position, stop.Color)).Should().Equal(
            (0.0, new PdfColor(0x11, 0x22, 0x33)),
            (1.0, new PdfColor(0xAA, 0xBB, 0xCC)));
        stroke.LineWidth.Should().Be(2.25);
        stroke.FallbackColor.Should().Be(new PdfColor(0x44, 0x55, 0x66));
        stroke.Gradient.StartX.Should().Be(144);
        stroke.Gradient.StartY.Should().Be(450);
        stroke.Gradient.EndX.Should().Be(144);
        stroke.Gradient.EndY.Should().Be(378);
        ops.OfType<PdfFillRect>().Should().BeEmpty();
        ops.OfType<PdfStrokeRect>().Should().BeEmpty();
    }

    [Fact]
    public void BuildDocument_MapsLinearGradientSlideBackgroundToPdfGradientOp()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var slide = new Slide
        {
            Background = new ShapeFill.Gradient(
                [
                    new GradientStop(0.0, new ThemeAwareColor(SrgbColor.FromRgb(0x102030))),
                    new GradientStop(0.5, new ThemeAwareColor(SrgbColor.FromRgb(0x406080))),
                    new GradientStop(1.0, new ThemeAwareColor(SrgbColor.FromRgb(0xDDEEFF))),
                ],
                GradientKind.Linear,
                angleDegrees: 0),
        };
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;
        var background = ops.OfType<PdfFillRectLinearGradient>().Should().ContainSingle().Subject;

        background.X.Should().Be(0);
        background.Y.Should().Be(0);
        background.Width.Should().Be(960);
        background.Height.Should().Be(540);
        background.FallbackColor.Should().Be(new PdfColor(0x10, 0x20, 0x30));
        background.Gradient.StartX.Should().Be(0);
        background.Gradient.StartY.Should().Be(270);
        background.Gradient.EndX.Should().Be(960);
        background.Gradient.EndY.Should().Be(270);
        background.Gradient.Stops.Select(stop => (stop.Position, stop.Color)).Should().Equal(
            (0.0, new PdfColor(0x10, 0x20, 0x30)),
            (0.5, new PdfColor(0x40, 0x60, 0x80)),
            (1.0, new PdfColor(0xDD, 0xEE, 0xFF)));
        ops.OfType<PdfFillRect>().Should().BeEmpty("linear slide backgrounds should not flatten to solid PDF fills");
    }

    [Fact]
    public void BuildDocument_KeepsSolidFallbackForRadialGradientFillAndOutline()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var gradient = new ShapeFill.Gradient(
            [
                new GradientStop(0.0, new ThemeAwareColor(SrgbColor.FromRgb(0x112233))),
                new GradientStop(1.0, new ThemeAwareColor(SrgbColor.FromRgb(0xAABBCC))),
            ],
            GradientKind.Radial,
            angleDegrees: 0);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = gradient,
            Outline = new ShapeOutline.GradientVisible(gradient, widthPt: 1.5),
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfFillRectLinearGradient>().Should().BeEmpty();
        ops.OfType<PdfStrokeRectLinearGradient>().Should().BeEmpty();
        ops.OfType<PdfFillRect>().Should().ContainSingle(fill => fill.Color == new PdfColor(0x11, 0x22, 0x33));
        ops.OfType<PdfStrokeRect>().Should().ContainSingle(stroke =>
            stroke.Color == new PdfColor(0x11, 0x22, 0x33) &&
            stroke.LineWidth == 1.5);
    }

    [Fact]
    public void ExportToBytes_EmitsShapeAlphaExtGStateForVectorGeometry()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        deck.Slides.Add(new Slide
        {
            Shapes =
            {
                new SlideShape
                {
                    OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
                    OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
                    ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
                    ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
                    Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4), alpha: 128)),
                },
            },
        });

        var pdf = Encoding.Latin1.GetString(PresentationPdfExporter.ExportToBytes(deck));

        pdf.Should().Contain("/ExtGState");
        pdf.Should().Contain("/ca 0.502");
        pdf.Should().Contain("/CA 0.502");
        pdf.Should().Contain("/GS1 gs");
    }

    [Fact]
    public void BuildDocument_EmitsPlannerShadowGroupBeforeVectorShapeBody()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4)),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, widthPt: 1.5),
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = SrgbColor.FromRgb(0x222222),
                OuterShadowAlpha = 128,
                OuterShadowDistEmu = DrawingMlCoordinateUnits.PointsToEmu(12),
                OuterShadowDirDeg = 0,
            },
            Text = "Shadowed",
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops.ToList();
        var shadowGroup = ops.OfType<PdfOpacityGroup>().Should().ContainSingle().Subject;
        var shadowFill = shadowGroup.Ops.OfType<PdfFillRect>().Should().ContainSingle().Subject;
        var bodyFill = ops.OfType<PdfFillRect>().Should().ContainSingle().Subject;

        ops.IndexOf(shadowGroup).Should().BeLessThan(ops.IndexOf(bodyFill));
        shadowGroup.Opacity.Should().BeApproximately(128 / 255.0, 0.0001);
        shadowFill.X.Should().Be(84);
        shadowFill.Y.Should().Be(378);
        shadowFill.Width.Should().Be(144);
        shadowFill.Height.Should().Be(72);
        shadowFill.Color.Should().Be(new PdfColor(0x22, 0x22, 0x22));
        shadowGroup.Ops.OfType<PdfStrokeRect>().Should().ContainSingle(stroke =>
            stroke.X == 84 &&
            stroke.Y == 378 &&
            stroke.LineWidth == 1.5 &&
            stroke.Color == new PdfColor(0x22, 0x22, 0x22));
        ops.OfType<PdfText>().Should().ContainSingle(text => text.Text == "Shadowed");
    }

    [Fact]
    public void BuildDocument_EmitsPlannerGlowGroupForVectorShapeBounds()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4)),
            Effects = new ShapeEffects
            {
                HasGlow = true,
                GlowColor = SrgbColor.FromRgb(0x00B0F0),
                GlowAlpha = 120,
                GlowRadiusEmu = DrawingMlCoordinateUnits.PointsToEmu(1.5),
            },
        });
        deck.Slides.Add(slide);

        var glowGroup = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfOpacityGroup>()
            .Should().ContainSingle()
            .Subject;
        var glowStroke = glowGroup.Ops.OfType<PdfStrokeRect>().Should().ContainSingle().Subject;

        glowGroup.Opacity.Should().BeApproximately(120 / 255.0, 0.0001);
        glowStroke.X.Should().Be(72);
        glowStroke.Y.Should().Be(378);
        glowStroke.Width.Should().Be(144);
        glowStroke.Height.Should().Be(72);
        glowStroke.LineWidth.Should().Be(3);
        glowStroke.Color.Should().Be(new PdfColor(0x00, 0xB0, 0xF0));
    }

    [Fact]
    public void BuildDocument_UsesModeledSlideSizeForPageAndGeometry()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        deck.SlideSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(576);
        deck.SlideSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(432);

        var slide = new Slide
        {
            Background = new ShapeFill.Solid(SrgbColor.FromRgb(0xFFFFFF)),
        };
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Text = "Custom size",
        });
        deck.Slides.Add(slide);

        var page = PresentationPdfExporter.BuildDocument(deck).Pages[0];

        page.WidthPoints.Should().Be(576);
        page.HeightPoints.Should().Be(432);
        page.Ops.OfType<PdfFillRect>().Should().Contain(fill =>
            fill.X == 0 &&
            fill.Y == 0 &&
            fill.Width == 576 &&
            fill.Height == 432);
        page.Ops.OfType<PdfStrokeRect>().Should().Contain(stroke =>
            stroke.X == 72 &&
            stroke.Y == 270 &&
            stroke.Width == 144 &&
            stroke.Height == 72);
        page.Ops.OfType<PdfText>().Should().Contain(text =>
            text.X == 80 &&
            text.Y == 316 &&
            text.Text == "Custom size");
    }

    [Fact]
    public void BuildDocument_ExportsLineShapesAsPdfLinesNotRectangleOutlines()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0xC00000), widthPt: 2.25),
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfStrokeRect>().Should().BeEmpty("PowerPoint line/connectors are strokes, not boxed shapes");
        ops.OfType<PdfLine>().Should().ContainSingle(line =>
            line.X1 == 72 &&
            line.Y1 == 450 &&
            line.X2 == 216 &&
            line.Y2 == 378 &&
            line.LineWidth == 2.25 &&
            line.Color == new PdfColor(0xC0, 0x00, 0x00));
        ops.OfType<PdfText>().Select(text => text.Text)
            .Should().NotContain("[Connector]", "textless connector shapes should not export visible fallback labels");
    }

    [Fact]
    public void BuildDocument_ExportsElbowConnectorRouteAsMultiplePdfLines()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.ElbowConnector,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, widthPt: 1.5),
            ElbowRoute =
            [
                (DrawingMlCoordinateUnits.PointsToEmu(72), DrawingMlCoordinateUnits.PointsToEmu(90)),
                (DrawingMlCoordinateUnits.PointsToEmu(144), DrawingMlCoordinateUnits.PointsToEmu(90)),
                (DrawingMlCoordinateUnits.PointsToEmu(144), DrawingMlCoordinateUnits.PointsToEmu(162)),
                (DrawingMlCoordinateUnits.PointsToEmu(216), DrawingMlCoordinateUnits.PointsToEmu(162)),
            ],
        });
        deck.Slides.Add(slide);

        var lines = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops.OfType<PdfLine>().ToArray();

        lines.Should().HaveCount(3);
        lines.Select(line => (line.X1, line.Y1, line.X2, line.Y2)).Should().Equal(
            (72, 450, 144, 450),
            (144, 450, 144, 378),
            (144, 378, 216, 378));
        lines.Should().OnlyContain(line => line.LineWidth == 1.5 && line.Color == PdfColor.Black);
    }

    [Fact]
    public void BuildDocument_ExportsStraightConnectorTriangleArrowheads()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Outline = new ShapeOutline.Visible(
                SrgbColor.FromRgb(0xC00000),
                widthPt: 2.25,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle),
                endLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle)),
        });
        deck.Slides.Add(slide);

        var pageOps = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;
        var line = pageOps.OfType<PdfLine>().Should().ContainSingle().Subject;
        var triangles = pageOps.OfType<PdfFilledTriangle>().ToArray();

        triangles.Should().HaveCount(2);
        triangles[0].X1.Should().Be(line.X1);
        triangles[0].Y1.Should().Be(line.Y1);
        triangles[1].X1.Should().Be(line.X2);
        triangles[1].Y1.Should().Be(line.Y2);
        BaseCenter(triangles[0]).X.Should().BeGreaterThan(triangles[0].X1);
        BaseCenter(triangles[0]).Y.Should().BeLessThan(triangles[0].Y1);
        BaseCenter(triangles[1]).X.Should().BeLessThan(triangles[1].X1);
        BaseCenter(triangles[1]).Y.Should().BeGreaterThan(triangles[1].Y1);
        triangles.Should().OnlyContain(triangle => triangle.Color == new PdfColor(0xC0, 0x00, 0x00));
    }

    [Fact]
    public void BuildDocument_ExportsElbowConnectorTriangleArrowheadsAtRouteEnds()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.ElbowConnector,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Outline = new ShapeOutline.Visible(
                SrgbColor.Black,
                widthPt: 1.5,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle),
                endLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle)),
            ElbowRoute =
            [
                (DrawingMlCoordinateUnits.PointsToEmu(72), DrawingMlCoordinateUnits.PointsToEmu(90)),
                (DrawingMlCoordinateUnits.PointsToEmu(144), DrawingMlCoordinateUnits.PointsToEmu(90)),
                (DrawingMlCoordinateUnits.PointsToEmu(144), DrawingMlCoordinateUnits.PointsToEmu(162)),
                (DrawingMlCoordinateUnits.PointsToEmu(216), DrawingMlCoordinateUnits.PointsToEmu(162)),
            ],
        });
        deck.Slides.Add(slide);

        var triangles = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfFilledTriangle>()
            .ToArray();

        triangles.Should().HaveCount(2);
        (triangles[0].X1, triangles[0].Y1).Should().Be((72, 450));
        BaseCenter(triangles[0]).Should().Be((80, 450));
        (triangles[1].X1, triangles[1].Y1).Should().Be((216, 378));
        BaseCenter(triangles[1]).Should().Be((208, 378));
    }

    [Fact]
    public void BuildDocument_ExportsEllipseShapesAsPdfEllipses()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x2F5597), widthPt: 1.75),
            Text = "Oval callout",
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfFillEllipse>().Should().ContainSingle(fill =>
            fill.X == 72 &&
            fill.Y == 378 &&
            fill.Width == 144 &&
            fill.Height == 72 &&
            fill.Color == new PdfColor(0x70, 0xAD, 0x47));
        ops.OfType<PdfStrokeEllipse>().Should().ContainSingle(stroke =>
            stroke.X == 72 &&
            stroke.Y == 378 &&
            stroke.Width == 144 &&
            stroke.Height == 72 &&
            stroke.Color == new PdfColor(0x2F, 0x55, 0x97) &&
            stroke.LineWidth == 1.75);
        ops.OfType<PdfFillRect>().Should().BeEmpty("ellipse shapes should not flatten to rectangular fill geometry");
        ops.OfType<PdfStrokeRect>().Should().BeEmpty("ellipse shapes should not flatten to rectangular outline geometry");
        ops.OfType<PdfText>().Should().ContainSingle(text =>
            text.X == 80 &&
            text.Y == 424 &&
            text.Text == "Oval callout");
    }

    [Fact]
    public void BuildDocument_ExportsCustomGeometryAsPdfPath()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var path = new CustomGeometryPath { PathW = 100, PathH = 100, Fill = true, Stroke = true };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.CubicBezTo, X: 100, Y: 50, X1: 75, Y1: 100, X2: 50, Y2: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.QuadBezTo, X: 25, Y: 100, X1: 0, Y1: 75));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        var slide = new Slide();
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x2F5597), widthPt: 1.75),
            Text = "Freeform",
        };
        shape.CustomGeometry.Add(path);
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;
        var pdfPath = ops.OfType<PdfPath>().Should().ContainSingle().Subject;
        var contour = pdfPath.Contours.Should().ContainSingle().Subject;

        pdfPath.FillColor.Should().Be(new PdfColor(0x70, 0xAD, 0x47));
        pdfPath.StrokeColor.Should().Be(new PdfColor(0x2F, 0x55, 0x97));
        pdfPath.StrokeWidth.Should().Be(1.75);
        contour.Start.Should().Be(new PdfPathPoint(72, 450));
        contour.Closed.Should().BeTrue();
        contour.Segments.Should().HaveCount(3);
        contour.Segments[0].Should().Be(PdfPathSegment.LineTo(new PdfPathPoint(216, 450)));
        contour.Segments[1].Should().Be(PdfPathSegment.BezierTo(
            new PdfPathPoint(216, 414),
            new PdfPathPoint(180, 378),
            new PdfPathPoint(144, 378)));
        contour.Segments[2].Should().Be(PdfPathSegment.BezierTo(
            new PdfPathPoint(120, 378),
            new PdfPathPoint(96, 384),
            new PdfPathPoint(72, 396)));
        ops.OfType<PdfFillRect>().Should().BeEmpty("custom geometry should not flatten to rectangle fill geometry");
        ops.OfType<PdfStrokeRect>().Should().BeEmpty("custom geometry should not flatten to rectangle outline geometry");
        ops.OfType<PdfText>().Should().ContainSingle(text =>
            text.X == 80 &&
            text.Y == 424 &&
            text.Text == "Freeform");
    }

    [Fact]
    public void BuildDocument_SplitsCustomGeometryFillAndOutlineWhenOpacityDiffers()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var path = new CustomGeometryPath { PathW = 100, PathH = 100, Fill = true, Stroke = true };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        var slide = new Slide();
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x70AD47), alpha: 128)),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(SrgbColor.FromRgb(0x2F5597), alpha: 64),
                widthPt: 1.75)
        };
        shape.CustomGeometry.Add(path);
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var groups = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfOpacityGroup>()
            .ToList();

        groups.Should().HaveCount(2);
        var fillGroup = groups.Single(group => group.Ops.OfType<PdfPath>().Single().FillColor is not null);
        var strokeGroup = groups.Single(group => group.Ops.OfType<PdfPath>().Single().StrokeColor is not null);
        fillGroup.Opacity.Should().BeApproximately(128 / 255.0, 0.0001);
        strokeGroup.Opacity.Should().BeApproximately(64 / 255.0, 0.0001);
    }

    [Fact]
    public void BuildDocument_ExportsCustomGeometryArcAsCubicPdfPath()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var path = new CustomGeometryPath { PathW = 100, PathH = 100, Fill = true, Stroke = false };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.ArcTo, WR: 100, HR: 100, StAng: 0, SwAng: 90));
        var slide = new Slide();
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
        };
        shape.CustomGeometry.Add(path);
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var pdfPath = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfPath>()
            .Should().ContainSingle()
            .Subject;
        var segment = pdfPath.Contours.Should().ContainSingle().Subject.Segments.Should().ContainSingle().Subject;

        segment.Kind.Should().Be(PdfPathSegmentKind.CubicBezier);
        segment.End.X.Should().BeApproximately(72, 0.001);
        segment.End.Y.Should().BeApproximately(378, 0.001);
        segment.Control1.X.Should().BeApproximately(216, 0.001);
        segment.Control1.Y.Should().BeApproximately(410.236, 0.001);
        segment.Control2.X.Should().BeApproximately(151.529, 0.001);
        segment.Control2.Y.Should().BeApproximately(378, 0.001);
    }

    [Fact]
    public void BuildDocument_ExportsRotatedRectangleAndTextThroughPdfRotationGroup()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            RotationDeg = 30,
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4)),
            Outline = new ShapeOutline.Visible(SrgbColor.Black, widthPt: 1.5),
            Text = "Rotated text",
        });
        deck.Slides.Add(slide);

        var group = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfRotationGroup>()
            .Should().ContainSingle()
            .Subject;

        group.CenterX.Should().Be(144);
        group.CenterY.Should().Be(414);
        group.RotationDegrees.Should().Be(30);
        group.Ops.OfType<PdfFillRect>().Should().ContainSingle(fill =>
            fill.X == 72 &&
            fill.Y == 378 &&
            fill.Width == 144 &&
            fill.Height == 72);
        group.Ops.OfType<PdfStrokeRect>().Should().ContainSingle(stroke =>
            stroke.X == 72 &&
            stroke.Y == 378 &&
            stroke.Width == 144 &&
            stroke.Height == 72);
        group.Ops.OfType<PdfText>().Should().ContainSingle(text =>
            text.X == 80 &&
            text.Y == 424 &&
            text.Text == "Rotated text");
    }

    [Fact]
    public void BuildDocument_ExportsRotatedConnectorThroughPdfRotationGroup()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.Line,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            RotationDeg = 45,
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0xC00000), widthPt: 2.25),
        });
        deck.Slides.Add(slide);

        var pageOps = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;
        var group = pageOps.OfType<PdfRotationGroup>().Should().ContainSingle().Subject;

        pageOps.OfType<PdfLine>().Should().BeEmpty("rotated connectors should be grouped instead of emitted as top-level unrotated lines");
        group.CenterX.Should().Be(144);
        group.CenterY.Should().Be(414);
        group.RotationDegrees.Should().Be(45);
        group.Ops.OfType<PdfLine>().Should().ContainSingle(line =>
            line.X1 == 72 &&
            line.Y1 == 450 &&
            line.X2 == 216 &&
            line.Y2 == 378 &&
            line.LineWidth == 2.25 &&
            line.Color == new PdfColor(0xC0, 0x00, 0x00));
    }

    [Fact]
    public void BuildDocument_ExportsPictureShapesAsPdfImages()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            RotationDeg = 33,
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfImage>().Should().ContainSingle(image =>
            image.X == 72 &&
            image.Y == 378 &&
            image.Width == 144 &&
            image.Height == 72 &&
            image.RotationDegrees == 33 &&
            image.ContentType == "image/png");
        ops.OfType<PdfRotationGroup>().Should().BeEmpty("picture rotation remains on PdfImage.RotationDegrees");
        ops.OfType<PdfText>().Select(text => text.Text)
            .Should().NotContain("[Picture]", "exported picture images should not retain the placeholder fallback label");
    }

    [Theory]
    [InlineData("ellipse", PdfImageClipKind.Ellipse)]
    [InlineData("roundRect", PdfImageClipKind.RoundedRectangle)]
    [InlineData(" Triangle ", PdfImageClipKind.Triangle)]
    [InlineData("DIAMOND", PdfImageClipKind.Diamond)]
    [InlineData("parallelogram", PdfImageClipKind.Parallelogram)]
    [InlineData("hexagon", PdfImageClipKind.Hexagon)]
    [InlineData("chevron", PdfImageClipKind.Chevron)]
    [InlineData("rect", PdfImageClipKind.None)]
    [InlineData("rtTriangle", PdfImageClipKind.None)]
    public void BuildDocument_CarriesPictureFrameGeometryToPdfImageClip(string frameGeometry, PdfImageClipKind expectedClip)
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            PictureFrameGeometry = frameGeometry,
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var image = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfImage>()
            .Should().ContainSingle()
            .Subject;

        image.ClipKind.Should().Be(expectedClip);
    }

    [Fact]
    public void BuildDocument_CarriesPictureAlphaToPdfImageOpacity()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            PictureFormat = new PictureFormat { AlphaModPct = 0.42 },
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var image = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfImage>()
            .Should().ContainSingle()
            .Subject;

        image.Opacity.Should().Be(0.42);
    }

    [Fact]
    public void BuildDocument_CarriesPictureCropToPdfImageSourceCrop()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            PictureFormat = new PictureFormat
            {
                CropLeft = 0.125,
                CropTop = 0.25,
                CropRight = 0.0625,
                CropBottom = 0.1875,
            },
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var image = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfImage>()
            .Should().ContainSingle()
            .Subject;

        image.SourceCrop.HasCrop.Should().BeTrue();
        image.SourceCrop.Left.Should().Be(0.125);
        image.SourceCrop.Top.Should().Be(0.25);
        image.SourceCrop.Right.Should().Be(0.0625);
        image.SourceCrop.Bottom.Should().Be(0.1875);
    }

    [Fact]
    public void BuildDocument_CarriesPictureColorEffectsToPdfImage()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            PictureFormat = new PictureFormat
            {
                Grayscale = true,
                BiLevelThreshold = 0.62,
                Brightness = 0.2,
                Contrast = -0.1,
            },
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var image = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops
            .OfType<PdfImage>()
            .Should().ContainSingle()
            .Subject;

        image.ColorEffects.HasPixelEffects.Should().BeTrue();
        image.ColorEffects.Grayscale.Should().BeTrue();
        image.ColorEffects.BiLevelThreshold.Should().Be(0.62);
        image.ColorEffects.Brightness.Should().Be(0.2);
        image.ColorEffects.Contrast.Should().Be(-0.1);
    }

    [Fact]
    public void ExportToBytes_EmbedsPictureImageXObject()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Picture = new ImagePart { Bytes = MinimalPngBytes(), ContentType = "image/png" },
        });
        deck.Slides.Add(slide);

        var pdf = Encoding.Latin1.GetString(PresentationPdfExporter.ExportToBytes(deck));

        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Im1 Do");
        pdf.Should().Contain("144 0 0 72 72 378 cm");
        pdf.Should().NotContain("[Picture]");
    }

    [Fact]
    public void TitleOp_IsBold()
    {
        var doc = PresentationPdfExporter.BuildDocument(SampleDeck());

        doc.Pages[0].Ops.OfType<PdfText>().First(t => t.Text == "Welcome")
            .Face.Should().Be(PdfFontFace.Bold);
    }

    [Fact]
    public void BuildDocument_SetsCreatorAndDocumentMetadata()
    {
        var props = PresentationPdfExporter.BuildDocument(SampleDeck()).Properties;

        props.Should().NotBeNull();
        props!.Creator.Should().Be("FreeP");
        props.Title.Should().Be("My Deck");
        props.Author.Should().Be("Tester");
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

    private static (double X, double Y) BaseCenter(PdfFilledTriangle triangle) =>
        ((triangle.X2 + triangle.X3) / 2.0, (triangle.Y2 + triangle.Y3) / 2.0);

    // R157 finding freep-shape-text-autofit F1: AppendShapeText used to lay out every shape's text
    // at a hard-coded 18pt/26pt regardless of what was authored, so a normAutofit-shrunk text box
    // whose real (on-screen) font size lets it hold more lines than the fixed budget allowed had
    // its remaining lines silently dropped rather than shrunk. This mirrors the finding's own
    // probe (a normAutofit AutoShape with 20 paragraphs authored at 10pt): the box below is sized
    // tall enough that 20 lines of genuinely-10pt text fit (300pt of vertical budget needed at
    // this exporter's own leading ratio; 320pt given here), which the OLD hard-coded-18pt/26pt
    // layout could never do regardless of box size mattering less than the wrong font/leading.
    [Fact]
    public void BuildDocument_AutofitShrunkTextBox_KeepsAllLinesAtAuthoredSize()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(4 * 72),  // 4in wide
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(320),    // tall enough for 20 lines at 10pt
            TextBody = new TextBody { AutoFitKind = TextAutoFitKind.Normal },
        };
        for (var i = 1; i <= 20; i++)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = $"Line {i:D2} of 20", FontSizePt = 10 });
            shape.TextBody.Paragraphs.Add(para);
        }

        var slide = new Slide();
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var textOps = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops.OfType<PdfText>().ToList();

        for (var i = 1; i <= 20; i++)
        {
            var expected = $"Line {i:D2} of 20";
            textOps.Should().Contain(t => t.Text == expected && t.FontSize == 10,
                $"'{expected}' should survive at the authored 10pt size, not be dropped or forced to 18pt");
        }
    }

    // Sibling/no-regression: a shape with no explicit run font size and no autofit (the common
    // case exercised by every other test in this file, via the SlideShape.Text setter) must keep
    // rendering at the original fixed BodySize/BodyLeadingPt (18pt / 26pt leading) so plain shape
    // text and its Y-position geometry are unchanged by the autofit-aware code path.
    [Fact]
    public void BuildDocument_PlainShapeText_StillUsesDefaultBodySize()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();

        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Text = "Positioned text",
        };
        var slide = new Slide();
        slide.Shapes.Add(shape);
        deck.Slides.Add(slide);

        var ops = PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops;

        ops.OfType<PdfText>().Should().ContainSingle(text =>
            text.Text == "Positioned text" &&
            text.FontSize == 18 &&
            text.X == 80 &&
            text.Y == 424);
    }
}
