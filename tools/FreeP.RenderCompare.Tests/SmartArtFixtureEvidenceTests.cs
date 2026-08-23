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

    [Fact]
    public void GroupedListOutlierSlidesKeepTheirAuthoritativeSmartArtRoutes()
    {
        var presentation = FreeP.Core.IO.PptxPackageReader.Read(Path.Combine(
            RepositoryRoot,
            "tools",
            "FreeP.RenderCompare",
            "corpus",
            "15-smartart-grouped-list.pptx"));

        var slide09 = presentation.Slides[8].Shapes
            .Single(shape => shape.SmartArt is not null)
            .SmartArt!;
        slide09.Data.Should().NotBeNull();
        slide09.Data!.LayoutUniqueId.Should().EndWith("/IncreasingCircleProcess");
        slide09.Data.Family.Should().Be(FreeP.Core.Model.SmartArtFamily.Process);
        slide09.Data.IsLiveLayoutSupported.Should().BeFalse();
        slide09.FallbackShapes.Should().HaveCount(12);
        slide09.Data.Nodes.SelectMany(Flatten).Select(node => node.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Should().BeEquivalentTo("Phase A", "Phase B", "Phase C", "Phase D");

        var slide10 = presentation.Slides[9].Shapes
            .Single(shape => shape.SmartArt is not null)
            .SmartArt!;
        slide10.Data.Should().NotBeNull();
        slide10.Data!.LayoutUniqueId.Should().EndWith("/vList6");
        slide10.Data.Family.Should().Be(FreeP.Core.Model.SmartArtFamily.List);
        slide10.Data.IsLiveLayoutSupported.Should().BeFalse();
        slide10.FallbackShapes.Should().HaveCount(4);
        slide10.FallbackShapes.SelectMany(shape => shape.TextBody?.Paragraphs ?? [])
            .Count(paragraph => paragraph.BulletKind != FreeP.Core.Model.BulletKind.None)
            .Should().Be(4);

        var arrowShapes = slide10.FallbackShapes
            .Where(shape => shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.RightArrow)
            .ToArray();
        arrowShapes.Should().HaveCount(2);
        arrowShapes.Select(shape => shape.TextBody!.InsetTopPt!.Value)
            .Should().AllSatisfy(inset => inset.Should().BeApproximately(25.3135, 0.01));
        arrowShapes.Select(shape => shape.TextBody!.InsetRightPt!.Value)
            .Should().AllSatisfy(inset => inset.Should().BeApproximately(70.5406, 0.01));

        static IEnumerable<FreeP.Core.Model.SmartArtNode> Flatten(FreeP.Core.Model.SmartArtNode node)
        {
            yield return node;
            foreach (var child in node.Children)
            {
                foreach (var descendant in Flatten(child))
                    yield return descendant;
            }
        }
    }

    [Fact]
    public void NativeCachedSmartArtRolesUseBoundedReaderCorrections()
    {
        var presentation = FreeP.Core.IO.PptxPackageReader.Read(Path.Combine(
            RepositoryRoot,
            "tools",
            "FreeP.RenderCompare",
            "corpus",
            "15-smartart-grouped-list.pptx"));

        var process = presentation.Slides[5].Shapes
            .Single(shape => shape.SmartArt is not null)
            .SmartArt!;
        process.Data!.LayoutUniqueId.Should().EndWith("/lProcess2");
        process.Data.IsLiveLayoutSupported.Should().BeFalse();
        process.FallbackShapes[0].TextBody!.InsetBottomPt.Should().BeLessThan(20);
        process.FallbackShapes[1].TextBody!.InsetTopPt.Should().BeLessThan(20);

        var matrix = presentation.Slides[7].Shapes
            .Single(shape => shape.SmartArt is not null)
            .SmartArt!;
        matrix.Data!.LayoutUniqueId.Should().EndWith("/matrix2");
        matrix.Data.IsLiveLayoutSupported.Should().BeFalse();
        matrix.FallbackShapes[0].AutoShapeKind.Should().Be(
            Free.Shared.Drawing.DrawingShapeKind.QuadArrow);
        matrix.FallbackShapes[1].AutoShapeKind.Should().Be(
            Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle);
        matrix.FallbackShapes[1].TextBody!.InsetTopPt.Should().BeLessThan(20);
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
