using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGesturePlannerTests
{
    private const double EmuPerDip = 9525.0;

    [Theory]
    [InlineData(false, false, CanvasEscapeAction.None)]
    [InlineData(false, true, CanvasEscapeAction.CancelGesture)]
    [InlineData(true, false, CanvasEscapeAction.CancelFormatPainter)]
    [InlineData(true, true, CanvasEscapeAction.CancelFormatPainter)]
    public void ResolveEscapeAction_PreservesFormatPainterPrecedence(
        bool formatPainterActive,
        bool gestureActive,
        CanvasEscapeAction expected)
    {
        CanvasGesturePlanner.ResolveEscapeAction(formatPainterActive, gestureActive)
            .Should().Be(expected);
    }

    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    private static CanvasResizeRequest MakeResizeRequest(
        CanvasGestureHandleKind handle,
        CanvasGesturePoint currentScreen,
        double x = 100,
        double y = 50,
        double cx = 200,
        double cy = 100,
        double rotation = 0,
        bool snapToGrid = false,
        bool snapToShapes = false,
        bool bypassSnap = false,
        Slide? slide = null)
        => new(
            StartScreen: new CanvasGesturePoint(0, 0),
            CurrentScreen: currentScreen,
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            State: new CanvasResizeState(
                ShapeId: 1,
                XEmu: ToEmu(x),
                YEmu: ToEmu(y),
                CxEmu: ToEmu(cx),
                CyEmu: ToEmu(cy),
                RotationDeg: rotation,
                Handle: handle),
            CurrentSlide: slide,
            SnapToGrid: snapToGrid,
            SnapToShapes: snapToShapes,
            BypassSnap: bypassSnap);

    [Fact]
    public void ReduceDrag_BelowStartThreshold_DoesNotStartOrCommit()
    {
        var plan = CanvasGesturePlanner.ReduceDrag(new CanvasDragReducerRequest(
            StartScreen: new CanvasGesturePoint(10, 20),
            CurrentScreen: new CanvasGesturePoint(12.9, 20),
            DragStarted: false,
            StartThresholdPx: CanvasGesturePlanner.DefaultDragStartThresholdPx,
            CommitThresholdPx: CanvasGesturePlanner.MeaningfulDragCommitThresholdPx));

        plan.DragStarted.Should().BeFalse();
        plan.ShouldCommit.Should().BeFalse();
    }

    [Fact]
    public void ReduceDrag_CrossingStartThreshold_StartsAndCanCommit()
    {
        var plan = CanvasGesturePlanner.ReduceDrag(new CanvasDragReducerRequest(
            StartScreen: new CanvasGesturePoint(10, 20),
            CurrentScreen: new CanvasGesturePoint(13, 20),
            DragStarted: false,
            StartThresholdPx: CanvasGesturePlanner.DefaultDragStartThresholdPx,
            CommitThresholdPx: CanvasGesturePlanner.MeaningfulDragCommitThresholdPx));

        plan.DragStarted.Should().BeTrue();
        plan.ShouldCommit.Should().BeTrue();
    }

    [Fact]
    public void ReduceDrag_StartedButBelowCommitThreshold_DoesNotCommit()
    {
        var plan = CanvasGesturePlanner.ReduceDrag(new CanvasDragReducerRequest(
            StartScreen: new CanvasGesturePoint(10, 20),
            CurrentScreen: new CanvasGesturePoint(10.5, 20),
            DragStarted: true,
            StartThresholdPx: CanvasGesturePlanner.DefaultDragStartThresholdPx,
            CommitThresholdPx: CanvasGesturePlanner.MeaningfulDragCommitThresholdPx));

        plan.DragStarted.Should().BeTrue();
        plan.ShouldCommit.Should().BeFalse();
    }

    [Fact]
    public void ComputeResizeBounds_SeHandle_GrowsWithoutMovingOrigin()
    {
        var result = CanvasGesturePlanner.ComputeResizeBounds(
            MakeResizeRequest(CanvasGestureHandleKind.ResizeSE, new CanvasGesturePoint(50, 60)));

        (result.XEmu / EmuPerDip).Should().BeApproximately(100, 0.001);
        (result.YEmu / EmuPerDip).Should().BeApproximately(50, 0.001);
        (result.CxEmu / EmuPerDip).Should().BeApproximately(250, 0.001);
        (result.CyEmu / EmuPerDip).Should().BeApproximately(160, 0.001);
    }

    [Fact]
    public void ComputeResizeBounds_EHandle_ClampsToMinimumWidth()
    {
        var result = CanvasGesturePlanner.ComputeResizeBounds(
            MakeResizeRequest(
                CanvasGestureHandleKind.ResizeE,
                new CanvasGesturePoint(-200, 0),
                x: 0,
                y: 0,
                cx: 100,
                cy: 100));

        result.XEmu.Should().Be(0);
        result.CxEmu.Should().Be(CanvasGesturePlanner.MinimumShapeSizeEmu);
    }

    [Fact]
    public void ComputeResizeBounds_AltBypassSkipsGridSnap()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = ToEmu(96),
            ExtentCyEmu = ToEmu(96),
        });

        var snapped = CanvasGesturePlanner.ComputeResizeBounds(
            MakeResizeRequest(
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(47, 47),
                x: 0,
                y: 0,
                cx: 96,
                cy: 96,
                snapToGrid: true,
                slide: slide));

        var bypassed = CanvasGesturePlanner.ComputeResizeBounds(
            MakeResizeRequest(
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(47, 47),
                x: 0,
                y: 0,
                cx: 96,
                cy: 96,
                snapToGrid: true,
                bypassSnap: true,
                slide: slide));

        (snapped.CxEmu / EmuPerDip).Should().BeApproximately(144, 0.001);
        (bypassed.CxEmu / EmuPerDip).Should().BeApproximately(143, 0.001);
    }

    [Fact]
    public void ComputeResizeBounds_RotatedSeHandle_KeepsNwAnchorFixed()
    {
        var result = CanvasGesturePlanner.ComputeResizeBounds(
            MakeResizeRequest(
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(20, 20),
                x: 100,
                y: 100,
                cx: 100,
                cy: 100,
                rotation: 90));

        var (originalAnchorX, originalAnchorY) = Rotate(100, 100, 150, 150, 90);

        double newX = result.XEmu / EmuPerDip;
        double newY = result.YEmu / EmuPerDip;
        double newCx = result.CxEmu / EmuPerDip;
        double newCy = result.CyEmu / EmuPerDip;
        var (newAnchorX, newAnchorY) = Rotate(
            newX,
            newY,
            newX + newCx / 2,
            newY + newCy / 2,
            90);

        newCx.Should().BeGreaterThan(100);
        newAnchorX.Should().BeApproximately(originalAnchorX, 0.001);
        newAnchorY.Should().BeApproximately(originalAnchorY, 0.001);
    }

    [Fact]
    public void ComputeRotationAngle_ShiftSnapRoundsToFifteenDegrees()
    {
        double rawDegrees = 100;
        double vectorDegrees = rawDegrees - 90;
        double radians = vectorDegrees * Math.PI / 180.0;
        var current = new CanvasGesturePoint(
            100 + Math.Cos(radians) * 100,
            100 + Math.Sin(radians) * 100);

        var unsnapped = CanvasGesturePlanner.ComputeRotationAngle(new CanvasRotationRequest(
            CurrentScreen: current,
            CenterSlide: new CanvasGesturePoint(100, 100),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            OriginalRotationDeg: 0,
            SnapToFifteenDegrees: false));

        var snapped = CanvasGesturePlanner.ComputeRotationAngle(new CanvasRotationRequest(
            CurrentScreen: current,
            CenterSlide: new CanvasGesturePoint(100, 100),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            OriginalRotationDeg: 0,
            SnapToFifteenDegrees: true));

        unsnapped.Should().BeApproximately(100, 0.001);
        snapped.Should().Be(105);
    }

    [Fact]
    public void PlanMultiResize_SeHandleScalesEverySelectedShapeFromGroupBounds()
    {
        var states = new[]
        {
            new CanvasTransformShapeState(1, ToEmu(100), ToEmu(100), ToEmu(100), ToEmu(50), 0),
            new CanvasTransformShapeState(2, ToEmu(300), ToEmu(100), ToEmu(50), ToEmu(50), 15),
        };

        var plan = CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
            StartScreen: new CanvasGesturePoint(0, 0),
            CurrentScreen: new CanvasGesturePoint(50, 25),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            Handle: CanvasGestureHandleKind.ResizeSE,
            Shapes: states,
            CurrentSlide: null,
            SnapToGrid: false,
            SnapToShapes: false,
            BypassSnap: false));

        plan.Shapes.Should().HaveCount(2);
        plan.Shapes[0].XEmu.Should().Be(ToEmu(100));
        plan.Shapes[0].YEmu.Should().Be(ToEmu(100));
        plan.Shapes[0].CxEmu.Should().Be(ToEmu(120));
        plan.Shapes[0].CyEmu.Should().Be(ToEmu(75));
        plan.Shapes[1].XEmu.Should().Be(ToEmu(340));
        plan.Shapes[1].CxEmu.Should().Be(ToEmu(60));
        plan.Shapes[1].CyEmu.Should().Be(ToEmu(75));
        plan.Shapes[1].RotationDeg.Should().Be(15);
        plan.PreviewShapes.Should().HaveCount(2);
        plan.PreviewShapes[0].ScreenBounds.Should().Be(new SlideScreenRect(100, 100, 120, 75));
        plan.PreviewShapes[1].ScreenBounds.Should().Be(new SlideScreenRect(340, 100, 60, 75));
        plan.PreviewShapes[1].RotationDeg.Should().Be(15);
    }

    [Fact]
    public void PlanMultiRotate_UsesStartGripDeltaAndRotatesCentersAroundGroupCenter()
    {
        var states = new[]
        {
            new CanvasTransformShapeState(1, ToEmu(100), ToEmu(100), ToEmu(100), ToEmu(100), 10),
            new CanvasTransformShapeState(2, ToEmu(300), ToEmu(100), ToEmu(100), ToEmu(100), 20),
        };

        var plan = CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
            StartScreen: new CanvasGesturePoint(250, 50),
            CurrentScreen: new CanvasGesturePoint(300, 150),
            Transform: new SlideTransformCore(1, 0, 0, 1280, 720),
            Shapes: states,
            SnapToFifteenDegrees: false));

        plan.PreviewRotationDeg.Should().BeApproximately(90, 0.001);
        plan.Shapes[0].XEmu.Should().Be(ToEmu(200));
        plan.Shapes[0].YEmu.Should().Be(ToEmu(0));
        plan.Shapes[0].RotationDeg.Should().BeApproximately(100, 0.001);
        plan.Shapes[1].XEmu.Should().Be(ToEmu(200));
        plan.Shapes[1].YEmu.Should().Be(ToEmu(200));
        plan.Shapes[1].RotationDeg.Should().BeApproximately(110, 0.001);
        plan.PreviewShapes.Should().HaveCount(2);
        plan.PreviewShapes.Single(preview => preview.ShapeId == 1).ScreenBounds
            .Should().Be(new SlideScreenRect(200, 0, 100, 100));
        plan.PreviewShapes.Single(preview => preview.ShapeId == 2).ScreenBounds
            .Should().Be(new SlideScreenRect(200, 200, 100, 100));
        plan.PreviewShapes.Single(preview => preview.ShapeId == 1).RotationDeg
            .Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void OrientedBoundsToScreen_EnvelopesRotatedMemberForSelectionChrome()
    {
        var bounds = SlideCanvasGeometryPlanner.OrientedBoundsToScreen(
            left: 100,
            top: 100,
            width: 100,
            height: 50,
            rotationDeg: 90,
            transform: new SlideTransformCore(1, 0, 0, 1280, 720));

        bounds.Left.Should().BeApproximately(125, 0.001);
        bounds.Top.Should().BeApproximately(75, 0.001);
        bounds.Width.Should().BeApproximately(50, 0.001);
        bounds.Height.Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void WpfAndAvaloniaHandlers_DelegateGesturePolicyToSharedPlanner()
    {
        var session = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureSession.cs");
        var router = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureRouter.cs");
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "CanvasGestureHandler.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.cs");

        session.Should().Contain("CanvasGesturePlanner.ComputeResizeBounds");
        session.Should().Contain("CanvasGesturePlanner.ComputeRotationAngle");
        session.Should().Contain("CanvasGesturePlanner.PlanMultiResize");
        session.Should().Contain("CanvasGesturePlanner.PlanMultiRotate");
        session.Should().Contain("CanvasGesturePlanner.ReduceDrag");
        router.Should().Contain("CanvasGestureSession _session");
        router.Should().Contain("_session.PlanMove(");
        router.Should().Contain("_session.PlanMultiResize(");
        router.Should().Contain("_session.PlanMultiRotate(");
        router.Should().Contain("_editor.ApplySelectedTransforms(");
        router.Should().Contain("_editor.TryApplyFormatPainterToShape(");

        wpf.Should().Contain("CanvasGestureRouter _gestureRouter");
        wpf.Should().Contain("_gestureRouter.HandlePointerPressed(");
        wpf.Should().Contain("_gestureRouter.CompletePointer(");
        wpf.Should().Contain("BeginFormatPainter");
        wpf.Should().Contain("CancelFormatPainter");
        wpf.Should().Contain("ApplyPreviewPlan(");
        wpf.Should().NotContain("ToCanvasGestureHandle");
        wpf.Should().NotContain("private const long MinEmu");
        wpf.Should().NotContain("SlideTransformCore.UnRotateDelta(dxDip, dyDip");
        wpf.Should().NotContain("Math.Abs(ddxPx)");
        wpf.Should().NotContain("Math.Abs(ddyPx)");

        avalonia.Should().Contain("CanvasGestureRouter _gestureRouter");
        avalonia.Should().Contain("_gestureRouter.HandlePointerPressed(");
        avalonia.Should().Contain("_gestureRouter.CompletePointer(");
        avalonia.Should().Contain("BeginFormatPainter");
        avalonia.Should().Contain("CancelFormatPainter");
        avalonia.Should().Contain("ApplyPreviewPlan(");
        avalonia.Should().NotContain("ToCanvasGestureHandle");
        avalonia.Should().NotContain("private const long MinEmu");
        avalonia.Should().NotContain("SlideTransformCore.UnRotateDelta(dxDip, dyDip");
        avalonia.Should().NotContain("Math.Abs(ddxPx)");
        avalonia.Should().NotContain("Math.Abs(ddyPx)");
    }

    private static (double X, double Y) Rotate(double px, double py, double cx, double cy, double degrees)
    {
        if (degrees == 0) return (px, py);
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double dx = px - cx;
        double dy = py - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
