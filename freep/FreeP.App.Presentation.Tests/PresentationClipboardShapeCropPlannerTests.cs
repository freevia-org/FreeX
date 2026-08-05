using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationClipboardShapeCropPlannerTests
{
    [Fact]
    public void Plan_UsesOuterPixelBoundsForFractionalShapeEdges()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = 1_000;
        presentation.SlideSizeCyEmu = 1_000;
        var shape = Shape(101, 209, 300, 400);

        var crop = PresentationClipboardShapeCropPlanner.Plan(
            presentation,
            [shape],
            frameWidth: 100,
            frameHeight: 100);

        crop.Should().Be(new PresentationClipboardPixelCrop(10, 20, 31, 41));
    }

    [Fact]
    public void Plan_UnionsAndClampsSelectionToTheFrame()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = 1_000;
        presentation.SlideSizeCyEmu = 1_000;

        var crop = PresentationClipboardShapeCropPlanner.Plan(
            presentation,
            [
                Shape(-100, -200, 250, 300),
                Shape(900, 800, 300, 400),
            ],
            frameWidth: 100,
            frameHeight: 100);

        crop.Should().Be(new PresentationClipboardPixelCrop(0, 0, 100, 100));
        crop.IsFullFrame(100, 100).Should().BeTrue();
    }

    [Fact]
    public void Plan_UsesOneEdgePixelForAnEntirelyOffFrameShape()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = 1_000;
        presentation.SlideSizeCyEmu = 1_000;

        PresentationClipboardShapeCropPlanner.Plan(
                presentation,
                [Shape(1_100, -200, 100, 100)],
                frameWidth: 100,
                frameHeight: 100)
            .Should().Be(new PresentationClipboardPixelCrop(99, 0, 1, 1));
    }

    [Fact]
    public void Plan_FallsBackToTheFullFrameWithoutUsableSlideGeometry()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = 0;

        PresentationClipboardShapeCropPlanner.Plan(
                presentation,
                [Shape(10, 20, 30, 40)],
                frameWidth: 320,
                frameHeight: 180)
            .Should().Be(new PresentationClipboardPixelCrop(0, 0, 320, 180));
    }

    [Fact]
    public void NativeRenderers_DelegateCropGeometryToTheSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sources = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "WpfShapeRenderer.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "PresentationClipboardService.cs"),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("PresentationClipboardShapeCropPlanner.Plan(");
            source.Should().NotContain("private static PixelRect CalculateCrop(");
            source.Should().NotContain("scaleX");
            source.Should().NotContain("Math.Floor");
        }
    }

    private static SlideShape Shape(long x, long y, long width, long height) => new()
    {
        OffsetXEmu = x,
        OffsetYEmu = y,
        ExtentCxEmu = width,
        ExtentCyEmu = height,
    };

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
