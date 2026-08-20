using System.IO.Compression;
using System.Xml.Linq;
using Free.ToolsShared;

namespace FreeP.RenderCompare.Tests;

public sealed class SmartArtFixtureEvidenceTests
{
    [Theory]
    [InlineData("../diagrams/layout3.xml")]
    [InlineData("..\\diagrams\\layout3.xml")]
    public void PowerPointDiagramLayoutTargetUsesOpcEntrySeparators(string relationshipTarget)
    {
        SmartArtFixtureGenerator.GetDiagramLayoutPartPath(relationshipTarget)
            .Should().Be("ppt/diagrams/layout3.xml");
    }

    [Fact]
    public void PowerPointDiagramLayoutTargetRejectsTraversalOutsideDiagrams()
    {
        var action = () => SmartArtFixtureGenerator.GetDiagramLayoutPartPath("../layouts/layout3.xml");

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NativeSmartArtFixtureContainsOneDiagramGraphicFramePerSlide()
    {
        using var archive = ZipFile.OpenRead(Path.Combine(
            RepositoryRoot,
            "tools",
            "FreeP.RenderCompare",
            "corpus",
            "15-smartart-grouped-list.pptx"));

        var presentation = ReadXml(archive, "ppt/presentation.xml");
        var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var dgm = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");

        presentation.Descendants(p + "sldId").Should().HaveCount(10);
        for (var index = 1; index <= 10; index++)
        {
            var slide = ReadXml(archive, $"ppt/slides/slide{index}.xml");
            slide.Descendants(p + "graphicFrame").Should().ContainSingle();
            slide.Descendants(a + "graphicData")
                .Single().Attribute("uri")!.Value
                .Should().Be("http://schemas.openxmlformats.org/drawingml/2006/diagram");
            slide.Descendants(dgm + "relIds").Should().ContainSingle();
        }
    }

    [Fact]
    public void NativeSmartArtFixtureCarriesPowerPointMaterializedDrawingForEverySlide()
    {
        using var archive = ZipFile.OpenRead(Path.Combine(
            RepositoryRoot,
            "tools",
            "FreeP.RenderCompare",
            "corpus",
            "15-smartart-grouped-list.pptx"));

        for (var index = 1; index <= 10; index++)
        {
            var rels = ReadXml(archive, $"ppt/slides/_rels/slide{index}.xml.rels");
            var relationshipTypes = rels.Root!.Elements()
                .Select(element => (string?)element.Attribute("Type"))
                .ToArray();
            relationshipTypes.Any(type => type is not null
                && type.EndsWith("/diagramDrawing", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue();
        }
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static string RepositoryRoot =>
        RepositoryRootLocator.Find(AppContext.BaseDirectory, "FreeX.slnx")
            ?? throw new DirectoryNotFoundException("Could not locate the FreeX repository root.");
}
