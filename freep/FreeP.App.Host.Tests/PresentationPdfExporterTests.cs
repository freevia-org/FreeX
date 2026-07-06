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
}
