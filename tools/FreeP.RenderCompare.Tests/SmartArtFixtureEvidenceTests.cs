using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.RenderCompare.Tests;

public sealed class SmartArtFixtureEvidenceTests
{
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
        drawing.Descendants(dsp + "sp").Should().HaveCount(3);
        drawing.Descendants(a + "prstGeom")
            .Select(element => (string?)element.Attribute("prst"))
            .Should().OnlyContain(value => value == "ellipse");
        drawing.Descendants(a + "t").Should().HaveCount(3);
        ReadXml(archive, "ppt/diagrams/data7.xml")
            .Descendants(dgm + "pt")
            .Should().HaveCount(3);
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
