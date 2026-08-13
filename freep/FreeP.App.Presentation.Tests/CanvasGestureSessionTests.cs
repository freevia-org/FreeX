using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureSessionTests
{
    private const long EmusPerDip = 9525;

    [Fact]
    public void DragLifecycle_TransitionsOnceAndClearRemovesEveryPendingState()
    {
        var slide = SlideWithShape();
        var session = new CanvasGestureSession();
        session.BeginMove(slide, [1], new CanvasGesturePoint(10, 20));

        session.Kind.Should().Be(CanvasGestureKind.Move);
        session.IsActive.Should().BeTrue();
        session.HasPendingState.Should().BeTrue();
        session.TrackDrag(new CanvasGesturePoint(12.9, 20)).DragStarted.Should().BeFalse();
        session.TrackDrag(new CanvasGesturePoint(13, 20)).DragStarted.Should().BeTrue();
        session.ShouldCommit(new CanvasGesturePoint(13, 20)).Should().BeTrue();

        session.Clear();

        session.Kind.Should().Be(CanvasGestureKind.None);
        session.IsActive.Should().BeFalse();
        session.HasPendingState.Should().BeFalse();
        session.MoveStartShapes.Should().BeNull();
        session.ResizeState.Should().BeNull();
        session.MultiTransformStartShapes.Should().BeNull();
        session.Geometry.Should().BeNull();
    }

    [Fact]
    public void Resize_CapturesOriginalShapeStateAndOwnsPlannerRequestConstruction()
    {
        var slide = SlideWithShape();
        var shape = slide.Shapes.Single();
        var session = new CanvasGestureSession();

        session.BeginResize(
            slide,
            shape.Id,
            CanvasGestureHandleKind.ResizeSE,
            new CanvasGesturePoint(0, 0)).Should().BeTrue();
        shape.ExtentCxEmu = 999 * EmusPerDip;

        var bounds = session.PlanResize(
            new CanvasGesturePoint(20, 10),
            SlideTransformCore.Identity,
            slide,
            snapToGrid: false,
            snapToShapes: false,
            bypassSnap: false);

        bounds.Should().Be(new CanvasResizeBounds(
            10 * EmusPerDip,
            20 * EmusPerDip,
            120 * EmusPerDip,
            60 * EmusPerDip));
        session.ResizeState!.Value.CxEmu.Should().Be(100 * EmusPerDip);
    }

    [Fact]
    public void MultiResize_CapturesSelectionAndProducesSharedPreviewAndCommitTransforms()
    {
        var slide = SlideWithShape();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            OffsetXEmu = 210 * EmusPerDip,
            OffsetYEmu = 20 * EmusPerDip,
            ExtentCxEmu = 50 * EmusPerDip,
            ExtentCyEmu = 50 * EmusPerDip,
        });
        var session = new CanvasGestureSession();

        session.BeginMultiResize(
            slide,
            [1, 2],
            CanvasGestureHandleKind.ResizeSE,
            new CanvasGesturePoint(0, 0)).Should().BeTrue();
        var plan = session.PlanMultiResize(
            new CanvasGesturePoint(50, 25),
            SlideTransformCore.Identity,
            slide,
            snapToGrid: false,
            snapToShapes: false,
            bypassSnap: false);

        plan.Shapes.Should().HaveCount(2);
        plan.PreviewShapes.Should().HaveCount(2);
        plan.PreviewBounds.Should().NotBeNull();
        session.ResizeState!.Value.Handle.Should().Be(CanvasGestureHandleKind.ResizeSE);
    }

    [Fact]
    public void Rotate_CapturesCenterAndPlansShiftSnapping()
    {
        var slide = SlideWithShape();
        var session = new CanvasGestureSession();

        session.BeginRotate(slide, 1, new CanvasGesturePoint(60, 0)).Should().BeTrue();

        session.RotateCenterSlide.Should().Be(new CanvasGesturePoint(60, 45));
        session.PlanRotation(
                new CanvasGesturePoint(160, 45),
                SlideTransformCore.Identity,
                snapToFifteenDegrees: true)
            .Should().Be(90);
    }

    [Fact]
    public void GeometryAndMarquee_KeepPortableStatusWithoutNativePointerTypes()
    {
        var session = new CanvasGestureSession();
        var bounds = new LayoutRect(10, 20, 100, 50);

        session.BeginGeometryAdjustment(
            7,
            "adj1",
            bounds,
            new CanvasGesturePoint(30, 40));
        session.Geometry.Should().Be(new CanvasGeometryGestureState(
            7,
            "adj1",
            bounds,
            new CanvasGesturePoint(30, 40)));

        session.BeginMarquee(
            new CanvasGesturePoint(1, 2),
            new CanvasGesturePoint(3, 4));
        session.Kind.Should().Be(CanvasGestureKind.Marquee);
        session.MarqueeStartSlide.Should().Be(new CanvasGesturePoint(3, 4));
        session.Geometry.Should().BeNull();
    }

    [Fact]
    public void GeometrySession_ValidatesPreviewsAndCommitsThroughOneCommandBoundary()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 9,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            ExtentCxEmu = 100 * EmusPerDip,
            ExtentCyEmu = 50 * EmusPerDip,
        };
        slide.Shapes.Add(shape);
        var gesture = new CanvasGestureSession();
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));

        gesture.TryBeginGeometryAdjustment(
            slide,
            presentation,
            shape.Id,
            "adj",
            new CanvasGesturePoint(18, 0)).Should().BeTrue();
        gesture.PlanGeometryPreview(slide, new CanvasGesturePoint(40, 0))
            .Should().Be(new CanvasGeometryPreviewPlan("adj", new CanvasGesturePoint(40, 0)));
        gesture.CommitGeometryAdjustment(
            editor,
            slide,
            new CanvasGesturePoint(40, 0)).Should().BeTrue();

        shape.PresetGeometryAdjustments["adj"].Should().Be(50000);
        editor.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void SharedPointerPolicies_OwnSelectedBodyHitDoubleClickAndNudgeDecisions()
    {
        var slide = SlideWithShape();
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(slide);

        CanvasGesturePlanner.HitSelectedShapeBody(
            slide,
            presentation,
            [1],
            new CanvasGesturePoint(20, 30)).Should().BeTrue();
        CanvasGesturePlanner.HitSelectedShapeBody(
            slide,
            presentation,
            [1],
            new CanvasGesturePoint(200, 300)).Should().BeFalse();
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(new SlideShape())
            .Should().BeTrue();
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(
            new SlideShape { TextBody = new TextBody() }).Should().BeFalse();
        CanvasGesturePlanner.ResolveNudgeStep(useLargeStep: false)
            .Should().Be(91440);
        CanvasGesturePlanner.ResolveNudgeStep(useLargeStep: true)
            .Should().Be(914400);
    }

    [Fact]
    public void SlideShowPointerPlanner_UsesOneMappingForClickHoverTriggerAndInk()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var hyperlink = new Hyperlink { Url = "https://example.com" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 100 * EmusPerDip,
            ExtentCyEmu = 100 * EmusPerDip,
            Hyperlink = hyperlink,
        });
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = 100,
            TriggerShapeId = 42,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
        });
        var pointer = new SlideShowCanvasPointer(
            96,
            96,
            1920,
            1080,
            new SlideShowSlideMetrics(960, 540));

        SlideShowPointerInteractionPlanner.MapToSlide(pointer)
            .Should().Be(new SlideShowPoint(48, 48));
        SlideShowPointerInteractionPlanner.PlanClick(slide, presentation, pointer)
            .Should().Match<SlideShowPointerClickIntent>(intent =>
                intent.Kind == SlideShowPointerClickIntentKind.Trigger &&
                intent.TriggerShapeId == 42);
        SlideShowPointerInteractionPlanner.HitTestHyperlink(slide, pointer)
            .Should().BeSameAs(hyperlink);
        SlideShowPointerInteractionPlanner.HitTestTriggerShape(slide, pointer)
            .Should().Be(42);
        SlideShowPointerInteractionPlanner.MapInkPoint(pointer)
            .Should().Be(new SlideShowInkPoint(48, 48));
    }

    private static Slide SlideWithShape()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = 10 * EmusPerDip,
            OffsetYEmu = 20 * EmusPerDip,
            ExtentCxEmu = 100 * EmusPerDip,
            ExtentCyEmu = 50 * EmusPerDip,
        });
        return slide;
    }
}
