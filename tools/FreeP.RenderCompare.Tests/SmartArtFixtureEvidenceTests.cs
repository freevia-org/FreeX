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
