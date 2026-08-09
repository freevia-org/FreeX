using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SelectionAdornerGeometryTests
{
    [Fact]
    public void GetHandleCenters_ReturnsResizeHandlesInGestureOrder()
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.GetHandleCenters(rect).Should().Equal(
            new CanvasGesturePoint(60, 20),
            new CanvasGesturePoint(110, 20),
            new CanvasGesturePoint(110, 40),
            new CanvasGesturePoint(110, 60),
            new CanvasGesturePoint(60, 60),
            new CanvasGesturePoint(10, 60),
            new CanvasGesturePoint(10, 40),
            new CanvasGesturePoint(10, 20));
    }

    [Fact]
    public void GetRotateHandleCenter_UsesSharedOffsetAboveTopCenter()
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.GetRotateHandleCenter(rect)
            .Should().Be(new CanvasGesturePoint(60, 2));
    }

    [Theory]
    [InlineData(60, 2, CanvasGestureHandleKind.Rotate)]
    [InlineData(10, 20, CanvasGestureHandleKind.ResizeNW)]
    [InlineData(110, 20, CanvasGestureHandleKind.ResizeNE)]
    [InlineData(110, 60, CanvasGestureHandleKind.ResizeSE)]
    [InlineData(10, 60, CanvasGestureHandleKind.ResizeSW)]
    [InlineData(60, 40, CanvasGestureHandleKind.Body)]
    [InlineData(0, 0, CanvasGestureHandleKind.None)]
    public void HitTestHandle_ReturnsExpectedHandle(
        double x,
        double y,
        CanvasGestureHandleKind expected)
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.HitTestHandle(rect, new CanvasGesturePoint(x, y))
            .Should().Be(expected);
    }

    [Fact]
    public void WpfAndAvaloniaAdorners_DelegateGeometryPolicyToSharedPlanner()
    {
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SelectionAdorner.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SelectionAdornerLayer.cs");

        wpf.Should().Contain("SelectionAdornerGeometry.GetHandleCenters");
        wpf.Should().Contain("SelectionAdornerGeometry.GetRotateHandleCenter");
        wpf.Should().Contain("SelectionAdornerGeometry.HitTestHandle");
        wpf.Should().Contain("public CanvasGestureHandleKind HitTestHandle");
        wpf.Should().NotContain("public enum HandleKind");
        wpf.Should().NotContain("ToHandleKind");
        wpf.Should().NotContain("Math.Sqrt");
        wpf.Should().NotContain("HandleHitRadius");

        avalonia.Should().Contain("SelectionAdornerGeometry.GetHandleCenters");
        avalonia.Should().Contain("SelectionAdornerGeometry.GetRotateHandleCenter");
        avalonia.Should().Contain("SelectionAdornerGeometry.HitTestHandle");
        avalonia.Should().Contain("public CanvasGestureHandleKind HitTestHandle");
        avalonia.Should().NotContain("public enum HandleKind");
        avalonia.Should().NotContain("ToHandleKind");
        avalonia.Should().NotContain("Math.Sqrt");
        avalonia.Should().NotContain("HandleHitRadius");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
