using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SmartArtDefaultLiveRendererContractTests
{
    [Fact]
    public void WpfSlideCanvas_ConsumesSharedCompositorForSmartArtDrawOps()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().Contain("RenderOp(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "SmartArt geometry must remain shared with Avalonia through SlideCompositor");
    }

    [Fact]
    public void WpfHierarchy3Rendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutHierarchy3(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "hierarchy3 geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfGroupedListRendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutGroupedList(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "grouped-list bands must remain renderer-neutral");
    }
}
