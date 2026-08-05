namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureOwnershipTests
{
    [Fact]
    public void CanvasAdapters_DelegatePortableGestureStateAndPlanningToSharedSession()
    {
        var shared = ReadRepoFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureSession.cs");
        var wpf = ReadRepoFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "CanvasGestureHandler.cs");
        var avalonia = ReadRepoFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.cs");

        shared.Should().Contain("public sealed class CanvasGestureSession");
        shared.Should().Contain("CanvasGesturePlanner.PlanMove");
        shared.Should().Contain("CanvasGesturePlanner.ComputeResizeBounds");
        shared.Should().Contain("CanvasGesturePlanner.PlanMultiRotate");

        foreach (var adapter in new[] { wpf, avalonia })
        {
            adapter.Should().Contain("CanvasGestureSession _gestureSession");
            adapter.Should().NotContain("private enum GestureKind");
            adapter.Should().NotContain("_dragStartScreen");
            adapter.Should().NotContain("_resizeOrigX");
            adapter.Should().NotContain("new CanvasMoveRequest");
            adapter.Should().NotContain("new CanvasResizeRequest");
            adapter.Should().NotContain("new CanvasRotationRequest");
            adapter.Should().NotContain("ShapeGeometryAdjustmentPlanner.BuildMutationPlan");
            adapter.Should().NotContain("PictureCropAuthoringPlanner.BuildMutationPlan");
            adapter.Should().NotContain("private const long SmallNudgeEmu");
        }
    }

    [Fact]
    public void SlideShowAdapters_DelegateInputRoutingAndCanvasMappingToSharedRuntime()
    {
        var session = ReadRepoFile(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowSessionController.cs");
        var runtime = ReadRepoFile(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowRuntimeApplication.cs");
        var wpf = ReadRepoFile("freep", "FreeP.App.Host", "SlideShowWindow.cs");
        var avalonia = ReadRepoFile("freep", "FreeP.App.Avalonia", "SlideShowWindow.cs");

        session.Should().Contain("SlideShowPointerInteractionPlanner.PlanClick");
        session.Should().Contain("SlideShowPointerInteractionPlanner.HitTestHyperlink");
        session.Should().Contain("SlideShowPointerInteractionPlanner.MapInkPoint");
        session.Should().Contain("SlideShowPointerClickIntentKind.Zoom");
        runtime.Should().Contain("_session.PlanPointerInput(pointer)");
        runtime.Should().Contain("_session.HitTestHyperlink(slide, pointer)");
        runtime.Should().Contain("_session.BeginPointerInk(pointer)");
        foreach (var adapter in new[] { wpf, avalonia })
        {
            adapter.Should().Contain("_runtime.HandlePointerInput");
            adapter.Should().Contain("_runtime.HitTestHyperlink");
            adapter.Should().Contain("_runtime.BeginPointerInk");
            adapter.Should().Contain("_runtime.AppendPointerInk");
            adapter.Should().Contain("_runtime.EndPointerInk");
            adapter.Should().NotContain("_session.PlanPointerInput");
            adapter.Should().NotContain("SlideShowHostPlanner.MapCanvasPointToSlide");
            adapter.Should().NotContain("case SlideShowPointerClickIntentKind.");
            adapter.Should().NotContain("private uint? HitTestTriggerShape");
        }
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return File.ReadAllText(Path.Combine([directory.FullName, .. pathParts]));

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
