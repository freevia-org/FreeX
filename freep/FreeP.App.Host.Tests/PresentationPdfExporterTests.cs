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
    [InlineData("rect", PdfImageClipKind.None)]
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
}
