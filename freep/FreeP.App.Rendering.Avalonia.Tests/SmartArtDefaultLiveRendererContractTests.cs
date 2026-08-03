using System.IO;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SmartArtDefaultLiveRendererContractTests
{
    [Fact]
    public void AvaloniaSlideCanvas_ConsumesSharedCompositorForSmartArtDrawOps()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs"));

        source.Should().Contain("SlideCompositor.Compose(");
        source.Should().Contain("RenderOp(");
        source.Should().NotContain("SmartArtLayoutEngine.Layout(",
            "SmartArt geometry must remain shared with WPF through SlideCompositor");
    }
}
