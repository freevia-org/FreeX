using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.RenderCompare.Tests;

public sealed class SmartArtFixtureEvidenceTests
{
    [Fact]
    public void Process1FixtureContainsTheAuditedFiveStageNodeAndConnectorGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout1.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing1.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/process1");
        var shapes = drawing.Descendants(dsp + "sp").ToList();
        shapes.Should().HaveCount(9);
        shapes.Where((_, index) => index % 2 == 0)
            .Select(shape => (string?)shape.Descendants(a + "prstGeom").Single().Attribute("prst"))
            .Should().OnlyContain(value => value == "roundRect");
        shapes.Where((_, index) => index % 2 == 1)
            .Should().OnlyContain(shape => shape.Descendants(a + "ln").Any());
        drawing.Descendants(a + "t").Select(element => element.Value)
            .Should().Equal("Plan", "Design", "Build", "Test", "Deploy");
        ReadXml(archive, "ppt/diagrams/data1.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(5);
    }

    [Fact]
    public void GroupedListFixtureContainsTheAuditedCachedBandGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout6.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing6.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/groupedList");
        drawing.Descendants(dsp + "sp").Should().HaveCount(8);
        drawing.Descendants(a + "t").Should().HaveCount(6);
        ReadXml(archive, "ppt/diagrams/data6.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(6);
    }

    [Fact]
    public void Relationship1FixtureContainsTheAuditedNodeEllipseGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout7.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing7.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/relationship1");
        var shapes = drawing.Descendants(dsp + "sp").ToList();
        shapes.Should().HaveCount(3);
        drawing.Descendants(a + "prstGeom")
            .Select(element => (string?)element.Attribute("prst"))
            .Should().OnlyContain(value => value == "ellipse");
        shapes.Select(shape => shape.Descendants(a + "ext").Single().Attributes()
                .Where(attribute => attribute.Name.LocalName is "cx" or "cy")
                .Select(attribute => long.Parse(attribute.Value)))
            .Should().OnlyContain(extents => extents.SequenceEqual(new long[] { 2_400_000L, 2_400_000L }));
        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("x")!.Value))
            .Zip(new long[] { 1_522_800L, 2_914_800L, 4_306_800L })
            .Should().OnlyContain(pair => pair.First == pair.Second);
        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("y")!.Value))
            .Should().OnlyContain(value => value == 1_672_400L);
        drawing.Descendants(a + "t").Should().HaveCount(3);
        ReadXml(archive, "ppt/diagrams/data7.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(3);
    }

    [Fact]
    public void GridMatrixFixtureContainsTheAuditedFourCellSquareGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout8.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing8.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/gridMatrix");
        var shapes = drawing.Descendants(dsp + "sp").ToList();
        shapes.Should().HaveCount(4);
        drawing.Descendants(a + "prstGeom")
            .Select(element => (string?)element.Attribute("prst"))
            .Should().OnlyContain(value => value == "rect");

        var extents = shapes.Select(shape => shape.Descendants(a + "ext").Single().Attributes()
            .Where(attribute => attribute.Name.LocalName is "cx" or "cy")
            .Select(attribute => long.Parse(attribute.Value))
            .ToArray()).ToArray();
        extents.Should().OnlyContain(extent => extent.SequenceEqual(new long[] { 2_576_543L, 2_576_543L }));

        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("x")!.Value))
            .Should().Equal(1_472_192L, 4_180_865L, 1_472_192L, 4_180_865L);
        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("y")!.Value))
            .Should().Equal(229_792L, 229_792L, 2_938_465L, 2_938_465L);
        drawing.Descendants(a + "t").Should().HaveCount(4);
        ReadXml(archive, "ppt/diagrams/data8.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(4);
    }

    [Fact]
    public void IncreasingCircleProcessFixtureContainsTheAuditedGrowingEllipseGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout9.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing9.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/increasingCircleProcess");
        var shapes = drawing.Descendants(dsp + "sp").ToList();
        shapes.Should().HaveCount(7);
        shapes.Take(4).Select(shape => (string?)shape.Descendants(a + "prstGeom")
                .Single().Attribute("prst"))
            .Should().OnlyContain(value => value == "ellipse");
        shapes.Skip(4).Should().OnlyContain(shape => shape.Descendants(a + "ln").Any());
        shapes.Take(4).Select(shape => long.Parse(shape.Descendants(a + "ext").Single().Attribute("cx")!.Value))
            .Should().BeInAscendingOrder();
        drawing.Descendants(a + "t").Select(element => element.Value)
            .Should().Equal("Phase A", "Phase B", "Phase C", "Phase D");
        ReadXml(archive, "ppt/diagrams/data9.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(4);
    }

    [Fact]
    public void VerticalArrowListFixtureContainsTheAuditedFourSlotGrammar()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tools", "FreeP.RenderCompare", "corpus", "15-smartart-grouped-list.pptx");

        using var archive = ZipFile.OpenRead(path);
        var layout = ReadXml(archive, "ppt/diagrams/layout10.xml");
        var drawing = ReadXml(archive, "ppt/diagrams/drawing10.xml");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var dsp = XNamespace.Get("http://schemas.microsoft.com/office/drawing/2008/diagram");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        layout.Root!.Attribute("uniqueId")!.Value.Should().EndWith("/verticalArrowList");
        var shapes = drawing.Descendants(dsp + "sp").ToList();
        shapes.Should().HaveCount(4);
        shapes.Select(shape => (string?)shape.Descendants(a + "prstGeom")
                .Single().Attribute("prst"))
            .Should().OnlyContain(value => value == "downArrow");
        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("x")!.Value))
            .Should().OnlyContain(value => value == 329_184L);
        shapes.Select(shape => long.Parse(shape.Descendants(a + "off").Single().Attribute("y")!.Value))
            .Should().Equal(229_792L, 1_574_434L, 2_919_076L, 4_263_718L);
        shapes.Select(shape => shape.Descendants(a + "ext").Single().Attributes()
                .Where(attribute => attribute.Name.LocalName is "cx" or "cy")
                .Select(attribute => long.Parse(attribute.Value)))
            .Should().OnlyContain(extent => extent.SequenceEqual(new long[] { 7_571_232L, 1_251_289L }));
        drawing.Descendants(a + "t").Select(element => element.Value)
            .Should().Equal("Collect", "Shape", "Review", "Share");
        ReadXml(archive, "ppt/diagrams/data10.xml")
            .Descendants(dgm + "pt")
            .Where(element => (string?)element.Attribute("type") != "doc")
            .Should().HaveCount(4);
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the FreeX repository root.");
    }
}
