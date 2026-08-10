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
        var router = ReadRepoFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureRouter.cs");
        var interaction = ReadRepoFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureInteractionPlanner.cs");
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
        router.Should().Contain("private readonly CanvasGestureSession _session");
        router.Should().Contain("HandlePointerPressed(CanvasGesturePressRequest request)");
        router.Should().Contain("PreviewPointer(");
        router.Should().Contain("CompletePointer(");
        router.Should().Contain("HandleKeyDown(");
        router.Should().Contain("ShapeHitTester.MarqueeHitTest");
        router.Should().Contain("ChartPointHitTester.TryHitTest");
        router.Should().Contain("ZoomNavigationService.TryGetTargetSlideIndex");
        interaction.Should().Contain("BuildPressRequest(");
        interaction.Should().Contain("PlanCursor(");
        interaction.Should().Contain("SelectionAdornerProjectionPlan selection");

        foreach (var adapter in new[] { wpf, avalonia })
        {
            adapter.Should().Contain("CanvasGestureRouter _gestureRouter");
            adapter.Should().Contain("_gestureRouter.HandlePointerPressed(");
            adapter.Should().Contain("_gestureRouter.PreviewPointer(");
            adapter.Should().Contain("_gestureRouter.CompletePointer(");
            adapter.Should().Contain("_gestureRouter.HandleKeyDown(");
            adapter.Should().Contain("CreatePressRequest(");
            adapter.Should().Contain("CanvasGestureInteractionPlanner.BuildPressRequest(");
            adapter.Should().Contain("CanvasGestureInteractionPlanner.PlanCursor(");
            adapter.Should().Contain("ApplyPreviewPlan(");
            adapter.Should().Contain("ToGestureModifiers(");
            adapter.Should().NotContain("CanvasGestureSession");
            adapter.Should().NotContain("private enum GestureKind");
            adapter.Should().NotContain("_dragStartScreen");
            adapter.Should().NotContain("_resizeOrigX");
            adapter.Should().NotContain("new CanvasMoveRequest");
            adapter.Should().NotContain("new CanvasResizeRequest");
            adapter.Should().NotContain("new CanvasRotationRequest");
            adapter.Should().NotContain("ShapeGeometryAdjustmentPlanner.BuildMutationPlan");
            adapter.Should().NotContain("PictureCropAuthoringPlanner.BuildMutationPlan");
            adapter.Should().NotContain("private const long SmallNudgeEmu");
            adapter.Should().NotContain("ChartPointHitTester.TryHitTest");
            adapter.Should().NotContain("ZoomNavigationService.TryGetTargetSlideIndex");
            adapter.Should().NotContain("ShapeHitTester.MarqueeHitTest");
            adapter.Should().NotContain("_editor.Select(");
            adapter.Should().NotContain("_editor.ClearSelection(");
            adapter.Should().NotContain("_editor.MoveSelected(");
            adapter.Should().NotContain("_editor.ResizeShape(");
            adapter.Should().NotContain("_editor.RotateShape(");
            adapter.Should().NotContain("_editor.ApplySelectedTransforms(");
            adapter.Should().NotContain("_editor.DeleteSelected(");
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

    private static string ReadRepoFile(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
