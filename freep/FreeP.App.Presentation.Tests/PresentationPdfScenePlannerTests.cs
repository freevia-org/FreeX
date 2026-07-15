using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPdfScenePlannerTests
{
    [Fact]
    public void ResolveRasterSize_UsesModeledSlideSizeAndPreservesAspectRatio()
    {
        var size = PresentationPdfScenePlanner.ResolveRasterSize(
            DrawingMlCoordinateUnits.PointsToEmu(576),
            DrawingMlCoordinateUnits.PointsToEmu(432),
            requestedWidthPx: 1024,
            requestedHeightPx: null);

        size.SlideSize.Should().Be(new PresentationPdfSlideSize(576, 432));
        size.WidthPx.Should().Be(1024);
        size.HeightPx.Should().Be(768);
    }

    [Fact]
    public void VectorAndRasterPlans_UseTheSameSlideSceneSizeAndMetadata()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Properties.Title = "Scene plan";
        presentation.SlideSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(800);
        presentation.SlideSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(600);
        presentation.Slides.Add(new Slide { Title = "Slide 1" });

        var scene = PresentationPdfScenePlanner.ResolveSlideSize(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
        var vectorPage = PresentationPdfExporter.BuildDocument(presentation).Pages.Single();
        var rasterPlan = PresentationRasterPdfExporter.BuildRenderPlan(
            presentation,
            new PresentationRasterPdfExportRequest(WidthPx: 1000),
            (_, _, _, _) => [1]);

        vectorPage.WidthPoints.Should().Be(scene.WidthPoints);
        vectorPage.HeightPoints.Should().Be(scene.HeightPoints);
        rasterPlan.PageWidthPoints.Should().Be(scene.WidthPoints);
        rasterPlan.PageHeightPoints.Should().Be(scene.HeightPoints);
        PresentationPdfScenePlanner.BuildDocumentProperties(presentation)
            .Title
            .Should()
            .Be("Scene plan");
    }
}
