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

    [Fact]
    public void WpfRelationship1Rendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutBasicRelationship(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "relationship1 geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfGridMatrixRendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutGridMatrix(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "gridMatrix geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfBasicMatrixRendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutBasicMatrix(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "basicMatrix geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfIncreasingCircleProcessRendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutIncreasingCircleProcess(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "increasingCircleProcess geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfVerticalArrowListRendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutVerticalArrowList(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "verticalArrowList geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfProcess1Rendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutProcess(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "process1 geometry must remain renderer-neutral");
    }

    [Fact]
    public void WpfList1Rendering_ConsumesTheSharedLivePlan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().NotContain("LayoutList(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "list1 geometry must remain renderer-neutral");
    }
}
