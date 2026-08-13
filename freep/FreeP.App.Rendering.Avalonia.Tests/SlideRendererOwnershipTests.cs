using FreeP.App.Rendering.Avalonia;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SlideRendererOwnershipTests
{
    [Fact]
    public void Renderer_exposes_bytes_without_path_owned_output_endpoints()
    {
        var publicMethods = typeof(SlideRenderer).GetMethods();

        var publicMethodNames = publicMethods.Select(method => method.Name).ToArray();

        publicMethodNames.Should().Contain(nameof(SlideRenderer.RenderToBytes));
        publicMethodNames.Should().NotContain(["RenderToPng", "RenderAllSlides"]);
    }
}
