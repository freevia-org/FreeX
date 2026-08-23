using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class SmartArtCachedVerticalArrowParityTests
{
    [Fact]
    public void VerticalArrowListCachedFallbackUsesMeasuredOfficeGeometry()
    {
        var presentation = PptxPackageReader.Read(Path.Combine(
            FindCorpusDirectory(),
            "15-smartart-grouped-list.pptx"));

        var shapes = SlideCompositor.Compose(
                presentation,
                presentation.Slides[9])
            .OfType<DrawOp.Shape>()
            .ToArray();

        var followNodes = shapes
            .Where(shape => shape.SmartArtRole == SmartArtSemanticRole.FollowNode)
            .ToArray();
        followNodes.Should().HaveCount(2);
        followNodes.Should().AllSatisfy(shape =>
        {
            var contour = shape.Geometry.Contours.Single();
            contour.Segments[0].End.X.Should().BeApproximately(
                shape.BoundsDip.Left + shape.BoundsDip.Width * 0.82,
                0.2);
        });

        foreach (var text in new[] { "Collect", "Share" })
        {
            var shape = shapes.Single(shape =>
                shape.Text?.Paragraphs.Any(paragraph =>
                    paragraph.Runs.Any(run => run.Text == text)) == true);
            var contour = shape.Geometry.Contours.Single();
            contour.Start.X.Should().BeApproximately(
                shape.BoundsDip.Left + Math.Min(shape.BoundsDip.Width, shape.BoundsDip.Height) * 0.18,
                0.2);
        }
    }

    private static string FindCorpusDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "FreeP.RenderCompare",
                "corpus");
            if (File.Exists(Path.Combine(candidate, "15-smartart-grouped-list.pptx")))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FreeP RenderCompare corpus was not found.");
    }
}
