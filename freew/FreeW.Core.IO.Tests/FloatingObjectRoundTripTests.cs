using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class FloatingObjectRoundTripTests
{
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static XDocument DocXml(TextDocument doc)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static FloatingPlacement TestPlacement(int zOrder = 5) => new()
    {
        Wrapping = ImageWrapping.Square,
        WrapTextSide = FloatingWrapTextSide.Largest,
        HorizontalOffsetPt = 36,
        VerticalOffsetPt = 18,
        HorizontalAnchor = HorizontalAnchor.Margin,
        VerticalAnchor = VerticalAnchor.Page,
        ZOrderIndex = zOrder
    };

    private static TextDocument DocumentWith(Run run)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(run);
        doc.Blocks.Add(para);
        return doc;
    }

    [Fact]
    public void FloatingShape_EmitsAnchor()
    {
        var shape = new Shape(ShapeKind.Ellipse, 72, 36) { Placement = TestPlacement() };
        var xml = DocXml(DocumentWith(Run.FromShape(shape)));
        xml.Descendants(Wp + "anchor").Should().NotBeEmpty();
        xml.Descendants(Wp + "inline").Should().BeEmpty();
    }

    [Fact]
    public void FloatingShape_RoundTrips()
    {
        var shape = new Shape(ShapeKind.Ellipse, 72, 36) { Placement = TestPlacement(7) };
        var recovered = RoundTrip(DocumentWith(Run.FromShape(shape)));
        var s = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.Shape is not null).Shape!;
        s.IsFloating.Should().BeTrue();
        s.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        s.Placement.WrapTextSide.Should().Be(FloatingWrapTextSide.Largest);
        s.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
        s.Placement.VerticalOffsetPt.Should().BeApproximately(18, 0.5);
        s.Placement.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        s.Placement.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        s.Placement.ZOrderIndex.Should().Be(7);
    }

    [Fact]
    public void InlineShape_Unaffected()
    {
        var shape = new Shape(ShapeKind.Rectangle, 72, 36);
        var recovered = RoundTrip(DocumentWith(Run.FromShape(shape)));
        var s = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.Shape is not null).Shape!;
        s.IsFloating.Should().BeFalse();
    }

    [Fact]
    public void FloatingChart_EmitsAnchor()
    {
        var chart = Chart.Create(ChartKind.Bar, new[] { "A", "B" }, new[] { 1.0, 2.0 });
        chart.Placement = TestPlacement();
        var xml = DocXml(DocumentWith(new Run(string.Empty) { Chart = chart }));
        xml.Descendants(Wp + "anchor").Should().NotBeEmpty();
    }

    [Fact]
    public void FloatingChart_RoundTrips()
    {
        var chart = Chart.Create(ChartKind.Line, new[] { "X", "Y" }, new[] { 3.0, 5.0 });
        chart.Placement = TestPlacement(3);
        var recovered = RoundTrip(DocumentWith(new Run(string.Empty) { Chart = chart }));
        var c = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.Chart is not null).Chart!;
        c.IsFloating.Should().BeTrue();
        c.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        c.Placement.ZOrderIndex.Should().Be(3);
        c.Placement.HorizontalOffsetPt.Should().BeApproximately(36, 0.5);
    }

    [Fact]
    public void FloatingSmartArt_RoundTrips()
    {
        var art = SmartArt.Create(SmartArtKind.Process, new[] { "Step1", "Step2" });
        art.Placement = TestPlacement(2);
        var recovered = RoundTrip(DocumentWith(new Run(string.Empty) { SmartArt = art }));
        var sa = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.SmartArt is not null).SmartArt!;
        sa.IsFloating.Should().BeTrue();
        sa.Placement!.ZOrderIndex.Should().Be(2);
    }

    [Fact]
    public void FloatingWordArt_RoundTrips()
    {
        var wa = new WordArt("Hello", WordArtStyle.Shadow, 36) { Placement = TestPlacement(4) };
        var recovered = RoundTrip(DocumentWith(Run.FromWordArt(wa)));
        var w = ((Paragraph)recovered.Blocks[0]).Runs.Single(r => r.WordArt is not null).WordArt!;
        w.IsFloating.Should().BeTrue();
        w.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        w.Placement.WrapTextSide.Should().Be(FloatingWrapTextSide.Largest);
        w.Placement.ZOrderIndex.Should().Be(4);
    }
}
